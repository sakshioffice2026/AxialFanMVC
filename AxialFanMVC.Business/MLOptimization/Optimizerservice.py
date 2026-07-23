"""
Part 3 — the live optimizer, called by the C# background worker.

    pip install fastapi uvicorn onnxruntime pymoo numpy pydantic

Run:
    uvicorn optimizer_service:app --host 0.0.0.0 --port 8088

This is a separate process from the ASP.NET Core app on purpose — pymoo/
NSGA-II and onnxruntime are Python-ecosystem tools, and C# already has a
clean way to call out to an HTTP service asynchronously (the background
worker in Part C#). Bolting a Python-in-process interop layer into the
.NET app would be more fragile than one HTTP call.
"""

from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field
import numpy as np
import onnxruntime as ort
from pymoo.core.problem import Problem
from pymoo.algorithms.moo.nsga2 import NSGA2
from pymoo.optimize import minimize
from pymoo.operators.sampling.rnd import FloatRandomSampling
from pymoo.operators.crossover.sbx import SBX
from pymoo.operators.mutation.pm import PM

app = FastAPI(title="AxialFan Design Optimizer")

# MUST match FEATURE_COLS order printed by train_surrogate.py exactly.
# Hardcoded here rather than inferred from the ONNX file's metadata because
# ONNX doesn't reliably preserve column names through skl2onnx — order is
# the only contract that survives, so it has to be kept in sync by hand
# whenever the feature set changes.
BLADE_PROFILES = ["Flat Plate", "NACA 0012", "NACA 2412", "NACA 4412"]  # alphabetical -> get_dummies order
FEATURE_ORDER = (
    ["tip_diameter_mm", "hub_ratio", "blade_angle_deg", "blade_count",
     "speed_rpm", "flow_rate_m3s", "total_pressure_pa", "temperature_celsius"]
    + [f"profile_{p}" for p in BLADE_PROFILES]
)

# Same envelope as SyntheticDataFactory.Bounds.Default — the surrogate has
# never seen outside this box, so the optimizer is not allowed to search
# outside it either. If you widen the C# bounds, widen these too and retrain.
GEOM_BOUNDS = {
    "tip_diameter_mm": (300.0, 3000.0),
    "hub_ratio": (0.30, 0.60),
    "blade_angle_deg": (10.0, 35.0),
    "speed_rpm": (500.0, 3000.0),
}
BLADE_COUNT_OPTIONS = [4, 6, 8, 10, 12]

_session = ort.InferenceSession("surrogate.onnx", providers=["CPUExecutionProvider"])
_output_name = _session.get_outputs()[0].name


class OptimizeRequest(BaseModel):
    flow_rate_m3s: float = Field(..., gt=0)
    total_pressure_pa: float = Field(..., gt=0)
    temperature_celsius: float = 25.0
    min_efficiency_pct: float | None = None
    max_noise_dba: float | None = None
    max_motor_power_kw: float | None = None   # not predicted directly — see note below
    max_tip_diameter_mm: float | None = None
    min_safety_factor: float = 1.2            # margin above the raw >=1.0 pass/fail


class Candidate(BaseModel):
    label: str
    tip_diameter_mm: float
    hub_ratio: float
    blade_angle_deg: float
    blade_count: int
    speed_rpm: float
    blade_profile: str
    predicted_efficiency_pct: float
    predicted_noise_dba: float
    predicted_cost_total: float
    predicted_safety_factor: float


def predict(X: np.ndarray) -> np.ndarray:
    """X: (n, 12) raw feature rows in FEATURE_ORDER. Returns (n, 4):
    [efficiency_pct, noise_dba, cost_total, safety_factor]."""
    return _session.run([_output_name], {"input": X.astype(np.float32)})[0]


def build_feature_rows(x_decision: np.ndarray, req: OptimizeRequest) -> np.ndarray:
    """x_decision columns: [tip_diameter_mm, hub_ratio, blade_angle_deg,
    speed_rpm, blade_count_index, profile_index] — decoded from continuous
    pymoo variables into the surrogate's actual one-hot feature layout."""
    n = x_decision.shape[0]
    rows = np.zeros((n, len(FEATURE_ORDER)), dtype=np.float32)

    rows[:, 0] = x_decision[:, 0]  # tip_diameter_mm
    rows[:, 1] = x_decision[:, 1]  # hub_ratio
    rows[:, 2] = x_decision[:, 2]  # blade_angle_deg

    blade_count_idx = np.clip(x_decision[:, 4].astype(int), 0, len(BLADE_COUNT_OPTIONS) - 1)
    rows[:, 3] = np.array([BLADE_COUNT_OPTIONS[i] for i in blade_count_idx])

    rows[:, 4] = x_decision[:, 3]  # speed_rpm
    rows[:, 5] = req.flow_rate_m3s
    rows[:, 6] = req.total_pressure_pa
    rows[:, 7] = req.temperature_celsius

    profile_idx = np.clip(x_decision[:, 5].astype(int), 0, len(BLADE_PROFILES) - 1)
    for i, p_idx in enumerate(profile_idx):
        rows[i, 8 + p_idx] = 1.0

    return rows


