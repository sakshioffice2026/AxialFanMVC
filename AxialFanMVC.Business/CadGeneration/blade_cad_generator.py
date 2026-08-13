# -*- coding: utf-8 -*-
"""
FreeCAD headless blade/hub solid generator.
Follows CyclonApp pattern: runs via freecadcmd.exe subprocess,
receives params via ENVIRONMENT VARIABLES, not CLI args.

Environment variables:
    CAD_BLADE_PARAMS_JSON = JSON string with blade parameters
    CAD_OUTPUT_DIR = folder to write STEP/DXF/OBJ into

Run via freecadcmd.exe only:
    env["CAD_BLADE_PARAMS_JSON"] = json.dumps(params)
    env["CAD_OUTPUT_DIR"] = output_dir
    subprocess.run([freecadcmd.exe, "-c", "exec(open('blade_cad_generator.py').read())"], env=env)

Standalone test (no env vars set -> uses sample parameters):
    freecadcmd.exe -c "exec(open('blade_cad_generator.py').read())"
"""

import os
import sys
import json
import math
from pathlib import Path

# ---- Locate FreeCAD's Python modules --------------------------------
def _add_freecad_to_path():
    if os.name == "nt":
        candidates = [
            os.environ.get("FREECAD_BIN_PATH"),
            r"C:\Program Files\FreeCAD 1.1\bin",
            r"C:\Program Files\FreeCAD 1.0\bin",
        ]
        for path in candidates:
            if path and os.path.isdir(path):
                sys.path.append(path)
                return
    # On Linux/inside freecadcmd, FreeCAD is already importable


_add_freecad_to_path()

import FreeCAD as App
from FreeCAD import Vector
import Part


