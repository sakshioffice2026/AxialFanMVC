# -*- coding: utf-8 -*-
"""
FastAPI endpoint for FreeCAD blade/hub CAD generation.
Follows CyclonApp pattern: calls freecadcmd.exe via subprocess.
Data passed via environment variables, not CLI args.

Run:
    pip install -r requirements.txt
    uvicorn cad_service:app --host 0.0.0.0 --port 8002

Called by C# ExportController via HTTP POST.
"""

from fastapi import FastAPI, HTTPException, BackgroundTasks
from pydantic import BaseModel, Field
from typing import Optional
import subprocess
import json
from pathlib import Path
import uuid
import os
import shutil
import asyncio

app = FastAPI(title="AxialFan CAD Generator", version="1.0")

# Configure paths
SERVICE_DIR = Path(__file__).parent
CAD_GENERATOR_SCRIPT = SERVICE_DIR / "blade_cad_generator.py"
CAD_OUTPUT_DIR = Path(os.getenv("CAD_OUTPUT_DIR", "/tmp/axial_fan_cad"))
CAD_OUTPUT_DIR.mkdir(parents=True, exist_ok=True)

# FreeCAD command path - customize for your installation
FREECAD_CMD_PATH = os.getenv(
    "FREECAD_CMD_PATH",
    r"C:\Program Files\FreeCAD 1.1\bin\freecadcmd.exe"
)


class BladeCadRequest(BaseModel):
    """Blade CAD generation request - mirrors C# DesignInput."""
    tip_radius_m: float = Field(..., gt=0, description="Fan tip radius in metres")
    hub_ratio: float = Field(0.45, ge=0.15, le=0.85, description="Hub radius / tip radius")
    blade_count: int = Field(6, ge=1, le=20, description="Number of blades")
    blade_angle_deg: float = Field(0.0, description="Retained for compatibility (not used)")
    profile_coordinate_json: Optional[str] = Field(None, description="Airfoil profile as JSON array")
    rpm: float = Field(1500.0, ge=0, description="Rotational speed")
    axial_velocity_ms: float = Field(5.0, ge=0, description="Inlet axial velocity")
    span_stations: int = Field(6, ge=2, le=20, description="Radial loft stations")
    target_solidity: float = Field(0.5, gt=0, description="Chord solidity target")
    stagger_angle_deg: float = Field(30.0, description="Blade stagger angle for DXF")


class BladeCadResponse(BaseModel):
    """Response with generated file paths."""
    case_id: str
    step_file: str
    dxf_file: str
    obj_file: str
    message: str


