import os
import sys
import math
import numpy as np
print("REACHED TOP", flush=True)

import pyvista as pv
print("PYVISTA IMPORTED", flush=True)

def _bounds_extent(bounds, axes=(0, 1, 2)):
    """Diagonal of bounds restricted to the given axes (0=x,1=y,2=z).
    An orthographic view down an axis (e.g. view_xz looks along Y)
    doesn't put that axis's extent on screen at all, so it must be
    excluded or the zoom factor comes out wrong for that panel."""
    xmin, xmax, ymin, ymax, zmin, zmax = bounds
    spans = {0: xmax - xmin, 1: ymax - ymin, 2: zmax - zmin}
    return math.sqrt(sum(spans[a] ** 2 for a in axes))


def _zoom_factor_for(full_bounds, focus_bounds, axes=(0, 1, 2), min_zoom=1.0, max_zoom=200.0):
    """How much to zoom in after reset_camera() has framed full_bounds,
    to instead tightly frame focus_bounds. reset_camera(bounds=...) is
    unreliable in this environment (doesn't actually re-tighten the
    frustum to the given box), so we fit the full scene first and then
    zoom by the ratio of the two regions' on-screen extents."""
    full_extent = _bounds_extent(full_bounds, axes)
    focus_extent = _bounds_extent(focus_bounds, axes)
    if focus_extent <= 0:
        return min_zoom
    factor = full_extent / focus_extent
    return max(min_zoom, min(max_zoom, factor))


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

    sliced = mesh.slice(normal="y", origin=mesh.center)
    print("STEP 7: slice done", flush=True)

    # True min/max fixed the "one flat color" bug, but a handful of
    # near-wall cells at the blade surface (e.g. -694 Pa) still dominate
    # the scale, compressing the visually-important far-field region into
    # a narrow sliver of `coolwarm` — reads as "mostly flat dark red" even
    # though it's technically no longer a single color. Clipping to the
    # 2nd-98th percentile excludes those few outlier cells from setting
    # the scale (they simply saturate to the colormap's end colors) so the
    # bulk of the field spreads across the full visible color range.
    p_data = sliced["p"]
    p_lo, p_hi = np.percentile(p_data, [2, 98])
    p_min, p_max = float(p_lo), float(p_hi)
    if p_max <= p_min:  # degenerate/near-uniform field - fall back to true range
        p_min, p_max = float(p_data.min()), float(p_data.max())
    print(f"STEP 7b: pressure clim (2nd-98th pct): [{p_min:.2f}, {p_max:.2f}] Pa "
          f"(true range [{float(p_data.min()):.2f}, {float(p_data.max()):.2f}])", flush=True)

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
        # Same issue as pressure: true min/max is set by a handful of
        # near-blade high-speed points, stretching the jet colormap so the
        # bulk of streamlines (mostly mid-range freestream speed) collapse
        # into one shade of green with barely any visible variation.
        # Percentile-clip so those few fast points saturate to red instead
        # of setting the scale for everything else.
        u_data = streamlines["U_magnitude"]
        u_lo, u_hi = np.percentile(u_data, [2, 98])
        u_min, u_max = float(u_lo), float(u_hi)
        if u_max <= u_min:  # degenerate/near-uniform speed - fall back to true range
            u_min, u_max = float(u_data.min()), float(u_data.max())
        print(f"STEP 8b: velocity clim (2nd-98th pct): [{u_min:.2f}, {u_max:.2f}] m/s "
              f"(true range [{float(u_data.min()):.2f}, {float(u_data.max()):.2f}])", flush=True)
    else:
        print("STEP 8: WARNING - no seed position produced streamlines; Panel 1 will show geometry only", flush=True)
        # Nothing gets colored by U in this case, so the range is unused —
        # kept as a harmless fallback rather than left undefined.
        u_mag_field = np.linalg.norm(mesh["U"], axis=1)
        u_min, u_max = float(u_mag_field.min()), float(u_mag_field.max())
    print(f"STEP 8: done ({streamlines.n_points if streamlines is not None else 0} points)", flush=True)

    # Focus region for the camera, driven by what's actually drawn (fan
    # geometry + resulting streamlines) rather than an arbitrary multiple
    # of the fan's own footprint. A fan_span*2.5 pad produced a box ~4.77m
    # wide here — bigger than the whole 2.86m x 2.86m x 1.91m domain — so
    # reset_camera()+zoom had nothing tighter to zoom into and did nothing.
    # Using the real extent of the drawn content keeps the box smaller
    # than the domain (guaranteeing an actual zoom-in) and also avoids
    # clipping off the streamlines, which reach well past the fan itself.
    geoms_to_frame = []
    if geom is not None:
        geoms_to_frame.append(geom.bounds)
    if streamlines is not None:
        geoms_to_frame.append(streamlines.bounds)
    if not geoms_to_frame:
        geoms_to_frame.append(mesh.bounds)

    # ── Flow-direction arrow — a real 3D glyph in the solve's own
    # coordinate system, not a pixel-position guess overlaid after the
    # fact. Placed just outside the fan's footprint (so it never overlaps
    # geometry/streamlines) and spans mesh z_min (inlet, same reference
    # point streamline seeding already uses) toward the seed's z, matching
    # the solver's actual +Z flow direction (streamlines_from_source used
    # integration_direction="forward" from a seed near mzmin).
    content_bxmin = min(b[0] for b in geoms_to_frame)
    content_bxmax = max(b[1] for b in geoms_to_frame)
    content_bymin = min(b[2] for b in geoms_to_frame)
    content_bymax = max(b[3] for b in geoms_to_frame)

    arrow_x = content_bxmax + 0.12 * max(content_bxmax - content_bxmin, 1e-6)
    arrow_y = (content_bymin + content_bymax) / 2.0
    arrow_z_start = mzmin + 0.02 * z_span
    arrow_z_end = mzmin + 0.30 * z_span
    arrow_length = arrow_z_end - arrow_z_start

    flow_arrow = pv.Arrow(
        start=(arrow_x, arrow_y, arrow_z_start),
        direction=(0, 0, 1),
        scale=arrow_length,
        tip_length=0.35, tip_radius=0.12, shaft_radius=0.045,
    )
    flow_label_points = np.array([
        (arrow_x, arrow_y, arrow_z_start),
        (arrow_x, arrow_y, arrow_z_end),
    ])
    flow_label_text = ["INLET (air in)", "OUTLET (air out)"]
    print(f"STEP 8g: flow arrow x={arrow_x:.3f} z=[{arrow_z_start:.3f}, {arrow_z_end:.3f}]", flush=True)

    # Fold the arrow into the same framing math as everything else, so the
    # camera zoom that already accounts for geometry+streamlines also
    # guarantees the arrow (and its labels) stay on-screen in both panels.
    geoms_to_frame.append(flow_arrow.bounds)

    fxmin = min(b[0] for b in geoms_to_frame)
    fxmax = max(b[1] for b in geoms_to_frame)
    fymin = min(b[2] for b in geoms_to_frame)
    fymax = max(b[3] for b in geoms_to_frame)
    fzmin = min(b[4] for b in geoms_to_frame)
    fzmax = max(b[5] for b in geoms_to_frame)

    # 25% margin on each axis so the content isn't touching the frame edge
    margin_x = 0.25 * max(fxmax - fxmin, 1e-6)
    margin_y = 0.25 * max(fymax - fymin, 1e-6)
    margin_z = 0.25 * max(fzmax - fzmin, 1e-6)
    focus_bounds = (fxmin - margin_x, fxmax + margin_x,
                     fymin - margin_y, fymax + margin_y,
                     fzmin - margin_z, fzmax + margin_z)
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
                          clim=[u_min, u_max], line_width=3,
                          scalar_bar_args={"title": "Velocity Magnitude (m/s)"})
    plotter.add_mesh(flow_arrow, color="black")
    plotter.add_point_labels(flow_label_points, flow_label_text, font_size=14,
                              text_color="black", shape_color="white",
                              shape_opacity=0.85, always_visible=True, show_points=False)
    plotter.view_isometric()
    plotter.reset_camera()
    zoom1 = _zoom_factor_for(mesh.bounds, focus_bounds, axes=(0, 1, 2))
    plotter.camera.zoom(zoom1)
    print(f"STEP 10 (zoom={zoom1:.2f}): panel 1 built", flush=True)
    plotter.show_axes()

    # --- Panel 2: pressure slice + wireframe geometry context, side view ---
    plotter.subplot(0, 1)
    plotter.add_text("Quantitative Pressure Slice (Side View)", font_size=10)
    if geom is not None:
        plotter.add_mesh(geom, style="wireframe", color="gray", opacity=0.5)
    plotter.add_mesh(sliced, scalars="p", cmap="coolwarm", clim=[p_min, p_max],
                      scalar_bar_args={"title": "Static Pressure (Pa)"})
    plotter.add_mesh(flow_arrow, color="black")
    plotter.add_point_labels(flow_label_points, flow_label_text, font_size=14,
                              text_color="black", shape_color="white",
                              shape_opacity=0.85, always_visible=True, show_points=False)
    plotter.view_xz()
    plotter.reset_camera()
    # view_xz is an orthographic projection along Y, so Y-extent never
    # reaches the screen — including it (as the original slice_bounds
    # math effectively did via a bigger, Y-inclusive box) understates
    # the true on-screen size and leaves the slice too small.
    zoom2 = _zoom_factor_for(mesh.bounds, focus_bounds, axes=(0, 2))
    plotter.camera.zoom(zoom2)
    print(f"STEP 11 (zoom={zoom2:.2f}): panel 2 built", flush=True)

    png_path = os.path.join(output_dir, "cfd_result.png")
    plotter.screenshot(png_path)
    print("STEP 12: screenshot done", flush=True)

    plotter.close()
    print("STEP 13: plotter closed", flush=True)

    vtp_path = os.path.join(output_dir, "pressure_slice.vtp")
    sliced.save(vtp_path)
    print("STEP 14: vtp saved", flush=True)

    # Streamlines were already computed in-memory above (Panel 1); persist
    # them as their own .vtp so the browser can load velocity streamlines
    # independently of the pressure slice. None when no seed produced any
    # (see the STEP 8 warning above) - that's an expected outcome for some
    # geometries, not a failure.
    streamlines_path = None
    if streamlines is not None:
        streamlines_path = os.path.join(output_dir, "streamlines.vtp")
        streamlines.save(streamlines_path)
        print("STEP 15: streamlines vtp saved", flush=True)

    return png_path, vtp_path, streamlines_path


if __name__ == "__main__":
    print("ENTERED MAIN", flush=True)
    if len(sys.argv) < 3:
        print("Usage: render_result.py <case_path> <output_dir>", file=sys.stderr, flush=True)
        sys.exit(1)
    result_png_path, result_vtp_path, result_streamlines_path = render(sys.argv[1], sys.argv[2])
    print(f"{result_png_path}|{result_vtp_path}|{result_streamlines_path or ''}")