class BladeCADGenerator:
    """Generates parametric blade/hub assembly in FreeCAD."""

    def __init__(self, doc_name: str = "AxialFan"):
        """Initialize FreeCAD document."""
        self.doc = App.newDocument(doc_name)
        self.blade_solids = []
        self.hub_solid = None

    @staticmethod
    def parse_profile(profile_json: str) -> list:
        """Parse airfoil profile from JSON."""
        if not profile_json:
            return None
        try:
            data = json.loads(profile_json)
            if not isinstance(data, list):
                return None
            pts = []
            for elem in data:
                if isinstance(elem, (list, tuple)) and len(elem) >= 2:
                    pts.append((float(elem[0]), float(elem[1])))
                elif isinstance(elem, dict):
                    x = float(elem.get("x", 0))
                    y = float(elem.get("y", 0))
                    pts.append((x, y))
                else:
                    return None
            return pts if len(pts) >= 3 else None
        except (json.JSONDecodeError, ValueError, TypeError):
            return None

    @staticmethod
    def naca_four_digit_loop(
        camber: float = 0.04,
        camber_pos: float = 0.4,
        thickness: float = 0.12,
        half_point_count: int = 20
    ) -> list:
        """Generate NACA 4-digit airfoil closed loop."""
        xs = [0.5 * (1 - math.cos(math.pi * i / half_point_count))
              for i in range(half_point_count + 1)]

        def yt(x):
            return 5 * thickness * (
                0.2969 * math.sqrt(x) - 0.1260 * x - 0.3516 * x**2
                + 0.2843 * x**3 - 0.1015 * x**4
            )

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

        upper, lower = [], []
        for x in xs:
            yc, dyc = camber_line(x)
            theta = math.atan(dyc)
            y_t = yt(x)
            upper.append((x - y_t * math.sin(theta), yc + y_t * math.cos(theta)))
            lower.append((x + y_t * math.sin(theta), yc - y_t * math.cos(theta)))

        loop = upper + [lower[i] for i in range(len(lower) - 2, 0, -1)]
        return loop

    @staticmethod
    def build_blade_sections(
        radii: list,
        chords: list,
        beta_rad: list,
        azimuth_rad: float,
        airfoil_loop: list
    ) -> list:
        """Build loft sections - one per radial station."""
        cos_phi = math.cos(azimuth_rad)
        sin_phi = math.sin(azimuth_rad)
        eR = Vector(cos_phi, sin_phi, 0)
        eT = Vector(-sin_phi, cos_phi, 0)
        eZ = Vector(0, 0, 1)

        sections = []
        for r, ch, beta in zip(radii, chords, beta_rad):
            cos_beta = math.cos(beta)
            sin_beta = math.sin(beta)
            c = eT * cos_beta + eZ * sin_beta
            t = Vector(
                eR.y * c.z - eR.z * c.y,
                eR.z * c.x - eR.x * c.z,
                eR.x * c.y - eR.y * c.x
            )
            t = t.normalize() if t.Length > 1e-10 else Vector(1, 0, 0)

            base_p = eR * r
            pts_3d = []
            for xf, yf in airfoil_loop:
                p3d = base_p + c * (xf * ch) + t * (yf * ch)
                pts_3d.append(p3d)

            pts_3d.append(pts_3d[0])
            wire = Part.makePolygon(pts_3d)
            sections.append(wire)

        return sections

    def build_blade(
        self,
        radii: list,
        chords: list,
        beta_rad: list,
        azimuth_rad: float,
        airfoil_loop: list
    ) -> Part.Solid:
        """Build single blade via lofting sections."""
        sections = self.build_blade_sections(radii, chords, beta_rad, azimuth_rad, airfoil_loop)

        if len(sections) < 2:
            raise ValueError(f"Need at least 2 sections, got {len(sections)}")

        try:
            lofted = Part.makeLoft(sections, solid=False)
        except Exception as e:
            raise RuntimeError(f"Loft failed: {e}")

        hub_pts = sections[0]
        hub_verts = hub_pts.Vertexes
        hub_center = sum([v.Point for v in hub_verts], Vector(0, 0, 0)) * (1.0 / len(hub_verts))

        tip_pts = sections[-1]
        tip_verts = tip_pts.Vertexes
        tip_center = sum([v.Point for v in tip_verts], Vector(0, 0, 0)) * (1.0 / len(tip_verts))

        try:
            hub_face = Part.makeFace(hub_pts) if len(hub_verts) > 2 else lofted
            tip_face = Part.makeFace(tip_pts) if len(tip_verts) > 2 else lofted
            shell = Part.Shell([lofted, hub_face, tip_face])
            blade_solid = Part.Solid(shell)
            blade_solid.removeInternalFaces()
        except Exception as e:
            print(f"Warning: Solid construction failed: {e}")
            blade_solid = lofted

        return blade_solid

    def generate_assembly(
        self,
        tip_radius_m: float,
        hub_ratio: float,
        blade_count: int,
        blade_angle_deg: float,
        profile_json: str,
        rpm: float,
        axial_velocity_ms: float,
        span_stations: int = 6,
        target_solidity: float = 0.5
    ) -> None:
        """Generate full blade/hub assembly."""
        if tip_radius_m <= 0:
            raise ValueError(f"Tip radius must be positive")

        hub_ratio = max(0.15, min(0.85, hub_ratio if 0 < hub_ratio < 1 else 0.45))
        blade_count = max(1, blade_count)
        span_stations = max(2, span_stations)

        hub_radius_m = tip_radius_m * hub_ratio
        span = tip_radius_m - hub_radius_m

        if span <= 0:
            raise ValueError(f"Hub >= tip radius")

        airfoil = (self.parse_profile(profile_json) or
                   self.naca_four_digit_loop())

        omega_rad_s = max(rpm, 0.0) * math.pi / 30.0
        axial_vel = max(axial_velocity_ms, 0.0)

        radii = []
        beta_rad_list = []
        chords = []

        for s in range(span_stations):
            frac = s / (span_stations - 1) if span_stations > 1 else 0
            r = hub_radius_m + frac * span
            radii.append(r)

            u = omega_rad_s * r
            beta = math.atan2(axial_vel, u) if u > 1e-10 or axial_vel > 1e-10 else math.pi / 2
            beta_rad_list.append(beta)

            chord = target_solidity * 2.0 * math.pi * r / blade_count
            chords.append(chord)

        # Build hub (solid cylinder)
        hub_height = tip_radius_m * 0.15
        self.hub_solid = Part.makeCylinder(hub_radius_m, hub_height, Vector(0, 0, -hub_height/2))
        hub_obj = self.doc.addObject("Part::Feature", "Hub")
        hub_obj.Shape = self.hub_solid

        # Build blades
        for k in range(blade_count):
            phi = 2.0 * math.pi * k / blade_count
            try:
                blade_solid = self.build_blade(radii, chords, beta_rad_list, phi, airfoil)
                self.blade_solids.append(blade_solid)
                blade_obj = self.doc.addObject("Part::Feature", f"Blade_{k}")
                blade_obj.Shape = blade_solid
            except Exception as e:
                print(f"Warning: Blade {k} failed: {e}")
                continue

        print(f"Assembly generated: hub + {len(self.blade_solids)} blades")

    def export_step(self, output_path: str) -> None:
        """Export hub + blades as STEP assembly."""
        if not self.blade_solids and not self.hub_solid:
            raise ValueError("No geometry to export")

        shapes = [self.hub_solid] if self.hub_solid else []
        shapes.extend(self.blade_solids)

        if len(shapes) == 1:
            combined = shapes[0]
        else:
            try:
                combined = shapes[0]
                for s in shapes[1:]:
                    combined = combined.fuse(s)
            except:
                combined = Part.makeCompound(shapes)

        Part.export(combined, output_path)
        print(f"Exported STEP: {output_path}")

    def export_obj(self, output_path: str) -> None:
        """Export as OBJ (for web viewer)."""
        shapes = [self.hub_solid] if self.hub_solid else []
        shapes.extend(self.blade_solids)

        if not shapes:
            raise ValueError("No geometry to export")

        combined = Part.makeCompound(shapes) if len(shapes) > 1 else shapes[0]
        
        mesh = combined.tessellate(0.01)
        
        with open(output_path, 'w') as f:
            f.write("# AxialFan blade assembly\n")
            v_offset = 1
            for tri in mesh:
                for pt in tri:
                    f.write(f"v {pt.x:.6f} {pt.y:.6f} {pt.z:.6f}\n")
                f.write(f"f {v_offset} {v_offset+1} {v_offset+2}\n")
                v_offset += 3

        print(f"Exported OBJ: {output_path}")

    def export_dxf(self, output_path: str, tip_radius_m: float, hub_radius_m: float, 
                   blade_count: int, avg_chord: float, stagger_angle_deg: float = 30.0) -> None:
        """Export 2D engineering drawing with hardcoded dimensions."""
        try:
            import ezdxf
        except ImportError:
            print("WARNING: ezdxf not available - skipping DXF export")
            return

        tip_diameter = tip_radius_m * 2 * 1000
        hub_diameter = hub_radius_m * 2 * 1000
        chord_mm = avg_chord * 1000
        hub_width = tip_radius_m * 0.15 * 1000
        blade_pitch = (2 * math.pi * tip_radius_m) / blade_count * 1000

        doc = ezdxf.new('R2000')
        msp = doc.modelspace()

        doc.layers.add('Dimensions', color=1)
        doc.layers.add('Geometry', color=7)
        doc.layers.add('Text', color=3)

        msp.add_circle((0, 0), tip_radius_m * 1000, dxfattribs={'layer': 'Geometry'})
        msp.add_circle((0, 0), hub_radius_m * 1000, dxfattribs={'layer': 'Geometry'})

        for k in range(2):
            phi = 2 * math.pi * k / blade_count
            x1 = hub_radius_m * 1000 * math.cos(phi)
            y1 = hub_radius_m * 1000 * math.sin(phi)
            x2 = tip_radius_m * 1000 * math.cos(phi)
            y2 = tip_radius_m * 1000 * math.sin(phi)
            msp.add_line((x1, y1), (x2, y2), dxfattribs={'layer': 'Geometry'})

        msp.add_text(f"O {tip_diameter:.1f} mm (Tip)", dxfattribs={'layer': 'Text'}).set_pos(
            (50, tip_radius_m * 1000 + 120), align=0)
        msp.add_text(f"O {hub_diameter:.1f} mm (Hub)", dxfattribs={'layer': 'Text'}).set_pos(
            (50, hub_radius_m * 1000 + 50), align=0)
        msp.add_text(f"Blades: {blade_count} @ {blade_pitch:.1f} mm pitch", 
                     dxfattribs={'layer': 'Text'}).set_pos(
            (-tip_radius_m * 1000 - 200, tip_radius_m * 1000 - 100), align=0)

        view_offset_x = tip_radius_m * 1000 + 300

        msp.add_line((view_offset_x, 0), (view_offset_x + hub_width, 0), 
                     dxfattribs={'layer': 'Geometry'})
        msp.add_line((view_offset_x + hub_width, 0), (view_offset_x + hub_width, hub_diameter / 2), 
                     dxfattribs={'layer': 'Geometry'})
        msp.add_line((view_offset_x + hub_width, hub_diameter / 2), (view_offset_x, hub_diameter / 2), 
                     dxfattribs={'layer': 'Geometry'})
        msp.add_line((view_offset_x, hub_diameter / 2), (view_offset_x, 0), 
                     dxfattribs={'layer': 'Geometry'})

        stagger_rad = math.radians(stagger_angle_deg)
        blade_axial_offset = chord_mm * math.cos(stagger_rad)
        blade_height = (tip_diameter - hub_diameter) / 2

        msp.add_line((view_offset_x, hub_diameter / 2), 
                     (view_offset_x + blade_axial_offset, hub_diameter / 2 + blade_height), 
                     dxfattribs={'layer': 'Geometry'})
        msp.add_line((view_offset_x + hub_width, hub_diameter / 2), 
                     (view_offset_x + hub_width + blade_axial_offset, hub_diameter / 2 + blade_height), 
                     dxfattribs={'layer': 'Geometry'})

        msp.add_text(f"{hub_width:.1f} mm", dxfattribs={'layer': 'Text'}).set_pos(
            (view_offset_x + hub_width / 2 - 30, -80), align=0)
        msp.add_text(f"Chord: {chord_mm:.1f} mm", dxfattribs={'layer': 'Text'}).set_pos(
            (view_offset_x + blade_axial_offset / 2, hub_diameter / 2 + blade_height + 50), align=0)
        msp.add_text(f"Stagger: {stagger_angle_deg:.1f} deg", dxfattribs={'layer': 'Text'}).set_pos(
            (view_offset_x + blade_axial_offset + 30, hub_diameter / 2 + blade_height / 2), align=0)

        msp.add_text("Axial Fan Blade Assembly", dxfattribs={'layer': 'Text', 'height': 5}).set_pos(
            (-tip_radius_m * 1000 - 100, -tip_radius_m * 1000 - 100), align=0)
        msp.add_text(f"Scale: 1:1 (mm)", dxfattribs={'layer': 'Text'}).set_pos(
            (-tip_radius_m * 1000 - 100, -tip_radius_m * 1000 - 150), align=0)

        doc.saveas(output_path)
        print(f"Exported DXF: {output_path}")


