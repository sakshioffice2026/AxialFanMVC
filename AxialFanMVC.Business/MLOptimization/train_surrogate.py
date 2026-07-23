"""
Part 2 of the pipeline — run this in Google Colab.

Input:  axialfan_synthetic_training_*.csv  (from DataFactoryController.GenerateTrainingData)
Output: surrogate.onnx                     (drop into AxialFanMVC/MLModels/, same folder
                                             as efficiency_correction.onnx)

Why RandomForest and not a neural net: you said no deep-ML background, and
tabular data with a few hundred thousand rows from a synthetic generator is
exactly where RandomForest is the right default — no normalization step to
get wrong, no learning-rate tuning, robust to the outlier/degenerate rows
the data factory already flagged via is_feasible. A neural net earns its
complexity later, once you have real (not synthetic) calibration-scale data
and want the physics-residual loss discussed earlier for the correction model
— that's a DIFFERENT model (efficiency_correction.onnx) with a different job.
This one is a plain performance predictor: geometry+duty -> outcomes.

Colab setup cell (run first):
    !pip install -q scikit-learn skl2onnx onnx pandas
"""

import pandas as pd
import numpy as np
from sklearn.ensemble import RandomForestRegressor
from sklearn.model_selection import train_test_split
from sklearn.metrics import mean_absolute_error, r2_score
from skl2onnx import convert_sklearn
from skl2onnx.common.data_types import FloatTensorType
import onnx

# ── 1. Load ──────────────────────────────────────────────────────────────
# Upload the CSV via Colab's file picker, or mount Drive and point here.
CSV_PATH = "axialfan_synthetic_training.csv"
df = pd.read_csv(CSV_PATH)

print(f"Loaded {len(df)} rows, {df['is_feasible'].sum()} feasible "
      f"({100 * df['is_feasible'].mean():.1f}%)")

# Train ONLY on feasible rows. Infeasible rows are exactly the ones with
# NaN/garbage in efficiency, cost, safety_factor — training on them would
# teach the regressor to output garbage in those input regions instead of
# just never proposing them. Feasibility itself is enforced separately, as
# a hard constraint in the optimizer (Part 3), not as a regression target.
df = df[df["is_feasible"] == 1].copy()

# blade_profile is categorical text — one-hot encode. Keep the resulting
# column order; the optimizer service needs to build feature vectors in
# this exact order too.
df = pd.get_dummies(df, columns=["blade_profile"], prefix="profile")

FEATURE_COLS = [
    "tip_diameter_mm", "hub_ratio", "blade_angle_deg", "blade_count",
    "speed_rpm", "flow_rate_m3s", "total_pressure_pa", "temperature_celsius",
] + [c for c in df.columns if c.startswith("profile_")]

TARGET_COLS = ["efficiency_pct", "noise_dba", "cost_total", "safety_factor"]

print("Feature columns (ORDER MATTERS — copy this list into optimizer_service.py):")
print(FEATURE_COLS)

X = df[FEATURE_COLS].astype(np.float32).values
y = df[TARGET_COLS].astype(np.float32).values

X_train, X_val, y_train, y_val = train_test_split(X, y, test_size=0.2, random_state=42)

# ── 2. Train ─────────────────────────────────────────────────────────────
# n_estimators/max_depth kept modest on purpose — this needs to run in
# milliseconds inside an NSGA-II loop that evaluates thousands of
# candidates per generation. A 500-tree forest is accurate but too slow
# there; 150 shallow-ish trees is a deliberate accuracy/latency trade-off.
model = RandomForestRegressor(
    n_estimators=150,
    max_depth=14,
    min_samples_leaf=3,
    n_jobs=-1,
    random_state=42,
)
model.fit(X_train, y_train)

# ── 3. Validate — DO NOT SKIP THIS ─────────────────────────────────────
pred = model.predict(X_val)
for i, col in enumerate(TARGET_COLS):
    mae = mean_absolute_error(y_val[:, i], pred[:, i])
    r2 = r2_score(y_val[:, i], pred[:, i])
    print(f"{col:20s}  MAE={mae:9.3f}  R2={r2:.4f}")

# Sanity thresholds — if these fail, don't export. Either the sample count
# was too low, the bounds are too wide for the actual physics, or a feature
# is missing. An R2 below ~0.85 on efficiency/noise means the optimizer
# will be searching against a surrogate that doesn't actually track the
# real engine, and every downstream candidate becomes untrustworthy.
assert r2_score(y_val[:, 0], pred[:, 0]) > 0.85, "Efficiency R2 too low — do not export."
assert r2_score(y_val[:, 3], pred[:, 3]) > 0.85, "Safety factor R2 too low — do not export."

# ── 4. Export to ONNX ────────────────────────────────────────────────────
initial_type = [("input", FloatTensorType([None, len(FEATURE_COLS)]))]
onnx_model = convert_sklearn(model, initial_types=initial_type, target_opset=15)

with open("surrogate.onnx", "wb") as f:
    f.write(onnx_model.SerializeToString())

print(f"\nExported surrogate.onnx — {len(FEATURE_COLS)} inputs, {len(TARGET_COLS)} outputs.")
print("Download this file, then either:")
print("  (a) drop it in AxialFanMVC/MLModels/ and load it from C# via ONNX Runtime, or")
print("  (b) use it directly in optimizer_service.py (Part 3) via onnxruntime — same file.")