#!/usr/bin/env python3
"""
Axial Fan Parametric CAD Generator
----------------------------------
Generates complete axial fan assembly in FreeCAD with parametric parameters.
Exports 3D STEP model and 2D DXF technical drawings.

Usage:
    freecadcmd -c "exec(open('axial_fan_parametric.py').read())"
    
    # Or via Python console in FreeCAD:
    import axial_fan_parametric
    axial_fan_parametric.generate(
        fan_diameter=0.5,
        hub_diameter=0.15,
        blade_count=6,
        blade_angle_deg=30,
        casing_length=0.3,
        shaft_diameter=0.025,
        flange_od=0.2,
        flange_id=0.12,
        mounting_holes=4,
        mounting_diameter=0.01
    )
"""

import math
import os
import sys
import json
from pathlib import Path


def create_cylinder(doc, name, radius, height, position=None, rotation=None):
    """Create a cylindrical solid."""
    obj = doc.addObject("Part::Feature", name)
    cyl = Part.makeCylinder(radius, height, Vector(0, 0, -height/2) if rotation is None else rotation)
    if rotation:
        obj.Shape = cyl.rotate(rotation)
    else:
        obj.Shape = cyl
    if position:
        obj.Position = position
    return obj


def create_disk(doc, name, outer_radius, inner_radius, thickness, position=None):
    """Create a cylindrical disk (annulus)."""
    obj = doc.addObject("Part::Feature", name)
    disk = Part.makeCylinder(outer_radius, thickness, Vector(0, 0, -thickness/2))
    # Cut inner cylinder
    inner_cut = Part.makeCylinder(inner_radius, thickness, Vector(0, 0, -thickness/2))
    obj.Shape = obj.Shape.cut(inner_cut)
    if position:
        obj.Position = position
    return obj


def generate_airfoil_naca(camber=0.04, camber_pos=0.4, thickness=0.12, points=100):
    """Generate NACA 4-digit airfoil profile coordinates."""
    xs = [0.5 * (1 - math.cos(math.pi * i / points)) for i in range(points + 1)]
    
    def yt(x):
        return 5 * thickness * (0.2969 * math.sqrt(x) - 0.1260 * x - 0.3516 * x**2
                                 + 0.2843 * x**3 - 0.1015 * x**4)
    
    def camber_line(x):
        if camber == 0 or camber_pos == 0:
            return 0.0, 0.0
        if x < camber_pos:
            yc = camber / (camber_pos**2) * (2 * camber_pos * x - x**2)
            dyc = 2 * camber / (camber_pos**2) * (camber_pos - x)
        else:
            p1 = 1 - camber_pos
            yc = camber / (p1**2) * ((1 - 2 * camber_pos) + 2 * camber_pos * x - x**2)
            dyc = 2 * camber / (p1**2) * (camber_pos - x)
        return yc, dyc
    
    points_list = []
    for x in xs:
        yc, dyc = camber_line(x)
        theta = math.atan(dyc)
        y_t = yt(x)
        upper = (x - y_t * math.sin(theta), yc + y_t * math.cos(theta))
        lower = (x + y_t * math.sin(theta), yc - y_t * math.cos(theta))
        points_list.extend([upper, lower])
    
    return points_list


