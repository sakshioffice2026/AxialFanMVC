using System.Diagnostics;
using System.Text;

namespace AxialFanMVC.Services
{
    // Renders the pressure-slice PNG/.vtp by shelling out to a Python
    // script (Cfd/Render/render_result.py) using pyvista, the same way
    // LocalCfdOrchestrator shells out to wsl.exe for OpenFOAM itself.
    //
    // Replaces the old ActiViz.NET/Kitware.VTK path and the CfdRenderHost
    // net48 project — that library is a mixed-mode assembly .NET 8's
    // CoreCLR cannot load at all. pyvista's VTK bindings are normal
    // cross-platform pip wheels, so this sidesteps that incompatibility
    // entirely instead of working around it with a separate process
    // targeting a different .NET runtime.
    //
    // Configure via appsettings.json -> CfdRender:PythonExe / ScriptPath
    // (wired in Program.cs).
    public static class CfdVtkRenderer
    {
        public static string PythonExe { get; set; } = "python";
       
         public static string ScriptPath { get; set; } =
             @"D:\Office\AxialFanMVC.Business\Cfd\Render\render_result.py";

        public static (string PngPath, string VtpPath) RenderOffscreen(string casePath, string outputDir)
        {
            var psi = new ProcessStartInfo
            {
                FileName = PythonExe,
                Arguments = $"\"{ScriptPath}\" \"{casePath}\" \"{outputDir}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var process = new Process { StartInfo = psi };

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            process.OutputDataReceived += (s, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            // render_result.py can print non-fatal warnings to stderr (e.g.
            // a field missing on the slice) while still exiting 0 with a
            // blank/partial image. Persist stderr next to the output
            // unconditionally, not just on failure, so a "succeeded but
            // looks wrong" run is still debuggable afterward.
            try
            {
                Directory.CreateDirectory(outputDir);
                File.WriteAllText(Path.Combine(outputDir, "render.log"), stderr.ToString());
            }
            catch { /* diagnostics best-effort — never let logging failure mask the real result */ }

            if (process.ExitCode != 0)
            {
                throw new CfdRenderException(
                    $"render_result.py failed (exit {process.ExitCode}).", stderr.ToString());
            }

            string[] parts = stdout.ToString().Trim().Split('|');
            if (parts.Length != 2)
            {
                throw new CfdRenderException(
                    "render_result.py exited 0 but didn't print the expected \"pngPath|vtpPath\" line.",
                    stdout.ToString());
            }

            return (parts[0], parts[1]);
        }
    }

    public class CfdRenderException : System.Exception
    {
        public string RendererLog { get; }
        public CfdRenderException(string message, string rendererLog) : base(message)
            => RendererLog = rendererLog;
    }
}