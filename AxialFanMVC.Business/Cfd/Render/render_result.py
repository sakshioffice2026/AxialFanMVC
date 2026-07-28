"""
Renders a pressure-slice PNG and .vtp from a finished OpenFOAM case.

Replaces the old ActiViz.NET/Kitware.VTK renderer (CfdVtkRenderer.cs) and
the standalone net48 CfdRenderHost project — pyvista's VTK bindings are
normal cross-platform pip wheels, not a mixed-mode .NET assembly, so none
of the .NET Framework/CoreCLR incompatibility that broke ActiViz applies
here.

Usage:   python render_result.py <casePath> <outputDir>
Success: prints "<pngPath>|<vtpPath>" to stdout, exits 0.
Failure: prints the error to stderr, exits 1.

Called by AxialFanMVC/Services/CfdVtkRenderer.cs the same way
LocalCfdOrchestrator.cs shells out to wsl.exe for OpenFOAM itself.
"""

import os
import sys

import pyvista as pv


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

    # reader.read() returns a MultiBlock (one block per OpenFOAM
    # region/patch) — combine() flattens it into a single mesh, same
    # purpose as the manual vtkAppendFilter loop in the old renderer.
    mesh = reader.read().combine()

    sliced = mesh.slice(normal="y", origin=mesh.center)

    plotter = pv.Plotter(off_screen=True, window_size=[1600, 1000])
    plotter.set_background("#26262e")
    plotter.add_mesh(
        sliced,
        scalars="p",
        cmap="coolwarm",
        scalar_bar_args={"title": "Static Pressure (Pa)"},
    )
    # An axis-aligned camera (e.g. "xy") can end up looking straight into
    # the slice plane edge-on, depending on which axis the slice's normal
    # is on — isometric is never edge-on to a single-axis-aligned plane,
    # so it's the safe default regardless of the geometry's orientation.
    plotter.view_isometric()
    plotter.reset_camera()

    png_path = os.path.join(output_dir, "pressure_slice.png")
    plotter.screenshot(png_path)
    plotter.close()

    vtp_path = os.path.join(output_dir, "pressure_slice.vtp")
    sliced.save(vtp_path)

    return png_path, vtp_path


if __name__ == "__main__":
    if len(sys.argv) < 3:
        print("Usage: render_result.py <casePath> <outputDir>", file=sys.stderr)
        sys.exit(1)

    try:
        png, vtp = render(sys.argv[1], sys.argv[2])
        print(f"{png}|{vtp}")
    except Exception as exc:  # noqa: BLE001 - surfaced to the C# caller's stderr
        print(str(exc), file=sys.stderr)
        sys.exit(1)