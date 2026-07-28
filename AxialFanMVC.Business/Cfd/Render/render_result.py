import os
import sys
print("REACHED TOP", flush=True)

import pyvista as pv
print("PYVISTA IMPORTED", flush=True)
if __name__ == "__main__":
    print("ENTERED MAIN", flush=True)
    if len(sys.argv) < 3:
        ...

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

    sliced = mesh.slice(normal="y", origin=mesh.center)
    print("STEP 6: slice done", flush=True)

    plotter = pv.Plotter(off_screen=True, window_size=[1600, 1000])
    print("STEP 7: plotter created", flush=True)

    plotter.set_background("#26262e")
    plotter.add_mesh(sliced, scalars="p", cmap="coolwarm",
                      scalar_bar_args={"title": "Static Pressure (Pa)"})
    print("STEP 8: mesh added", flush=True)

    plotter.view_isometric()
    plotter.reset_camera()
    print("STEP 9: camera set", flush=True)

    png_path = os.path.join(output_dir, "pressure_slice.png")
    plotter.screenshot(png_path)
    print("STEP 10: screenshot done", flush=True)

    plotter.close()
    print("STEP 11: plotter closed", flush=True)

    vtp_path = os.path.join(output_dir, "pressure_slice.vtp")
    sliced.save(vtp_path)
    print("STEP 12: vtp saved", flush=True)

    return png_path, vtp_path