class FanDesignProblem(Problem):
    """Decision vars: [tip_dia, hub_ratio, blade_angle, rpm, blade_count_idx, profile_idx].
    Objectives (all minimized — efficiency negated): -efficiency, noise, cost.
    Constraint: min_safety_factor - predicted_safety_factor <= 0 (pymoo convention: g(x) <= 0 is feasible),
    plus any user-supplied hard limits from the schema fields."""

    def __init__(self, req: OptimizeRequest):
        self.req = req
        xl = [GEOM_BOUNDS["tip_diameter_mm"][0], GEOM_BOUNDS["hub_ratio"][0],
              GEOM_BOUNDS["blade_angle_deg"][0], GEOM_BOUNDS["speed_rpm"][0], 0, 0]
        xu = [GEOM_BOUNDS["tip_diameter_mm"][1], GEOM_BOUNDS["hub_ratio"][1],
              GEOM_BOUNDS["blade_angle_deg"][1], GEOM_BOUNDS["speed_rpm"][1],
              len(BLADE_COUNT_OPTIONS) - 1, len(BLADE_PROFILES) - 1]

        n_constraints = 1  # safety factor, always on
        if req.min_efficiency_pct is not None:
            n_constraints += 1
        if req.max_noise_dba is not None:
            n_constraints += 1
        if req.max_tip_diameter_mm is not None:
            n_constraints += 1

        super().__init__(n_var=6, n_obj=3, n_constr=n_constraints, xl=xl, xu=xu)

    def _evaluate(self, x, out, *args, **kwargs):
        pred = predict(build_feature_rows(x, self.req))
        efficiency, noise, cost, safety = pred[:, 0], pred[:, 1], pred[:, 2], pred[:, 3]

        out["F"] = np.column_stack([-efficiency, noise, cost])

        constraints = [self.req.min_safety_factor - safety]
        if self.req.min_efficiency_pct is not None:
            constraints.append(self.req.min_efficiency_pct - efficiency)
        if self.req.max_noise_dba is not None:
            constraints.append(noise - self.req.max_noise_dba)
        if self.req.max_tip_diameter_mm is not None:
            constraints.append(x[:, 0] - self.req.max_tip_diameter_mm)

        out["G"] = np.column_stack(constraints)


@app.post("/optimize", response_model=list[Candidate])
def optimize(req: OptimizeRequest):
    if req.flow_rate_m3s < 0.5 - 1e-6 or req.flow_rate_m3s > 20 + 1e-6:
        raise HTTPException(422, "flow_rate_m3s outside the surrogate's trained range (0.5-20 m3/s) — "
                                  "predictions here would be an extrapolation, not a result.")
    if req.total_pressure_pa < 50 or req.total_pressure_pa > 2000:
        raise HTTPException(422, "total_pressure_pa outside the surrogate's trained range (50-2000 Pa).")

    problem = FanDesignProblem(req)
    algorithm = NSGA2(
        pop_size=100,
        sampling=FloatRandomSampling(),
        crossover=SBX(prob=0.9, eta=15),
        mutation=PM(eta=20),
        eliminate_duplicates=True,
    )
    result = minimize(problem, algorithm, ("n_gen", 80), seed=1, verbose=False)

    if result.X is None or len(result.X) == 0:
        raise HTTPException(
            409,
            "No feasible design found within the trained geometry bounds for these constraints. "
            "The constraints (efficiency/noise/tip diameter/safety factor) may be mutually "
            "unsatisfiable at this duty point, or too tight for the searched envelope."
        )

    pareto_X = result.X
    pareto_pred = predict(build_feature_rows(pareto_X, req))

    def to_candidate(idx: int, label: str) -> Candidate:
        row = pareto_X[idx]
        p = pareto_pred[idx]
        blade_count = BLADE_COUNT_OPTIONS[int(np.clip(row[4], 0, len(BLADE_COUNT_OPTIONS) - 1))]
        profile = BLADE_PROFILES[int(np.clip(row[5], 0, len(BLADE_PROFILES) - 1))]
        return Candidate(
            label=label,
            tip_diameter_mm=round(float(row[0]), 1),
            hub_ratio=round(float(row[1]), 3),
            blade_angle_deg=round(float(row[2]), 2),
            blade_count=blade_count,
            speed_rpm=round(float(row[3]), 0),
            blade_profile=profile,
            predicted_efficiency_pct=round(float(p[0]), 2),
            predicted_noise_dba=round(float(p[1]), 1),
            predicted_cost_total=round(float(p[2]), 2),
            predicted_safety_factor=round(float(p[3]), 2),
        )

    idx_budget = int(np.argmin(pareto_pred[:, 2]))       # min cost
    idx_silent = int(np.argmin(pareto_pred[:, 1]))       # min noise
    idx_premium = int(np.argmax(pareto_pred[:, 0]))      # max efficiency

    return [
        to_candidate(idx_budget, "Budget Variant"),
        to_candidate(idx_silent, "Silent Variant"),
        to_candidate(idx_premium, "Premium Variant"),
    ]