def generate(fan_diameter=0.5, hub_diameter=0.15, blade_count=6,
             blade_angle_deg=30, casing_length=0.3, shaft_diameter=0.025,
             flange_od=0.2, flange_id=0.12, mounting_holes=4,
             mounting_diameter=0.01, span_stations=6, output_dir="/tmp/axial_fan"):
    """
    Generate complete axial fan assembly parametrically.
    
    Parameters (all in meters unless noted):
    - fan_diameter: Overall fan tip diameter
    - hub_diameter: Hub/core diameter
    - blade_count: Number of blades
    - blade_angle_deg: Blade angle at tip (degrees)
    - casing_length: Casing/housing length
    - shaft_diameter: Shaft diameter
    - flange_od: Flange outer diameter
    - flange_id: Flange inner diameter
    - mounting_holes: Number of mounting holes
    - mounting_diameter: Mounting hole diameter
    - span_stations: Number of blade span stations
    - output_dir: Output directory for generated files
    """
    
    fan_radius = fan_diameter / 2
    hub_radius = hub_diameter / 2
    
    # Validate parameters
    if fan_radius <= hub_radius:
        raise ValueError("Fan diameter must be greater than hub diameter")
    if shaft_diameter >= fan_radius:
        raise ValueError("Shaft diameter must be smaller than fan radius")
    
    # Create document
    doc = App.newDocument("AxialFanAssembly")
    
    # === 1. CREATE SHAFT ===
    shaft_radius = shaft_diameter / 2
    shaft_height = casing_length + 0.1
    shaft = create_cylinder(doc, "Shaft", shaft_radius, shaft_height,
                           position=Vector(0, 0, -shaft_height/2))
    
    # === 2. CREATE HUB ===
    hub_height = fan_radius * 0.15
    hub = create_cylinder(doc, "Hub", hub_radius, hub_height,
                         position=Vector(0, 0, -hub_height/2))
    
    # === 3. GENERATE BLADES ===
    # Generate NACA airfoil profile
    airfoil = generate_airfoil_naca(camber=0.04, camber_pos=0.4, thickness=0.12, points=100)
    
    # Calculate span stations
    span = fan_radius - hub_radius
    blade_angle_rad = math.radians(blade_angle_deg)
    
    blade_solids = []
    for k in range(blade_count):
        phi = 2.0 * math.pi * k / blade_count
        
        # Build blade sections along the span
        blade_sections = []
        for s in range(span_stations):
            frac = s / (span_stations - 1) if span_stations > 1 else 0
            r = hub_radius + frac * span
            
            # Create airfoil section at this radius
            # Scale airfoil by radius and position at azimuth angle
            twist = blade_angle_rad * frac  # Linear twist
            
            # Create scaled airfoil
            scaled_airfoil = [(x * r / fan_radius, y) for x, y in airfoil]
            
            # Create polygon wire
            poly_pts = scaled_airfoil + [scaled_airfoil[0]]
            wire = Part.makePolygon(poly_pts)
            if not wire.isValid():
                continue
            
            # Position wire at radius r, angle phi from hub center
            # Rotate by azimuth angle around z-axis
            cos_phi = math.cos(phi)
            sin_phi = math.sin(phi)
            
            # Transform points: rotate then radial position
            rotated_pts = []
            for x, y in poly_pts:
                # Rotate around origin by phi
                xr = x * cos_phi - y * sin_phi
                yr = x * sin_phi + y * cos_phi
                # Position at radial distance r from center
                rr = r + xr  # radial offset + base radius
                rotated_pts.append((rr, yr))
            
            # Close polygon
            rotated_pts.append(rotated_pts[0])
            
            # Final wire at correct radial position
            final_wire = Part.makePolygon(rotated_pts)
            if final_wire.isValid():
                blade_sections.append(final_wire)
        
        # Loft sections to create blade solid
        if len(blade_sections) >= 2:
            try:
                blade_solid = Part.makeLoft(blade_sections, solid=True, ruled=False)
                if (blade_solid and blade_solid.isValid() 
                        and blade_solid.Volume > 1e-12):
                    blade_solids.append(blade_solid)
                    # Add to document
                    obj = doc.addObject("Part::Feature", f"Blade_{k}")
                    obj.Shape = blade_solid
            except Exception:
                pass
    
    # === 4. CREATE CASING ===
    casing_outer_radius = fan_radius + 0.1
    casing_inner_radius = fan_radius + 0.05
    casing_thickness = casing_length
    
    # Create casing cylinder
    casing = create_cylinder(doc, "Casing", casing_outer_radius, 
                            casing_thickness + 0.2,
                            position=Vector(0, 0, -casing_thickness/2 - 0.1))
    
    # Add flange at casing end
    flange = create_disk(doc, "Flange", flange_od/2, flange_id/2, 0.02,
                        position=Vector(0, 0, casing_thickness/2 + 0.1))
    
    # === 5. MOUNTING HOLES ===
    hole_pattern_radius = flange_od/2 - 0.02
    for i in range(mounting_holes):
        angle = 2.0 * math.pi * i / mounting_holes
        hole_pos = Vector(
            hole_pattern_radius * math.cos(angle),
            hole_pattern_radius * math.sin(angle),
            flange_id/2 + 0.01
        )
        hole = doc.addObject("Part::Feature", f"Mounting_Hole_{i}")
        hole_cyl = Part.makeCylinder(mounting_diameter/2, 0.03,
                                    position=hole_pos)
        hole.Shape = hole_cyl.Shape
    
    # === 6. EXPORT STEP FILE ===
    output_path = Path(output_dir)
    output_path.mkdir(parents=True, exist_ok=True)
    
    # Combine all shapes for STEP export
    all_shapes = []
    for obj in doc.Objects:
        if obj.Name.startswith(("Shaft", "Hub", "Blade", "Casing", "Flange")):
            if obj.Shape and obj.Shape.isValid():
                all_shapes.append(obj.Shape)
    
    if all_shapes:
        combined = Part.makeCompound(all_shapes)
        step_file = str(output_path / f"axial_fan_{fan_diameter}m_{blade_count}blades.step")
        try:
            combined.exportStep(step_file)
            print(f"STEP exported: {step_file}")
        except Exception as e:
            print(f"STEP export failed: {e}")
    
    # === 7. TECHDRAW 2D DRAWINGS ===
    try:
        import TechDraw
        
        # Create drawing page
        page = doc.addObject("TechDraw::DrawPage", "Assembly_Overview")
        page_format = page.Template
        page_format.format = "ISO_A3_Landscape"
        page.Template = page_format
        
        # Create views from different directions
        views_info = [
            ("Front", Vector(0, -1, 0)),
            ("Top", Vector(0, 0, -1)),
            ("Side", Vector(1, 0, 0)),
        ]
        
        for view_name, direction in views_info:
            view = doc.addObject("TechDraw::DrawViewPart", f"{view_name}_View")
            view.Source = [obj for obj in doc.Objects if obj.Shape is not None]
            view.Direction = direction
            view.Scale = 1.0
            page.addView(view)
        
        doc.recompute()
        
        # Export DXF for each view
        dxf_dir = output_path / "dxf_drawings"
        dxf_dir.mkdir(exist_ok=True)
        
        # Save page layout
        page.save(str(output_path / "drawing_layout.layout"))
        
        for view_name, direction in views_info:
            dxf_file = str(dxf_dir / f"axial_fan_{view_name.lower()}.dxf")
            try:
                # Try TechDraw writeDXFView
                if page.views:
                    TechDraw.writeDXFView(page.views[0], dxf_file)
                print(f"DXF exported: {dxf_file}")
            except Exception as e:
                print(f"DXF export {view_name} failed: {e}")
    
    except ImportError:
        print("TechDraw module not available - skipping 2D drawings")
    
    # === SUMMARY ===
    print(f"\n=== Axial Fan Generation Complete ===")
    print(f"Fan diameter: {fan_diameter}m")
    print(f"Hub diameter: {hub_diameter}m")
    print(f"Blade count: {blade_count}")
    print(f"Blade angle: {blade_angle_deg}°")
    print(f"Casing length: {casing_length}m")
    print(f"Shaft diameter: {shaft_diameter}m")
    print(f"Flange OD: {flange_od}m, ID: {flange_id}m")
    print(f"Mounting holes: {mounting_holes}×{mounting_diameter}m")
    print(f"Span stations: {span_stations}")
    print(f"Output directory: {output_dir}")
    print(f"Generated objects: {len(doc.Objects)}")
    
    return {
        "doc": doc,
        "step_file": str(output_path / f"axial_fan_{fan_diameter}m_{blade_count}blades.step") if output_path else None,
        "output_dir": str(output_path),
        "blade_count": blade_count,
        "dxf_dir": str(output_path / "dxf_drawings") if output_path else None
    }


