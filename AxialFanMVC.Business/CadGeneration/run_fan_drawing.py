"""
run_fan_drawing.py
Runs fan_technical_drawing.py with your own dimensions.
Usage:
    pip install ezdxf
    python run_fan_drawing.py
"""
from fan_technical_drawing import generate_fan_drawing

dims = {
    "FanDiameterMm": 400,
    "HubDiameterMm": 240,
    "BladeCount": 6,
    "CasingLengthMm": 114,
    "MountingFrameSizeMm": 507,
    "WallThicknessMm": 3,
    "FlangeWidthMm": 15,
}

generate_fan_drawing(dims, "fan_technical_drawing.dxf")