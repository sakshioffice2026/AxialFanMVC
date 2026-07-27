using System.Diagnostics;
using System.Text;

namespace AxialFanMVC.Services
{
    public static class CfdVtkRenderer
    {
        public static string ExePath { get; set; } = @"D:\Tools\CfdRenderHost\CfdRenderHost.exe";

        public static (string PngPath, string VtpPath) RenderOffscreen(string casePath, string outputDir)
        {
            var psi = new ProcessStartInfo
            {
                FileName = ExePath,
                Arguments = $"\"{casePath}\" \"{outputDir}\"",
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

            if (process.ExitCode != 0)
            {
                throw new CfdRenderException(
                    $"CfdRenderHost.exe failed (exit {process.ExitCode}).", stderr.ToString());
            }

            string[] parts = stdout.ToString().Trim().Split('|');
            if (parts.Length != 2)
            {
                throw new CfdRenderException(
                    "CfdRenderHost.exe exited 0 but didn't print the expected \"pngPath|vtpPath\" line.",
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