def main():
    """Entry point when script is run directly."""
    import argparse
    
    parser = argparse.ArgumentParser(
        description="Parametric Axial Fan CAD Generator"
    )
    parser.add_argument("--fan-diameter", type=float, default=0.5,
                        help="Fan tip diameter in meters (default: 0.5)")
    parser.add_argument("--hub-diameter", type=float, default=0.15,
                        help="Hub/core diameter in meters (default: 0.15)")
    parser.add_argument("--blade-count", type=int, default=6,
                        help="Number of blades (default: 6)")
    parser.add_argument("--blade-angle", type=float, default=30,
                        help="Blade angle at tip in degrees (default: 30)")
    parser.add_argument("--casing-length", type=float, default=0.3,
                        help="Casing length in meters (default: 0.3)")
    parser.add_argument("--shaft-diameter", type=float, default=0.025,
                        help="Shaft diameter in meters (default: 0.025)")
    parser.add_argument("--flange-od", type=float, default=0.2,
                        help="Flange outer diameter in meters (default: 0.2)")
    parser.add_argument("--flange-id", type=float, default=0.12,
                        help="Flange inner diameter in meters (default: 0.12)")
    parser.add_argument("--mounting-holes", type=int, default=4,
                        help="Number of mounting holes (default: 4)")
    parser.add_argument("--mounting-diameter", type=float, default=0.01,
                        help="Mounting hole diameter in meters (default: 0.01)")
    parser.add_argument("--span-stations", type=int, default=6,
                        help="Number of blade span stations (default: 6)")
    parser.add_argument("--output", type=str, default="/tmp/axial_fan",
                        help="Output directory for generated files")
    
    args = parser.parse_args()
    
    result = generate(
        fan_diameter=args.fan_diameter,
        hub_diameter=args.hub_diameter,
        blade_count=args.blade_count,
        blade_angle_deg=args.blade_angle,
        casing_length=args.casing_length,
        shaft_diameter=args.shaft_diameter,
        flange_od=args.flange_od,
        flange_id=args.flange_id,
        mounting_holes=args.mounting_holes,
        mounting_diameter=args.mounting_diameter,
        span_stations=args.span_stations,
        output_dir=args.output
    )
    
    return result


if __name__ == "__main__":
    # When run directly, use default parameters
    result = generate()
    # Can also use command line:
    # python axial_fan_parametric.py --fan-diameter 0.6 --blade-count 8