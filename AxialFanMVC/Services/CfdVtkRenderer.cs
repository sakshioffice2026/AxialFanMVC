using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace AxialFanMVC.Services
{
    // Renders the pressure-slice PNG/.vtp for a completed CFD case.
    //
    // render_result.py's VTK/PyVista stack ultimately goes through
    // OpenGL/WGL to create its rendering context — that requires an
    // interactive desktop session. Launching python.exe directly from
    // IIS's app pool worker process (or a Windows Service, or a "run
    // whether user is logged on or not" Scheduled Task) runs it in a
    // non-interactive session, so context creation fails.
    //
    // Fix (Windows, IIS-hosted): drop a request file into
    // CfdRender:IpcDirectory and trigger a Scheduled Task
    // (CfdRender:TaskName) configured to "Run only when user is logged
    // on", so rendering happens on an interactive desktop.
    //
    // Fix (Windows, local dev via `dotnet run`/Visual Studio): the
    // ASP.NET Core process itself is already running inside the
    // developer's own interactive desktop session — there is no
    // non-interactive boundary to work around, so routing through the
    // Scheduled Task/IPC machinery just adds a fragile dependency on
    // the Task Scheduler engine for no benefit. When
    // CfdRender:UseDirectRenderOnWindows is true, this calls the
    // render script directly and synchronously, the same way the
    // Linux path does.
    //
    // Fix (Linux): Xvfb provides a standard virtual framebuffer for
    // headless OpenGL, so we just run render_result.py directly and
    // synchronously.
    //
    // Public API is unchanged — RenderOffscreen(casePath, outputDir) —
    // so CfdBackgroundService and anything else calling this needs no
    // changes.
    //
    // Configure via appsettings.json -> CfdRender:* (wired in Program.cs).
    public static class CfdVtkRenderer
    {
        // Windows IPC path: not used directly (the Scheduled Task's action
        // already has its own fixed python.exe + script path), kept only
        // as a reference for whoever sets that task up.
        // Linux + Windows direct-render path: used to invoke
        // render_result.py directly.
        public static string PythonExe { get; set; } = "python3";
        public static string ScriptPath { get; set; } = "";
        public static string TaskName { get; set; } = "AxialFanCfdRender";

        public static string IpcDirectory { get; set; } = @"D:\Office\CfdIpc";

        public static int TimeoutSeconds { get; set; } = 300;

        // When true on Windows, skip the Scheduled Task/IPC handoff entirely
        // and call the render script directly and synchronously — valid
        // only when the ASP.NET Core process itself has an interactive
        // desktop session (local `dotnet run` / Visual Studio debugging),
        // never for an IIS-hosted deployment.
        public static bool UseDirectRenderOnWindows { get; set; } = false;

        public static (string PngPath, string VtpPath, string? StreamlinesVtpPath) RenderOffscreen(string casePath, string outputDir)
        {
            bool isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);

            if (!isWindows)
                return RenderOffscreenDirect(casePath, outputDir);

            if (UseDirectRenderOnWindows)
                return RenderOffscreenDirect(casePath, outputDir);

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
            // still debuggable afterward.
            try
            {
                Directory.CreateDirectory(outputDir);
                File.WriteAllText(Path.Combine(outputDir, "render.log"), log ?? string.Empty);
            }
            catch { /* diagnostics best-effort — never let logging failure mask the real result */ }

            return (pngPath, vtpPath, streamlinesVtpPath);
        }

        // Shared by Linux (always) and Windows-direct-render (when
        // UseDirectRenderOnWindows is true) — runs PythonExe/ScriptPath
        // against casePath/outputDir directly and synchronously, no
        // Scheduled Task or IPC files involved. On Linux this wraps
        // PythonExe with xvfb-run; on Windows-direct it runs the venv
        // python straight, since the calling process already owns an
        // interactive desktop context.
        private static (string PngPath, string VtpPath, string? StreamlinesVtpPath) RenderOffscreenDirect(string casePath, string outputDir)
        {
            Directory.CreateDirectory(outputDir);

            bool isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);

            var psi = isWindows
                ? new ProcessStartInfo
                {
                    FileName = PythonExe,
                    Arguments = $"\"{ScriptPath}\" \"{casePath}\" \"{outputDir}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }
                : new ProcessStartInfo
                {
                    FileName = "xvfb-run",
                    Arguments = $"-a \"{PythonExe}\" \"{ScriptPath}\" \"{casePath}\" \"{outputDir}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };

            if (isWindows)
            {
                // Must match run_render.bat exactly — VTK_DEFAULT_OPENGL_WINDOW
                // forces VTK's Win32 OpenGL backend instead of letting it
                // auto-detect, and a writable TEMP/TMP avoids permission
                // issues on whatever profile the process is running under.
                // Omitting these is what caused the 0xC0000005 access
                // violation when this path was first added without them.
                psi.EnvironmentVariables["TEMP"] = @"C:\Windows\Temp";
                psi.EnvironmentVariables["TMP"] = @"C:\Windows\Temp";
                psi.EnvironmentVariables["VTK_DEFAULT_OPENGL_WINDOW"] = "vtkWin32OpenGLRenderWindow";
            }

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            using var process = new Process { StartInfo = psi };

            process.OutputDataReceived += (s, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            bool exited = process.WaitForExit(TimeoutSeconds * 1000);
            if (!exited)
            {
                try { process.Kill(true); } catch { /* best-effort */ }
                throw new CfdRenderException(
                    $"render_result.py did not complete within {TimeoutSeconds}s.",
                    stdout.ToString() + stderr.ToString());
            }

            string fullLog = stdout.ToString() + stderr.ToString();

            try
            {
                File.WriteAllText(Path.Combine(outputDir, "render.log"), fullLog);
            }
            catch { /* diagnostics best-effort — never let logging failure mask the real result */ }

            if (process.ExitCode != 0)
            {
                throw new CfdRenderException(
                    $"render_result.py failed (exit {process.ExitCode}).", fullLog);
            }

            // render_result.py's __main__ block prints "png|vtp|streamlines"
            // as its final stdout line — parse that instead of re-deriving
            // paths, so the C# side never drifts out of sync with what the
            // script actually wrote.
            string? resultLine = stdout.ToString()
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault(l => l.Contains('|'));

            if (resultLine == null)
                throw new CfdRenderException(
                    "render_result.py exited 0 but produced no parseable output line.", fullLog);

            var parts = resultLine.Trim().Split('|');
            string pngPath = parts[0];
            string vtpPath = parts.Length > 1 ? parts[1] : "";
            string? streamlinesVtpPath = parts.Length > 2 && parts[2].Length > 0 ? parts[2] : null;

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