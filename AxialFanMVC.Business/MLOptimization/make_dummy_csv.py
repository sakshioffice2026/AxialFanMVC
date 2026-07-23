import numpy as np
import pandas as pd

np.random.seed(42)
n = 5000

df = pd.DataFrame({
    "tip_diameter_mm": np.random.uniform(300, 3000, n),
    "hub_ratio": np.random.uniform(0.30, 0.60, n),
    "blade_angle_deg": np.random.uniform(10, 35, n),
    "blade_count": np.random.choice([4, 6, 8, 10, 12], n),
    "speed_rpm": np.random.uniform(500, 3000, n),
    "blade_profile": np.random.choice(["NACA 4412", "NACA 2412", "NACA 0012", "Flat Plate"], n),
    "flow_rate_m3s": np.random.uniform(0.5, 20, n),
    "total_pressure_pa": np.random.uniform(50, 2000, n),
    "temperature_celsius": np.random.uniform(-10, 50, n),
})

# fake but reasonable-looking targets, just so the pipeline runs end-to-end
df["efficiency_pct"] = 60 + 0.01 * df["blade_angle_deg"] * df["blade_count"] + np.random.normal(0, 2, n)
df["shaft_power_kw"] = df["flow_rate_m3s"] * df["total_pressure_pa"] / 1000
df["noise_dba"] = 60 + 0.02 * df["speed_rpm"] / 100 + np.random.normal(0, 1, n)
df["safety_factor"] = np.random.uniform(1.0, 3.0, n)
df["cost_total"] = df["tip_diameter_mm"] * 2 + df["blade_count"] * 500
df["is_feasible"] = 1
df["status"] = "ok"

df.to_csv("axialfan_synthetic_training.csv", index=False)
print("Created axialfan_synthetic_training.csv with", n, "rows")