@app.post("/generate-blade-cad", response_model=BladeCadResponse)
async def generate_blade_cad(req: BladeCadRequest, bg_tasks: BackgroundTasks):
    """
    Generate blade/hub CAD (STEP, DXF, OBJ) from design parameters.
    
    Runs FreeCAD as separate process (freecadcmd.exe) via subprocess.
    Input passed via environment variables (not CLI args, since freecadcmd
    tries to interpret CLI args as file paths).
    
    Example:
        POST /generate-blade-cad
        {
            "tip_radius_m": 0.2,
            "hub_ratio": 0.45,
            "blade_count": 6,
            "rpm": 1500,
            "axial_velocity_ms": 5.0
        }
    """
    case_id = str(uuid.uuid4())[:8]
    case_dir = CAD_OUTPUT_DIR / case_id
    case_dir.mkdir(parents=True, exist_ok=True)

    print(f"[CAD] Starting generation for case {case_id}", flush=True)
    print(f"[CAD] Script: {CAD_GENERATOR_SCRIPT}", flush=True)
    print(f"[CAD] FreeCAD: {FREECAD_CMD_PATH}", flush=True)

    if not CAD_GENERATOR_SCRIPT.exists():
        raise HTTPException(
            status_code=500,
            detail=f"CAD generator script not found: {CAD_GENERATOR_SCRIPT}"
        )

    if not Path(FREECAD_CMD_PATH).exists():
        raise HTTPException(
            status_code=500,
            detail=f"FreeCAD command not found: {FREECAD_CMD_PATH}. Set FREECAD_CMD_PATH env var."
        )

    # Prepare parameters for environment variable
    params = {
        "case_id": case_id,
        "tip_radius_m": req.tip_radius_m,
        "hub_ratio": req.hub_ratio,
        "blade_count": req.blade_count,
        "blade_angle_deg": req.blade_angle_deg,
        "profile_coordinate_json": req.profile_coordinate_json,
        "rpm": req.rpm,
        "axial_velocity_ms": req.axial_velocity_ms,
        "span_stations": req.span_stations,
        "target_solidity": req.target_solidity,
        "stagger_angle_deg": req.stagger_angle_deg
    }

    try:
        # Setup environment
        env = os.environ.copy()
        env["CAD_BLADE_PARAMS_JSON"] = json.dumps(params)
        env["CAD_OUTPUT_DIR"] = str(case_dir)

        # Execute FreeCAD script via subprocess
        print(f"[CAD] Subprocess starting...", flush=True)

        exec_code = (
            f"exec(open(r'{CAD_GENERATOR_SCRIPT}', encoding='utf-8-sig').read())"
        )

        proc = subprocess.run(
            [FREECAD_CMD_PATH, "-c", exec_code],
            env=env,
            capture_output=True,
            text=True,
            timeout=180  # 3 minute timeout for CAD generation
        )

        print(f"[CAD] Subprocess completed with return code: {proc.returncode}", flush=True)

        if proc.returncode != 0:
            print(f"[CAD] ERROR: Non-zero exit code", flush=True)
            print(f"[CAD] STDOUT:\n{proc.stdout}", flush=True)
            print(f"[CAD] STDERR:\n{proc.stderr}", flush=True)
            if case_dir.exists():
                shutil.rmtree(case_dir)
            raise HTTPException(
                status_code=500,
                detail=f"CAD generation failed (exit code {proc.returncode})"
            )

        if proc.stdout:
            print(f"[CAD] STDOUT:\n{proc.stdout}", flush=True)
        if proc.stderr:
            print(f"[CAD] STDERR:\n{proc.stderr}", flush=True)

        # Verify output files exist
        step_file = case_dir / f"fan_{case_id}.step"
        dxf_file = case_dir / f"fan_{case_id}.dxf"
        obj_file = case_dir / f"fan_{case_id}.obj"

        if not step_file.exists():
            print(f"[CAD] ERROR: Expected STEP file not found: {step_file}", flush=True)
            if case_dir.exists():
                shutil.rmtree(case_dir)
            raise HTTPException(status_code=500, detail="STEP file not generated")

        print(f"[CAD] SUCCESS: Generated files in {case_dir}", flush=True)

        # Schedule cleanup after 24 hours
        bg_tasks.add_task(_cleanup_task, case_dir, delay_seconds=86400)

        return BladeCadResponse(
            case_id=case_id,
            step_file=str(step_file),
            dxf_file=str(dxf_file),
            obj_file=str(obj_file),
            message=f"Generated: {req.blade_count} blades, O{req.tip_radius_m*2:.3f}m, {int(req.rpm)} RPM"
        )

    except subprocess.TimeoutExpired:
        print(f"[CAD] ERROR: Subprocess timed out after 180s", flush=True)
        if case_dir.exists():
            shutil.rmtree(case_dir)
        raise HTTPException(status_code=500, detail="CAD generation timed out (180s)")

    except Exception as e:
        print(f"[CAD] ERROR: {e}", flush=True)
        if case_dir.exists():
            shutil.rmtree(case_dir)
        raise HTTPException(status_code=500, detail=f"CAD generation failed: {str(e)}")


@app.get("/blade-cad/{case_id}/files")
async def get_blade_files(case_id: str):
    """List generated files for a case."""
    case_dir = CAD_OUTPUT_DIR / case_id
    if not case_dir.exists():
        raise HTTPException(status_code=404, detail=f"Case {case_id} not found")

    files = {
        "step": [str(f) for f in case_dir.glob("*.step")],
        "dxf": [str(f) for f in case_dir.glob("*.dxf")],
        "obj": [str(f) for f in case_dir.glob("*.obj")]
    }
    return {"case_id": case_id, "files": files}


@app.delete("/blade-cad/{case_id}")
async def delete_case(case_id: str):
    """Manually delete a case and its files."""
    case_dir = CAD_OUTPUT_DIR / case_id
    if not case_dir.exists():
        raise HTTPException(status_code=404, detail=f"Case {case_id} not found")

    try:
        shutil.rmtree(case_dir)
        return {"message": f"Deleted case {case_id}"}
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Deletion failed: {str(e)}")


@app.get("/health")
async def health():
    """Health check - called by C# before requests."""
    freecad_ok = Path(FREECAD_CMD_PATH).exists()
    script_ok = CAD_GENERATOR_SCRIPT.exists()
    return {
        "status": "ok" if freecad_ok and script_ok else "degraded",
        "service": "AxialFan CAD Generator",
        "version": "1.0",
        "freecad_path": str(FREECAD_CMD_PATH),
        "freecad_found": freecad_ok,
        "script_found": script_ok
    }


async def _cleanup_task(case_dir: Path, delay_seconds: int = 86400):
    """Background cleanup task."""
    await asyncio.sleep(delay_seconds)
    if case_dir.exists():
        shutil.rmtree(case_dir)
        print(f"[CAD] Auto-cleaned {case_dir}")


if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=8002)