"""
Renders a two-panel PNG (geometry + streamlines, and a pressure slice) and a
.vtp from a finished OpenFOAM case.

Replaces the old ActiViz.NET/Kitware.VTK renderer (CfdVtkRenderer.cs) and
the standalone net48 CfdRenderHost project — pyvista's VTK bindings are
normal cross-platform pip wheels, not a mixed-mode .NET assembly, so none
of the .NET Framework/CoreCLR incompatibility that broke ActiViz applies
here.

Usage:   python render_result.py <casePath> <outputDir>
Success: prints "<pngPath>|<vtpPath>" as the LAST line of stdout, exits 0.
Failure: prints the error to stderr, exits 1.

Called by AxialFanMVC/Services/CfdVtkRenderer.cs the same way
LocalCfdOrchestrator.cs shells out to wsl.exe for OpenFOAM itself.
"""

import os
import sys

import numpy as np
import pyvista as pv

# Confirmed patch name for the fan blade boundary in the OpenFOAM case.
BLADE_PATCH_NAME = "fan"


def _percentile_clim(array, lo=1.0, hi=99.0):
    """Robust color range: clamps outlier cells instead of using raw
    min/max, which otherwise lets a single extreme cell (e.g. a near-wall
    boundary-layer artifact) wash out the entire colormap."""
    lo_val = float(np.percentile(array, lo))
    hi_val = float(np.percentile(array, hi))
    if lo_val == hi_val:
        # Degenerate field (e.g. all zeros) — avoid a zero-width clim.
        return [lo_val - 1.0, hi_val + 1.0]
    return [lo_val, hi_val]


def render(case_path: str, output_dir: str) -> tuple[str, str]:
    os.makedirs(output_dir, exist_ok=True)

    # pyvista's OpenFOAMReader, like the OpenFOAM readers before it, needs
    # a dummy .foam marker file to identify the case root.
    foam_file = os.path.join(case_path, "case.foam")
    if not os.path.exists(foam_file):
        open(foam_file, "w").close()

    reader = pv.OpenFOAMReader(foam_file)
    reader.cell_to_point_creation = True
    if reader.time_values:
        reader.set_active_time_value(reader.time_values[-1])

    blocks = reader.read()

    # reader.read() returns a MultiBlock (internalMesh + boundary patches).
    # combine() flattens the volume mesh into a single mesh for slicing and
    # streamline seeding, same purpose as the manual vtkAppendFilter loop
    # in the old renderer.
    mesh = blocks.combine()

    boundary = blocks["boundary"]
    if BLADE_PATCH_NAME not in boundary.keys():
        raise RuntimeError(
            f"Patch '{BLADE_PATCH_NAME}' not found in case boundary. "
            f"Available patches: {list(boundary.keys())}"
        )
    blade = boundary[BLADE_PATCH_NAME]
    if blade.n_points == 0:
        raise RuntimeError(f"Patch '{BLADE_PATCH_NAME}' has zero points — empty geometry.")

    sliced = mesh.slice(normal="y", origin=mesh.center)

    # Frame the camera on a box sized from the blade's PLANAR extent
    # (its largest in-plane dimension), not its raw bounds — the blade is
    # very thin along one axis (a few cm) versus ~1m across, and fitting
    # the camera to that raw, wildly disproportionate box zooms out to
    # the huge far-field domain instead of the blade region.
    cx, cy, cz = blade.center
    planar_size = max(
        blade.bounds[1] - blade.bounds[0],
        blade.bounds[3] - blade.bounds[2],
    )
    half = planar_size * 0.75
    frame_bounds = (cx - half, cx + half, cy - half, cy + half, cz - half, cz + half)

    # Crop the slice to the same near-blade region — otherwise the vast,
    # near-uniform far-field pressure swamps both the visible frame and
    # the color range, hiding the local variation around the blade.
    near_slice = sliced.clip_box(frame_bounds, invert=False)

    plotter = pv.Plotter(off_screen=True, shape=(1, 2), window_size=[1800, 900])
    plotter.set_background("#e8e8e8")

    # --- Panel 1: geometry + flow streamlines, isometric view ---
    plotter.subplot(0, 0)
    plotter.add_text("Geometry + Flow Visualization (Isometric View)", font_size=10)
    plotter.add_mesh(blade, color="#9a9a9e", specular=0.6, specular_power=25)

    # Seed streamlines relative to the BLADE's size/position, not the
    # domain's — the domain is 3-4x the blade radius, so a domain-scale
    # seed radius scatters seeds far from the blade and only captures
    # uniform far-field flow (the all-straight-blue-lines symptom).
    streams = mesh.streamlines(
        vectors="U",
        source_center=blade.center,
        source_radius=blade.length * 0.8,
        n_points=40,
        max_length=blade.length * 4,
        initial_step_length=0.05,
        terminal_speed=1e-6,
    )
    if streams.n_points > 0:
        u_clim = _percentile_clim(streams["U"])
        plotter.add_mesh(
            streams.tube(radius=blade.length * 0.01),
            scalars="U",
            cmap="turbo",
            clim=u_clim,
            scalar_bar_args={"title": "Velocity Magnitude (m/s)"},
        )

    plotter.view_isometric()
    # Frame on the well-proportioned box, not the blade's raw (Z-thin) bounds.
    plotter.reset_camera(bounds=frame_bounds)

    # --- Panel 2: quantitative pressure slice, with blade for context ---
    plotter.subplot(0, 1)
    plotter.add_text("Quantitative Pressure Slice (Side View)", font_size=10)
    plotter.add_mesh(blade, style="wireframe", color="white", opacity=0.4)
    p_clim = _percentile_clim(near_slice["p"])
    plotter.add_mesh(
        near_slice,
        scalars="p",
        cmap="coolwarm",
        clim=p_clim,
        scalar_bar_args={"title": "Static Pressure (Pa)"},
    )
    # The slice's normal is "y", so it spans the X-Z plane — view_xz looks
    # along the Y axis, face-on to the slice. view_yz (looking along X)
    # would view this same slice edge-on, collapsing it to a thin line.
    plotter.view_xz()
    plotter.reset_camera(bounds=frame_bounds)

    png_path = os.path.join(output_dir, "pressure_slice.png")
    plotter.screenshot(png_path)
    plotter.close()

    vtp_path = os.path.join(output_dir, "pressure_slice.vtp")
    near_slice.extract_surface().save(vtp_path)

    return png_path, vtp_path


if __name__ == "__main__":
    if len(sys.argv) < 3:
        print("Usage: render_result.py <casePath> <outputDir>", file=sys.stderr)
        sys.exit(1)

    try:
        png, vtp = render(sys.argv[1], sys.argv[2])
        # Flush explicitly and print as the final statement so the C# side
        # can safely parse "the last non-empty stdout line" instead of the
        # whole buffer, in case any library prints incidental warnings
        # to stdout earlier in the run.
        print(f"{png}|{vtp}", flush=True)
    except Exception as exc:  # noqa: BLE001 - surfaced to the C# caller's stderr
        print(str(exc), file=sys.stderr, flush=True)
        sys.exit(1)