# ---- Main execution (when run by freecadcmd.exe) ----
if __name__ == "__main__":
    # Read parameters from environment variables (set by cad_service.py)
    params_json = os.environ.get("CAD_BLADE_PARAMS_JSON", "")
    output_dir = os.environ.get("CAD_OUTPUT_DIR", "/tmp/axial_fan_cad")

    # Sample/fallback parameters for standalone testing
    if not params_json:
        params = {
            "tip_radius_m": 0.2,
            "hub_ratio": 0.45,
            "blade_count": 6,
            "rpm": 1500,
            "axial_velocity_ms": 5.0,
            "span_stations": 6,
            "target_solidity": 0.5,
            "stagger_angle_deg": 30.0,
            "profile_coordinate_json": None
        }
        print("[Blade CAD] Using sample parameters")
    else:
        try:
            params = json.loads(params_json)
            print(f"[Blade CAD] Loaded params from env")
        except json.JSONDecodeError as e:
            print(f"[Blade CAD] ERROR parsing CAD_BLADE_PARAMS_JSON: {e}")
            sys.exit(1)

    # Create output directory
    Path(output_dir).mkdir(parents=True, exist_ok=True)

    try:
        gen = BladeCADGenerator("AxialFan")
        gen.generate_assembly(
            tip_radius_m=params["tip_radius_m"],
            hub_ratio=params.get("hub_ratio", 0.45),
            blade_count=params.get("blade_count", 6),
            blade_angle_deg=params.get("blade_angle_deg", 0.0),
            profile_json=params.get("profile_coordinate_json"),
            rpm=params.get("rpm", 1500),
            axial_velocity_ms=params.get("axial_velocity_ms", 5.0),
            span_stations=params.get("span_stations", 6),
            target_solidity=params.get("target_solidity", 0.5)
        )

        # Export files
        case_id = params.get("case_id", "test")
        step_file = os.path.join(output_dir, f"fan_{case_id}.step")
        dxf_file = os.path.join(output_dir, f"fan_{case_id}.dxf")
        obj_file = os.path.join(output_dir, f"fan_{case_id}.obj")

        gen.export_step(step_file)
        gen.export_dxf(
            dxf_file,
            tip_radius_m=params["tip_radius_m"],
            hub_radius_m=params["tip_radius_m"] * params.get("hub_ratio", 0.45),
            blade_count=params.get("blade_count", 6),
            avg_chord=params.get("target_solidity", 0.5) * 2.0 * 3.14159 * 
                     (params["tip_radius_m"] + params["tip_radius_m"] * params.get("hub_ratio", 0.45)) / 2 / params.get("blade_count", 6),
            stagger_angle_deg=params.get("stagger_angle_deg", 30.0)
        )
        gen.export_obj(obj_file)

        print(f"[Blade CAD] SUCCESS: Generated {step_file}, {dxf_file}, {obj_file}")
        sys.exit(0)

    except Exception as e:
        print(f"[Blade CAD] ERROR: {e}")
        import traceback
        traceback.print_exc()
        sys.exit(1)