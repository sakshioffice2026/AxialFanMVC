import os
import sys
import numpy as np
print("REACHED TOP", flush=True)

import pyvista as pv
print("PYVISTA IMPORTED", flush=True)

def render(case_path, output_dir):
    os.makedirs(output_dir, exist_ok=True)
    print("STEP 1: dir created", flush=True)

    foam_file = os.path.join(case_path, "case.foam")
    if not os.path.exists(foam_file):
        open(foam_file, "w").close()
    print("STEP 2: foam file ready", flush=True)

    reader = pv.OpenFOAMReader(foam_file)
    print("STEP 3: reader created", flush=True)

    reader.cell_to_point_creation = True
    if reader.time_values:
        reader.set_active_time_value(reader.time_values[-1])
    print("STEP 4: time value set", flush=True)

    mesh = reader.read().combine()
    print("STEP 5: mesh read and combined", flush=True)

    # Fan surface geometry, generated alongside the case by BladeStlGenerator.
    # Used as a structural actor in both panels; optional so a missing/older
    # case (pre-dating the STL generator) still renders the data panels.
    stl_path = os.path.join(case_path, "constant", "triSurface", "fan.stl")
    geom = pv.read(stl_path) if os.path.exists(stl_path) else None
    print(f"STEP 6: geometry {'loaded' if geom is not None else 'skipped (missing)'}", flush=True)

    print(f"STEP 6b: mesh bounds {mesh.bounds}", flush=True)
    if geom is not None:
        print(f"STEP 6c: geom bounds {geom.bounds}", flush=True)

    # Data-driven color ranges — fixed guesses (0-60 m/s, -500..1500 Pa)
    # don't match this case's actual field values, which is why the
    # pressure slice rendered as one flat color. Reading real min/max
    # from the solved fields and logging them avoids further guessing.
    p_min, p_max = float(mesh["p"].min()), float(mesh["p"].max())
    u_mag_field = np.linalg.norm(mesh["U"], axis=1)
    u_min, u_max = float(u_mag_field.min()), float(u_mag_field.max())
    print(f"STEP 6d: pressure range in data: [{p_min:.2f}, {p_max:.2f}] Pa", flush=True)
    print(f"STEP 6e: velocity magnitude range in data: [{u_min:.2f}, {u_max:.2f}] m/s", flush=True)

    sliced = mesh.slice(normal="y", origin=mesh.center)
    print("STEP 7: slice done", flush=True)

    # Seed disc sized to the fan's XY footprint (not the full domain —
    # mesh.bounds is the far-field box, much larger than the fan).
    mxmin, mxmax, mymin, mymax, mzmin, mzmax = mesh.bounds
    if geom is not None:
        gxmin, gxmax, gymin, gymax, gzmin, gzmax = geom.bounds
        seed_x, seed_y = (gxmin + gxmax) / 2.0, (gymin + gymax) / 2.0
        seed_radius = 1.15 * max(gxmax - gxmin, gymax - gymin) / 2.0
    else:
        seed_x, seed_y = (mxmin + mxmax) / 2.0, (mymin + mymax) / 2.0
        seed_radius = 0.15 * max(mxmax - mxmin, mymax - mymin) / 2.0

    # Seed Z position: try increasing distances from the domain's actual
    # inlet (mesh z_min), not an assumed offset upstream of the blade.
    # A fixed blade-thickness offset failed on this case: the fan sits
    # almost exactly at mesh z_min (geom z in [-0.0004, 0.0313] vs domain
    # z in [0, 1.908]), so there's effectively no fluid region upstream
    # of the blades — any offset derived from the blade itself placed
    # seeds outside the mesh (0 points, then a crash on an empty mesh).
    z_span = mzmax - mzmin
    streamlines = None
    for frac in (0.005, 0.02, 0.05, 0.1, 0.2):
        seed_z = mzmin + frac * z_span
        seed = pv.Disc(center=(seed_x, seed_y, seed_z), inner=0.0,
                        outer=seed_radius, normal=(0, 0, 1), r_res=6, c_res=8)
        candidate = mesh.streamlines_from_source(
            seed, vectors="U", integration_direction="forward",
            max_length=100.0, initial_step_length=0.05)
        print(f"STEP 8 (seed z={seed_z:.4f}, frac={frac}): {candidate.n_points} points", flush=True)
        if candidate.n_points > 0:
            streamlines = candidate
            break

    if streamlines is not None:
        streamlines["U_magnitude"] = np.linalg.norm(streamlines["U"], axis=1)
    else:
        print("STEP 8: WARNING - no seed position produced streamlines; Panel 1 will show geometry only", flush=True)
    print(f"STEP 8: done ({streamlines.n_points if streamlines is not None else 0} points)", flush=True)

    # Focus region for the camera, centered on the fan — reset_camera()
    # alone fits the WHOLE domain (Z spans 1.9m vs the fan's 0.03m), so
    # the fan renders as a speck with the actual flow structure invisible.
    # A box a few fan-diameters across, centered on the fan, matches the
    # tight framing in the reference image.
    if geom is not None:
        gxmin, gxmax, gymin, gymax, gzmin, gzmax = geom.bounds
        fan_span = max(gxmax - gxmin, gymax - gymin)
        cx, cy, cz = geom.center
    else:
        fan_span = seed_radius * 2.0
        cx, cy, cz = seed_x, seed_y, mzmin
    pad = fan_span * 2.5
    focus_bounds = (cx - pad, cx + pad, cy - pad, cy + pad, cz - pad, cz + pad)
    print(f"STEP 8f: camera focus bounds {focus_bounds}", flush=True)

    plotter = pv.Plotter(shape=(1, 2), off_screen=True, window_size=[2000, 1000])
    plotter.set_background("#e8e8e8")
    print("STEP 9: plotter created", flush=True)

    # --- Panel 1: geometry + velocity streamlines, isometric ---
    plotter.subplot(0, 0)
    plotter.add_text("Geometry + Flow Visualization (Isometric View)", font_size=10)
    if geom is not None:
        plotter.add_mesh(geom, color="lightgray", smooth_shading=True,
                          specular=0.5, specular_power=15)
    if streamlines is not None:
        plotter.add_mesh(streamlines, scalars="U_magnitude", cmap="jet",
                          clim=[u_min, u_max], line_width=2,
                          scalar_bar_args={"title": "Velocity Magnitude (m/s)"})
    plotter.view_isometric()
    plotter.reset_camera(bounds=focus_bounds)
    plotter.show_axes()
    print("STEP 10: panel 1 built", flush=True)

    # --- Panel 2: pressure slice + wireframe geometry context, side view ---
    plotter.subplot(0, 1)
    plotter.add_text("Quantitative Pressure Slice (Side View)", font_size=10)
    if geom is not None:
        plotter.add_mesh(geom, style="wireframe", color="gray", opacity=0.5)
    plotter.add_mesh(sliced, scalars="p", cmap="coolwarm", clim=[p_min, p_max],
                      scalar_bar_args={"title": "Static Pressure (Pa)"})
    plotter.view_xz()
    slice_pad = pad * 2.0
    slice_bounds = (cx - slice_pad, cx + slice_pad, cy - slice_pad, cy + slice_pad,
                     cz - slice_pad, cz + slice_pad)
    plotter.reset_camera(bounds=slice_bounds)
    print("STEP 11: panel 2 built", flush=True)

    png_path = os.path.join(output_dir, "cfd_result.png")
    plotter.screenshot(png_path)
    print("STEP 12: screenshot done", flush=True)

    plotter.close()
    print("STEP 13: plotter closed", flush=True)

    vtp_path = os.path.join(output_dir, "pressure_slice.vtp")
    sliced.save(vtp_path)
    print("STEP 14: vtp saved", flush=True)

    return png_path, vtp_path


if __name__ == "__main__":
    print("ENTERED MAIN", flush=True)
    if len(sys.argv) < 3:
        print("Usage: render_result.py <case_path> <output_dir>", file=sys.stderr, flush=True)
        sys.exit(1)
    result_png_path, result_vtp_path = render(sys.argv[1], sys.argv[2])
    print(f"{result_png_path}|{result_vtp_path}")