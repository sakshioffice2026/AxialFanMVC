using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace AxialFanMVC.Services
{
    // Renders the pressure-slice PNG/.vtp for a completed CFD case.
    //
    // render_result.py's VTK/PyVista stack ultimately goes through
    // OpenGL/WGL to create its rendering context (even the Mesa
    // software-rendering fallback, libgallium_wgl.dll, is still WGL
    // underneath) — that requires an interactive desktop session.
    // Launching python.exe directly from IIS's app pool worker process
    // (or from a Windows Service, or a "run whether user is logged on or
    // not" Scheduled Task) runs it in a non-interactive session, so
    // context creation fails and takes the whole process down with a
    // native 0xC0000005 access violation instead of a catchable
    // exception — that crash reproduced identically outside IIS too,
    // which ruled out App Pool identity/permissions/env vars as the
    // cause.
    //
    // Fix: this no longer shells out to python.exe itself. Instead it
    // drops a request file into CfdRender:IpcDirectory and triggers a
    // Scheduled Task (CfdRender:TaskName) configured to "Run only when
    // user is logged on", so the actual rendering happens on an
    // interactive desktop. That task's action is render_dispatch.py
    // (Cfd/Render/render_dispatch.py), which calls render_result.py's
    // render() and writes a matching response file back to the same
    // IPC directory for this class to pick up.
    //
    // Public API is unchanged — RenderOffscreen(casePath, outputDir) —
    // so CfdBackgroundService and anything else calling this needs no
    // changes.
    //
    // Configure via appsettings.json -> CfdRender:* (wired in Program.cs).
    public static class CfdVtkRenderer
    {
        // No longer used directly by this class (the Scheduled Task's
        // action already has its own fixed python.exe + script path),
        // kept only as a reference for whoever sets that task up.
        public static string PythonExe { get; set; } = "python3";
        public static string ScriptPath { get; set; } = "";
        public static string TaskName { get; set; } = "AxialFanCfdRender";

        public static string IpcDirectory { get; set; } = @"D:\Office\CfdIpc";

        public static int TimeoutSeconds { get; set; } = 300;

        public static (string PngPath, string VtpPath, string? StreamlinesVtpPath) RenderOffscreen(string casePath, string outputDir)
        {
            Directory.CreateDirectory(IpcDirectory);

            string requestId = Guid.NewGuid().ToString("N");
            string requestPath = Path.Combine(IpcDirectory, $"{requestId}.request.json");
            string responsePath = Path.Combine(IpcDirectory, $"{requestId}.response.json");

            File.WriteAllText(requestPath, JsonSerializer.Serialize(new { casePath, outputDir }));

            TriggerScheduledTask();

            var (pngPath, vtpPath, streamlinesVtpPath, log) = WaitForResponse(requestPath, responsePath);

            // render_dispatch.py can report a non-fatal issue in the log
            // while still succeeding (e.g. a field missing on the slice).
            // Persist the log next to the output unconditionally, not
            // just on failure, so a "succeeded but looks wrong" run is
            // still debuggable afterward — same reasoning as before this
            // rewrite, just sourced from the IPC response now instead of
            // a direct stderr capture.
            try
            {
                Directory.CreateDirectory(outputDir);
                File.WriteAllText(Path.Combine(outputDir, "render.log"), log ?? string.Empty);
            }
            catch { /* diagnostics best-effort — never let logging failure mask the real result */ }

            return (pngPath, vtpPath, streamlinesVtpPath);
        }

        private static void TriggerScheduledTask()
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks",
                Arguments = $"/run /tn \"{TaskName}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var process = new Process { StartInfo = psi };

            var stderr = new StringBuilder();
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

            process.Start();
            process.BeginErrorReadLine();

            // schtasks /run just enqueues the task and returns almost
            // immediately — this isn't waiting for the render itself, so
            // a short, fixed timeout here is enough; the real wait
            // happens in WaitForResponse via CfdRender:TimeoutSeconds.
            if (!process.WaitForExit(15000))
            {
                process.Kill(true);
                throw new CfdRenderException(
                    "schtasks /run did not return within 15s.", stderr.ToString());
            }

            if (process.ExitCode != 0)
            {
                throw new CfdRenderException(
                    $"schtasks /run failed (exit {process.ExitCode}) for task \"{TaskName}\" — " +
                    "confirm the task exists and is enabled.",
                    stderr.ToString());
            }
        }

        private static (string PngPath, string VtpPath, string? StreamlinesVtpPath, string Log) WaitForResponse(
            string requestPath, string responsePath)
        {
            var deadline = DateTime.UtcNow.AddSeconds(TimeoutSeconds);

            while (DateTime.UtcNow < deadline)
            {
                if (File.Exists(responsePath))
                {
                    // render_dispatch.py writes to a .tmp file and
                    // renames it into place, so existence implies a
                    // complete write — still guard against a transient
                    // sharing-violation race on the rename itself.
                    string json;
                    try
                    {
                        json = File.ReadAllText(responsePath);
                    }
                    catch (IOException)
                    {
                        Thread.Sleep(250);
                        continue;
                    }

                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    string log = root.TryGetProperty("log", out var logProp)
                        ? logProp.GetString() ?? string.Empty
                        : string.Empty;
                    bool success = root.TryGetProperty("success", out var successProp)
                        && successProp.GetBoolean();

                    TryCleanup(requestPath, responsePath);

                    if (!success)
                    {
                        string error = root.TryGetProperty("error", out var errProp)
                            ? errProp.GetString() ?? string.Empty
                            : "render_dispatch.py reported failure with no error detail.";
                        throw new CfdRenderException(
                            "render_dispatch.py failed.", $"{error}\n\n--- log ---\n{log}");
                    }

                    string pngPath = root.GetProperty("pngPath").GetString()!;
                    string vtpPath = root.GetProperty("vtpPath").GetString()!;
                    // Nullable: not every case produces streamlines (see
                    // render_result.py's STEP 8 warning) - that's expected,
                    // not a failure, so a missing/null property here just
                    // means no streamlines file for this run.
                    string? streamlinesVtpPath = root.TryGetProperty("streamlinesVtpPath", out var slProp)
                        && slProp.ValueKind != JsonValueKind.Null
                        ? slProp.GetString()
                        : null;
                    return (pngPath, vtpPath, streamlinesVtpPath, log);
                }

                Thread.Sleep(1000);
            }

            TryCleanup(requestPath, responsePath);
            throw new CfdRenderException(
                $"Timed out after {TimeoutSeconds}s waiting for the CFD render Scheduled Task " +
                $"(\"{TaskName}\") to respond. Check that a user is logged into the server's " +
                "desktop session and that the task is enabled.",
                string.Empty);
        }

        private static void TryCleanup(string requestPath, string responsePath)
        {
            try { if (File.Exists(requestPath)) File.Delete(requestPath); } catch { /* best-effort */ }
            try { if (File.Exists(responsePath)) File.Delete(responsePath); } catch { /* best-effort */ }
        }
    }

    public class CfdRenderException : System.Exception
    {
        public string RendererLog { get; }
        public CfdRenderException(string message, string rendererLog) : base(message)
            => RendererLog = rendererLog;
    }
}