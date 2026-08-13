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

MECHANICAL/GEOMETRY FIX PASS (this revision):
  1. build_blade(): replaced the manual hub_face/tip_face/Shell/Solid hand-
     assembly (which silently produced an INVALID/EMPTY shape whenever
     Part.Shell([open_loft_shell, cap, cap]) choked on being handed a
     multi-face Shell instead of flat Faces - the root cause of the empty
     STEP and the hub-only/tip-only OBJ mesh) with Part.makeLoft(sections,
     solid=True). This is FreeCAD's own capped-solid loft: it caps the
     first and last (closed, planar) section wires and returns one real
     watertight Solid, per blade, with no manual shell surgery to get wrong.
  2. Every solid is now VALIDATED (isValid(), Volume > 0, ShapeType) right
     after construction. A blade or hub that fails is a HARD ERROR, not a
     swallowed warning - a fan assembly silently missing blades must never
     reach export as if it were correct.
  3. export_step(): dropped the risky boolean fuse() attempt (fusing N
     independent lofted solids has no mechanical benefit for an assembly
     and is a common source of OCCT failures); exports a validated
     multi-solid compound directly, which is the standard/robust way to
     hand off a multi-part assembly to STEP.
  4. export_dxf(): the old version fabricated the blade side-view from a
     single hardcoded stagger_angle_deg and a single average chord,
     completely disconnected from the actual per-station chord/twist the
     solid was built from. It now derives the LEADING EDGE and TRAILING
     EDGE curves directly from the same (radius, chord, beta) station data
     used to loft the real blade, plus draws the real hub/tip airfoil
     cross-sections to scale - so the drawing can no longer contradict the
     3D model.

