"""Scheduled Task entry point for CFD rendering.

render_result.py's VTK/PyVista stack goes through OpenGL/WGL, which needs
an interactive desktop session to create a rendering context. Launched
directly from IIS, a Windows Service, or a "run whether user is logged on
or not" Scheduled Task, that context creation fails and takes the whole
process down with a native 0xC0000005 access violation instead of a
catchable exception.

This script is the action behind a Scheduled Task configured to
"Run only when user is logged on" -- CfdVtkRenderer.cs no longer launches
python.exe/render_result.py directly. Instead it drops a *.request.json
file into the IPC directory and triggers this task with `schtasks /run`.
This script (running on the interactive desktop) picks up any pending
request(s), calls render(), and writes a matching *.response.json file
for CfdVtkRenderer.cs to pick up.

Usage:
    render_dispatch.py <ipc_directory>
"""
import contextlib
import glob
import io
import json
import os
import sys
import traceback

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from render_result import render


def process_one(request_path):
    response_path = request_path[: -len(".request.json")] + ".response.json"

    try:
        with open(request_path, "r", encoding="utf-8") as f:
            req = json.load(f)

        case_path = req["casePath"]
        output_dir = req["outputDir"]

        buf = io.StringIO()
        with contextlib.redirect_stdout(buf):
            png_path, vtp_path, streamlines_path = render(case_path, output_dir)

        response = {
            "success": True,
            "pngPath": png_path,
            "vtpPath": vtp_path,
            "streamlinesVtpPath": streamlines_path,
            "log": buf.getvalue(),
        }
    except Exception:
        response = {
            "success": False,
            "error": traceback.format_exc(),
            "log": "",
        }

    tmp_path = response_path + ".tmp"
    with open(tmp_path, "w", encoding="utf-8") as f:
        json.dump(response, f)
    os.replace(tmp_path, response_path)

    try:
        os.remove(request_path)
    except OSError:
        pass


def main():
    if len(sys.argv) < 2:
        print("Usage: render_dispatch.py <ipc_directory>", file=sys.stderr, flush=True)
        sys.exit(1)

    ipc_dir = sys.argv[1]
    pattern = os.path.join(ipc_dir, "*.request.json")

    pending = sorted(glob.glob(pattern), key=os.path.getmtime)
    for request_path in pending:
        process_one(request_path)


if __name__ == "__main__":
    main()