Coordinate/stacking convention (unchanged, documented explicitly here
because export_dxf's geometry-derivation depends on it):
  Each blade section is stacked so the airfoil LEADING EDGE (local x=0)
  sits exactly on the radial ray at that station's (r, phi) - i.e. LE is
  the "spine" the sections are threaded on. The chord vector then rotates
  by the local blade angle beta(r), so the TRAILING EDGE (local x=chord)
  swings axially by chord*sin(beta) relative to the LE. That is why, in a
  developed (r vs axial) view, the true LE curve is a straight vertical
  line and the true TE curve is the one that carries the twist - see
  export_dxf().
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
import TechDraw


class BladeGeometryError(RuntimeError):
    """Raised when the blade/hub solid does not come out mechanically
    valid (missing blades, zero-volume solid, non-solid shape, etc).
    Deliberately NOT caught anywhere that would let export proceed -
    a broken assembly must fail loudly, not export an empty/partial
    STEP that looks like a successful 200/201 response."""


class BladeCADGenerator:
    """Generates parametric blade/hub assembly in FreeCAD."""

    def __init__(self, doc_name: str = "AxialFan"):
        """Initialize FreeCAD document."""
        self.doc = App.newDocument(doc_name)
        self.blade_solids = []
        self.hub_solid = None

        # Station data actually used to build blade 0 - stashed here so
        # export_dxf() can derive the drawing from the SAME numbers the
        # solid was lofted from, instead of recomputing/guessing them.
        self.station_radii = []
        self.station_chords = []
        self.station_beta_rad = []
        self.airfoil_loop = []
        self.hub_height_m = 0.0

        # Casing/shaft/flange solids - populated by build_casing_assembly().
        # Kept separate from hub_solid/blade_solids (rotor group) since
        # casing is stationary and shaft protrudes beyond the rotor.
        self.casing_solid = None
        self.shaft_solid = None
        self.inlet_flange_solid = None
        self.outlet_flange_solid = None
        self._casing_clearance_m = 0.0
        self._casing_wall_m = 0.0

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
        """Build loft sections - one closed planar wire per radial station."""
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
        airfoil_loop: list,
        blade_index: int = 0
    ) -> Part.Solid:
        """Build a single blade as a real watertight solid, by lofting the
        closed section wires with capped ends (FreeCAD's own loft-to-solid,
        not a hand-built Shell/Solid). Raises BladeGeometryError instead of
        returning a degenerate shape."""
        sections = self.build_blade_sections(radii, chords, beta_rad, azimuth_rad, airfoil_loop)

        if len(sections) < 2:
            raise BladeGeometryError(
                f"Blade {blade_index}: need at least 2 span sections, got {len(sections)}"
            )

        try:
            # solid=True: FreeCAD caps the first/last (closed, planar)
            # wires itself and returns one real Solid - this is the
            # standard, robust way to loft a capped solid and replaces
            # the previous manual Shell([open_loft, hub_face, tip_face])
            # construction, which was passing a multi-face Shell where a
            # flat list of Faces was expected and produced an invalid
            # shape that silently failed to export any BREP data.
            blade_solid = Part.makeLoft(sections, solid=True, ruled=False)
        except Exception as e:
            raise BladeGeometryError(f"Blade {blade_index}: loft-to-solid failed: {e}")

        if blade_solid is None or blade_solid.ShapeType != "Solid":
            raise BladeGeometryError(
                f"Blade {blade_index}: loft did not produce a Solid "
                f"(got {getattr(blade_solid, 'ShapeType', None)})"
            )
        if not blade_solid.isValid():
            raise BladeGeometryError(f"Blade {blade_index}: resulting solid failed isValid() check")
        if blade_solid.Volume <= 1e-12:
            raise BladeGeometryError(
                f"Blade {blade_index}: resulting solid has zero/near-zero volume "
                f"({blade_solid.Volume}) - sections likely self-intersect or are degenerate"
            )

        return blade_solid

    @staticmethod
    def _round_flange(radius_inner_m, z_m, extend_forward, flange_t_m,
                       flange_width_m, bolt_dia_m, bolt_count, overlap_m=0.005):
        """Flat annular bolted flange, welded onto a round duct end.
        Same overlap-into-the-shell technique as CyclonApp's flange
        builder: fuse() needs real shared volume, not a zero-area ring
        contact, or the flange ends up a disconnected solid."""
        radius_outer = radius_inner_m + flange_width_m
        height = flange_t_m + overlap_m
        z0 = (z_m - overlap_m) if extend_forward else (z_m - flange_t_m)
        direction = Vector(0, 0, 1)

        outer_disc = Part.makeCylinder(radius_outer, height, Vector(0, 0, z0), direction)
        inner_hole = Part.makeCylinder(radius_inner_m, height + 0.002,
                                        Vector(0, 0, z0 - 0.001), direction)
        flange = outer_disc.cut(inner_hole)

        bolt_circle_r = (radius_inner_m + radius_outer) / 2.0
        for i in range(bolt_count):
            ang = 2.0 * math.pi * i / bolt_count
            bx = bolt_circle_r * math.cos(ang)
            by = bolt_circle_r * math.sin(ang)
            bolt = Part.makeCylinder(bolt_dia_m / 2.0, height + 0.002,
                                      Vector(bx, by, z0 - 0.001), direction)
            flange = flange.cut(bolt)

        if not flange.isValid() or flange.Volume <= 1e-12:
            raise BladeGeometryError("Flange solid is invalid or zero-volume")
        return flange

    def build_casing_assembly(
        self,
        tip_radius_m: float,
        casing_length_m: float,
        shaft_radius_m: float,
        tip_clearance_m: float = 0.002,
        wall_thickness_m: float = 0.003,
        flange_thickness_m: float = 0.008,
        flange_width_m: float = 0.02,
        flange_bolt_dia_m: float = 0.008,
        flange_bolt_count: int = 8,
        shaft_protrude_m: float = 0.03,
    ) -> None:
        """Build stationary casing (cylindrical shell + bolted flanges at
        both ends) and the through-shaft, centered on the same Z axis the
        rotor (hub + blades) is built on. Kept as a SEPARATE solid group
        from the rotor - casing is stationary, shaft passes through the
        hub bore, neither is fused to the rotating hub/blades. Raises
        BladeGeometryError on any invalid/zero-volume result, same
        validate-or-fail posture as build_blade / generate_assembly."""
        if casing_length_m <= 0:
            raise BladeGeometryError("Casing length must be positive")
        if shaft_radius_m <= 0 or shaft_radius_m >= tip_radius_m:
            raise BladeGeometryError(
                f"Shaft radius must be positive and smaller than tip radius "
                f"(got shaft_radius_m={shaft_radius_m}, tip_radius_m={tip_radius_m})"
            )

        casing_inner_r = tip_radius_m + tip_clearance_m
        casing_outer_r = casing_inner_r + wall_thickness_m
        # Stashed so export_dxf() can redraw casing circles without
        # recomputing/duplicating these numbers.
        self._casing_clearance_m = tip_clearance_m
        self._casing_wall_m = wall_thickness_m

        # Casing shell centered so rotor mid-plane (z=0, from
        # generate_assembly's hub placement) sits at casing mid-length.
        z0 = -casing_length_m / 2.0
        outer = Part.makeCylinder(casing_outer_r, casing_length_m, Vector(0, 0, z0))
        bore = Part.makeCylinder(casing_inner_r, casing_length_m + 0.002,
                                  Vector(0, 0, z0 - 0.001))
        casing = outer.cut(bore)

        inlet_flange = self._round_flange(
            casing_outer_r, z0, False, flange_thickness_m, flange_width_m,
            flange_bolt_dia_m, flange_bolt_count
        )
        outlet_flange = self._round_flange(
            casing_outer_r, z0 + casing_length_m, True, flange_thickness_m,
            flange_width_m, flange_bolt_dia_m, flange_bolt_count
        )

        casing = casing.fuse(inlet_flange).fuse(outlet_flange)
        if not casing.isValid() or casing.Volume <= 1e-12:
            raise BladeGeometryError("Casing+flange assembly is invalid or zero-volume")

        shaft_len = casing_length_m + 2.0 * shaft_protrude_m
        shaft = Part.makeCylinder(shaft_radius_m, shaft_len,
                                   Vector(0, 0, z0 - shaft_protrude_m))
        if not shaft.isValid() or shaft.Volume <= 1e-12:
            raise BladeGeometryError("Shaft solid is invalid or zero-volume")

        self.casing_solid = casing
        self.shaft_solid = shaft
        self.inlet_flange_solid = inlet_flange
        self.outlet_flange_solid = outlet_flange

        casing_obj = self.doc.addObject("Part::Feature", "Casing")
        casing_obj.Shape = casing
        shaft_obj = self.doc.addObject("Part::Feature", "Shaft")
        shaft_obj.Shape = shaft

        print(f"Casing+shaft built: casing OD={casing_outer_r*2*1000:.1f}mm, "
              f"length={casing_length_m*1000:.1f}mm, shaft dia={shaft_radius_m*2*1000:.1f}mm")

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
        """Generate full blade/hub assembly. Raises BladeGeometryError if
        the hub or ANY blade does not come out as a valid solid - a fan
        assembly missing blades must never be exported as if it succeeded."""
        if tip_radius_m <= 0:
            raise BladeGeometryError("Tip radius must be positive")

        hub_ratio = max(0.15, min(0.85, hub_ratio if 0 < hub_ratio < 1 else 0.45))
        blade_count = max(1, blade_count)
        span_stations = max(2, span_stations)

        hub_radius_m = tip_radius_m * hub_ratio
        span = tip_radius_m - hub_radius_m

        if span <= 0:
            raise BladeGeometryError("Hub radius must be smaller than tip radius")

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

        # Stash the exact station data used, so export_dxf() can derive
        # a drawing that matches this solid instead of a separately
        # fabricated approximation.
        self.station_radii = radii
        self.station_chords = chords
        self.station_beta_rad = beta_rad_list
        self.airfoil_loop = airfoil

        # Build hub (solid cylinder)
        hub_height = tip_radius_m * 0.15
        self.hub_height_m = hub_height
        self.hub_solid = Part.makeCylinder(hub_radius_m, hub_height, Vector(0, 0, -hub_height / 2))

        if not self.hub_solid.isValid() or self.hub_solid.Volume <= 1e-12:
            raise BladeGeometryError("Hub cylinder is invalid or has zero volume")

        hub_obj = self.doc.addObject("Part::Feature", "Hub")
        hub_obj.Shape = self.hub_solid

        # Build blades - ANY failure here is fatal (see BladeGeometryError
        # docstring). No more per-blade try/except-and-skip: a fan that
        # silently ends up with fewer blades than requested is not a
        # partially-successful result, it's a wrong part.
        for k in range(blade_count):
            phi = 2.0 * math.pi * k / blade_count
            blade_solid = self.build_blade(radii, chords, beta_rad_list, phi, airfoil, blade_index=k)
            self.blade_solids.append(blade_solid)
            blade_obj = self.doc.addObject("Part::Feature", f"Blade_{k}")
            blade_obj.Shape = blade_solid

        if len(self.blade_solids) != blade_count:
            raise BladeGeometryError(
                f"Only {len(self.blade_solids)} of {blade_count} blades built successfully"
            )

        print(f"Assembly generated: hub + {len(self.blade_solids)} blades (all valid solids)")

    def export_step(self, output_path: str) -> None:
        """Export hub + blades as a multi-solid STEP assembly.

        Deliberately does NOT attempt to boolean-fuse the blades into the
        hub: fusing N independently-lofted solids adds real OCCT failure
        risk for no mechanical benefit (a STEP assembly of separate,
        correctly-positioned solids is standard and perfectly valid for
        manufacturing/CAM). Every shape is validated before export so an
        empty/degenerate STEP (as previously happened silently) can no
        longer occur.
        """
        if not self.blade_solids or self.hub_solid is None:
            raise BladeGeometryError("No valid geometry to export (hub or blades missing)")

        shapes = [self.hub_solid] + self.blade_solids
        if self.casing_solid is not None:
            shapes.append(self.casing_solid)
        if self.shaft_solid is not None:
            shapes.append(self.shaft_solid)
        for i, s in enumerate(shapes):
            if s is None or not s.isValid() or s.Volume <= 1e-12:
                raise BladeGeometryError(f"Shape index {i} is invalid or zero-volume - refusing to export")

        combined = Part.makeCompound(shapes)
        # Part.export() expects FreeCAD document objects (things with a
        # .Shape attribute) - handing it a raw Part.Shape/compound (as
        # before) silently writes a STEP file with only header/axis-
        # placement entities and NO actual B-Rep geometry (confirmed:
        # exported files were ~1.6KB with no SHAPE_REPRESENTATION solid
        # data, despite OBJ export of the same shapes working fine).
        # Shape.exportStep() writes the real geometry directly.
        combined.exportStep(output_path)
        print(f"Exported STEP: {output_path} ({len(shapes)} solids)")

    def export_obj(self, output_path: str) -> None:
        """Export as OBJ (for web viewer)."""
        if not self.blade_solids or self.hub_solid is None:
            raise BladeGeometryError("No valid geometry to export (hub or blades missing)")

        shapes = [self.hub_solid] + self.blade_solids
        if self.casing_solid is not None:
            shapes.append(self.casing_solid)
        if self.shaft_solid is not None:
            shapes.append(self.shaft_solid)
        combined = Part.makeCompound(shapes)

        vertices, facets = combined.tessellate(0.01)
        if not vertices or not facets:
            raise BladeGeometryError("Tessellation produced no geometry")

        with open(output_path, 'w') as f:
            f.write("# AxialFan blade assembly\n")
            for v in vertices:
                f.write(f"v {v.x:.6f} {v.y:.6f} {v.z:.6f}\n")
            for f1, f2, f3 in facets:
                # OBJ face indices are 1-based
                f.write(f"f {f1 + 1} {f2 + 1} {f3 + 1}\n")

        print(f"Exported OBJ: {output_path} ({len(vertices)} verts, {len(facets)} faces)")

    def export_dxf(self, output_path: str, tip_radius_m: float, hub_radius_m: float,
                   blade_count: int) -> None:
        """Export a 2D engineering drawing DERIVED FROM THE ACTUAL SOLID's
        station data (self.station_radii/chords/beta_rad, set by
        generate_assembly) - not from a separately hardcoded stagger angle
        or average chord. See the stacking-convention note in the module
        docstring for why the LE curve is a straight line and the TE curve
        is the one carrying the twist.
        """
        try:
            import ezdxf
            from ezdxf.enums import TextEntityAlignment
        except ImportError:
            print("WARNING: ezdxf not available - skipping DXF export")
            return

        if not self.station_radii:
            raise BladeGeometryError("export_dxf called before generate_assembly (no station data)")

        def place_text(text_entity, pos):
            text_entity.set_placement(pos, align=TextEntityAlignment.LEFT)

        tip_diameter = tip_radius_m * 2 * 1000
        hub_diameter = hub_radius_m * 2 * 1000
        hub_width = self.hub_height_m * 1000
        blade_pitch = (2 * math.pi * tip_radius_m) / blade_count * 1000
        root_chord_mm = self.station_chords[0] * 1000
        tip_chord_mm = self.station_chords[-1] * 1000
        root_beta_deg = math.degrees(self.station_beta_rad[0])
        tip_beta_deg = math.degrees(self.station_beta_rad[-1])

        doc = ezdxf.new('R2000')
        msp = doc.modelspace()

        doc.layers.add('Dimensions', color=1)
        doc.layers.add('Geometry', color=7)
        doc.layers.add('Text', color=3)

        # ---- Front view: tip/hub circles + real blade angular positions ----
        msp.add_circle((0, 0), tip_radius_m * 1000, dxfattribs={'layer': 'Geometry'})
        msp.add_circle((0, 0), hub_radius_m * 1000, dxfattribs={'layer': 'Geometry'})

        # Casing outer/bore circles, only if casing was built.
        if self.casing_solid is not None:
            casing_outer_r_mm = (tip_radius_m + self._casing_clearance_m
                                  + self._casing_wall_m) * 1000
            casing_bore_r_mm = (tip_radius_m + self._casing_clearance_m) * 1000
            msp.add_circle((0, 0), casing_outer_r_mm, dxfattribs={'layer': 'Geometry'})
            msp.add_circle((0, 0), casing_bore_r_mm, dxfattribs={'layer': 'Geometry'})
            place_text(msp.add_text(f"O {casing_outer_r_mm*2:.1f} mm (Casing OD)",
                       dxfattribs={'layer': 'Text'}), (50, casing_outer_r_mm + 190))

        for k in range(blade_count):
            phi = 2 * math.pi * k / blade_count
            x1 = hub_radius_m * 1000 * math.cos(phi)
            y1 = hub_radius_m * 1000 * math.sin(phi)
            x2 = tip_radius_m * 1000 * math.cos(phi)
            y2 = tip_radius_m * 1000 * math.sin(phi)
            msp.add_line((x1, y1), (x2, y2), dxfattribs={'layer': 'Geometry'})

            # Blade silhouette (leading/trailing edge envelope at hub and
            # tip, using the real chord/beta at each station) - the bare
            # centerline above only marks blade ANGULAR POSITION, it is
            # not the blade outline, so it reads as a spoke rather than
            # a recognizable blade shape.
            perp = phi + math.pi / 2
            hub_half_w = (self.station_chords[0] * math.cos(self.station_beta_rad[0])) * 1000 / 2
            tip_half_w = (self.station_chords[-1] * math.cos(self.station_beta_rad[-1])) * 1000 / 2
            silhouette = [
                (x1 + hub_half_w * math.cos(perp), y1 + hub_half_w * math.sin(perp)),
                (x2 + tip_half_w * math.cos(perp), y2 + tip_half_w * math.sin(perp)),
                (x2 - tip_half_w * math.cos(perp), y2 - tip_half_w * math.sin(perp)),
                (x1 - hub_half_w * math.cos(perp), y1 - hub_half_w * math.sin(perp)),
            ]
            silhouette.append(silhouette[0])
            msp.add_lwpolyline(silhouette, dxfattribs={'layer': 'Geometry'})

        place_text(msp.add_text(f"O {tip_diameter:.1f} mm (Tip)", dxfattribs={'layer': 'Text'}),
                   (50, tip_radius_m * 1000 + 120))
        place_text(msp.add_text(f"O {hub_diameter:.1f} mm (Hub)", dxfattribs={'layer': 'Text'}),
                   (50, hub_radius_m * 1000 + 50))
        place_text(msp.add_text(f"Blades: {blade_count} @ {blade_pitch:.1f} mm pitch",
                   dxfattribs={'layer': 'Text'}), (-tip_radius_m * 1000 - 200, tip_radius_m * 1000 - 100))

        # ---- Side (developed r-vs-axial) view: REAL LE/TE curves ----
        # LE is the stacking spine -> straight line at axial offset 0 for
        # every station (see module docstring). TE = LE + chord(r)*sin(beta(r)),
        # taken straight from the same arrays the solid was lofted from.
        # Gap is proportional to fan size (was a fixed +300mm, which for
        # a small fan left a huge empty gap and made the whole sheet's
        # bounding box many times bigger than the fan itself - "fit to
        # view" then rendered the fan as a tiny speck next to empty space).
        gap = tip_radius_m * 1000 * 0.4
        view_offset_x = tip_radius_m * 1000 * 2 + gap
        r_to_y = lambda r_m: r_m * 1000  # radius maps to the view's vertical axis

        le_pts = []
        te_pts = []
        for r, ch, beta in zip(self.station_radii, self.station_chords, self.station_beta_rad):
            le_x = view_offset_x
            te_x = view_offset_x + ch * math.sin(beta) * 1000
            y = r_to_y(r)
            le_pts.append((le_x, y))
            te_pts.append((te_x, y))

        for i in range(len(le_pts) - 1):
            msp.add_line(le_pts[i], le_pts[i + 1], dxfattribs={'layer': 'Geometry'})
            msp.add_line(te_pts[i], te_pts[i + 1], dxfattribs={'layer': 'Geometry'})
        # Root and tip closing lines (root/tip chord)
        msp.add_line(le_pts[0], te_pts[0], dxfattribs={'layer': 'Geometry'})
        msp.add_line(le_pts[-1], te_pts[-1], dxfattribs={'layer': 'Geometry'})

        place_text(msp.add_text(f"{hub_width:.1f} mm hub width", dxfattribs={'layer': 'Text'}),
                   (view_offset_x, r_to_y(self.station_radii[0]) - 80))
        place_text(msp.add_text(f"Root chord: {root_chord_mm:.1f} mm @ {root_beta_deg:.1f} deg",
                   dxfattribs={'layer': 'Text'}),
                   (view_offset_x - 250, r_to_y(self.station_radii[0]) - 130))
        place_text(msp.add_text(f"Tip chord: {tip_chord_mm:.1f} mm @ {tip_beta_deg:.1f} deg",
                   dxfattribs={'layer': 'Text'}),
                   (view_offset_x - 250, r_to_y(self.station_radii[-1]) + 60))

        # ---- Real airfoil cross-sections at root and tip, drawn to scale ----
        def draw_airfoil(cx, cy, chord_m):
            pts = [(cx + xf * chord_m * 1000, cy + yf * chord_m * 1000)
                   for xf, yf in self.airfoil_loop]
            pts.append(pts[0])
            msp.add_lwpolyline(pts, dxfattribs={'layer': 'Geometry'})

        airfoil_view_x = view_offset_x + tip_radius_m * 1000 * 0.6 + gap
        draw_airfoil(airfoil_view_x, 0, self.station_chords[0])
        place_text(msp.add_text("Root section", dxfattribs={'layer': 'Text'}),
                   (airfoil_view_x, -150))

        draw_airfoil(airfoil_view_x, 400, self.station_chords[-1])
        place_text(msp.add_text("Tip section", dxfattribs={'layer': 'Text'}),
                   (airfoil_view_x, 250))

        place_text(msp.add_text("Axial Fan Blade Assembly", dxfattribs={'layer': 'Text', 'height': 5}),
                   (-tip_radius_m * 1000 - 100, -tip_radius_m * 1000 - 100))
        place_text(msp.add_text("Scale: 1:1 (mm)", dxfattribs={'layer': 'Text'}),
                   (-tip_radius_m * 1000 - 100, -tip_radius_m * 1000 - 150))

        doc.saveas(output_path)
        print(f"Exported DXF: {output_path}")

    def export_true_2d_views(self, output_dir: str, base_name: str = "fan") -> dict:
        """Exports GENUINE flattened 2D orthographic views (front + side)
        by projecting the ACTUAL solid (hub + blades + casing + shaft, all
        the real fused/lofted geometry) through FreeCAD's TechDraw engine
        - not a separately hand-drawn approximation. This is what makes
        the 2D DXF and the 3D STEP/OBJ guaranteed to match, same as the
        CyclonApp pipeline's _export_view_dxf: TechDraw.writeDXFView on a
        DrawViewPart, falling back to a raw wireframe dump if TechDraw's
        headless writer is unavailable on this FreeCAD build."""
        shapes = [self.hub_solid] + self.blade_solids
        if self.casing_solid is not None:
            shapes.append(self.casing_solid)
        if self.shaft_solid is not None:
            shapes.append(self.shaft_solid)
        if self.hub_solid is None or not self.blade_solids:
            raise BladeGeometryError("No valid geometry to project (hub or blades missing)")

        combined = Part.makeCompound(shapes)
        tmp_obj = self.doc.addObject("Part::Feature", "TmpAssembly2D")
        tmp_obj.Shape = combined
        self.doc.recompute()

        views = {"front": Vector(0, -1, 0), "side": Vector(1, 0, 0), "top": Vector(0, 0, -1)}
        result = {}
        for name, direction in views.items():
            filename = f"{base_name}_{name}_2d.dxf"
            dxf_path = os.path.join(output_dir, filename)

            page = self.doc.addObject("TechDraw::DrawPage", f"Page_{name}")
            template = self.doc.addObject("TechDraw::DrawSVGTemplate", f"Tpl_{name}")
            template_path = os.path.join(
                App.getResourceDir(), "Mod", "TechDraw", "Templates", "A3_Landscape.svg"
            )
            if os.path.isfile(template_path):
                template.Template = template_path
            page.Template = template

            view = self.doc.addObject("TechDraw::DrawViewPart", f"View_{name}")
            view.Source = [tmp_obj]
            view.Direction = direction
            view.Scale = 1.0
            view.X = 0
            view.Y = 0
            # Show hidden-line edges too - without this, blades that fall
            # behind another blade or the casing from this view angle are
            # silently dropped (only 1-2 of N blades survive projection),
            # which is why earlier exports looked like most blades were
            # missing. Property name varies by FreeCAD build, so this is
            # best-effort and non-fatal if unavailable.
            for prop_name in ("HardHidden", "VisibleHiddenEdges"):
                try:
                    setattr(view, prop_name, True)
                except Exception:
                    pass
            page.addView(view)
            self.doc.recompute()

            try:
                TechDraw.writeDXFView(view, dxf_path)
            except Exception as e:
                print(f"WARNING: writeDXFView failed for {name} ({e}), falling back to raw wireframe")
                try:
                    import importDXF
                    importDXF.export([tmp_obj], dxf_path)
                except Exception as e2:
                    print(f"WARNING: fallback export also failed for {name}: {e2}")
                    dxf_path = None

            for obj in (page, template, view):
                try:
                    self.doc.removeObject(obj.Name)
                except Exception:
                    pass
            result[name] = dxf_path

        try:
            self.doc.removeObject(tmp_obj.Name)
        except Exception:
            pass

        print(f"Exported true 2D views (projected from actual solid): {result}")
        return result


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
            "rpm": 300,               # was 1500 - low rpm gives a real
                                       # pitch angle instead of near-
                                       # feathered hairline blades
            "axial_velocity_ms": 12.0,  # was 5.0 - raises pitch angle
            "span_stations": 6,
            "target_solidity": 0.9,   # was 0.5 - wider, more visible chord
            "stagger_angle_deg": 30.0,
            "profile_coordinate_json": None,
            "casing_length_m": 0.15,  # was unset - casing+shaft now
                                       # included in the default test run,
                                       # matching cyclone's full-assembly
                                       # default output
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

        # Casing/shaft/flange - optional. Only built when casing_length_m
        # is given, so existing rotor-only requests behave unchanged.
        if params.get("casing_length_m"):
            gen.build_casing_assembly(
                tip_radius_m=params["tip_radius_m"],
                casing_length_m=params["casing_length_m"],
                shaft_radius_m=params.get("shaft_radius_m") or (
                    params["tip_radius_m"] * params.get("hub_ratio", 0.45) * 0.5
                ),
                tip_clearance_m=params.get("tip_clearance_m", 0.002),
                wall_thickness_m=params.get("wall_thickness_m", 0.003),
                flange_thickness_m=params.get("flange_thickness_m", 0.008),
                flange_width_m=params.get("flange_width_m", 0.02),
                flange_bolt_dia_m=params.get("flange_bolt_dia_m", 0.008),
                flange_bolt_count=params.get("flange_bolt_count", 8),
                shaft_protrude_m=params.get("shaft_protrude_m", 0.03),
            )

        # Export files
        case_id = params.get("case_id", "test")
        step_file = os.path.join(output_dir, f"fan_{case_id}.step")
        dxf_file = os.path.join(output_dir, f"fan_{case_id}.dxf")
        obj_file = os.path.join(output_dir, f"fan_{case_id}.obj")

        gen.export_step(step_file)
        gen.export_obj(obj_file)

        # ONLY 2D deliverable: real flattened views projected from the
        # actual 3D solid via TechDraw (front/top/side) - same approach
        # as the cyclone pipeline. The old export_dxf() (a separately
        # hand-drawn sketch built from formulas, not a projection of the
        # real solid) is no longer called - it repeatedly produced
        # scattered/inconsistent drawings because it was never actually
        # derived from the same geometry as the STEP/OBJ.
        true_2d_paths = gen.export_true_2d_views(output_dir, base_name=f"fan_{case_id}")
        dxf_file = true_2d_paths.get("front")

        print(f"[Blade CAD] SUCCESS: Generated {step_file}, {dxf_file}, {obj_file}, {true_2d_paths}")
        sys.exit(0)

    except Exception as e:
        print(f"[Blade CAD] ERROR: {e}")
        import traceback
        traceback.print_exc()
        sys.exit(1)