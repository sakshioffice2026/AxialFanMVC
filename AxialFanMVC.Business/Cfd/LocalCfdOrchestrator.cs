using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AxialFanMVC.Business.Cfd
{
    public class CfdSolverException : Exception
    {
        public string SolverLog { get; }
        public CfdSolverException(string message, string solverLog) : base(message)
            => SolverLog = solverLog;
    }

    public sealed class LocalCfdOrchestrator
    {
        private static readonly SemaphoreSlim PipelineLock = new SemaphoreSlim(1, 1);
        private readonly string _templateRoot;
        private readonly IProgress<string> _progress;

        // templateRootPath comes from DI in Program.cs, resolved off
        // IWebHostEnvironment.ContentRootPath ? see registration snippet below.
        public LocalCfdOrchestrator(string templateRootPath, IProgress<string> progress = null)
        {
            _templateRoot = templateRootPath;
            _progress = progress;
        }

        public async Task<string> RunPipelineAsync(
            double rpm, double velocityMs, double radiusM,
            int bladeCount, double hubRatio, double bladeAngleDeg,
            string? bladeProfileCoordinateJson = null,
            CancellationToken ct = default)
        {
            await PipelineLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                string casePath = Path.Combine(
                    Path.GetTempPath(), "AxialFanCFD_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(casePath);

                CopyTemplateDirectory(_templateRoot, casePath);
                GenerateBladeGeometry(casePath, radiusM, hubRatio, bladeCount, bladeAngleDeg, bladeProfileCoordinateJson, rpm, velocityMs);
                InjectCalculatedValues(casePath, rpm, velocityMs, radiusM);

                await RunWslCommandAsync("blockMesh", casePath, ct).ConfigureAwait(false);
                await RunWslCommandAsync("surfaceFeatures", casePath, ct).ConfigureAwait(false);
                await RunWslCommandAsync("snappyHexMesh -overwrite", casePath, ct).ConfigureAwait(false);
                await RunWslCommandAsync("topoSet", casePath, ct).ConfigureAwait(false);
                await RunWslCommandAsync("foamRun -solver incompressibleFluid", casePath, ct).ConfigureAwait(false);

                // Carves the rotorZone cellZone (system/topoSetDict) that
                // constant/MRFProperties' MRF1 cellZone needs ? must run
                // after snappyHexMesh has actually shaped the mesh around the blade.
                await RunWslCommandAsync("topoSet", casePath, ct).ConfigureAwait(false);

                // v13: simpleFoam is a deprecated shim that just prints a
                // notice and execs this; call the real solver module directly.
                //
                // NOTE (fixed): previously used "bash -ic" (interactive shell),
                // which sources ~/.bashrc to pick up the OpenFOAM environment.
                // Under IIS's non-interactive worker process this reliably hung
                // at topoSet/foamRun with 0% CPU (no real terminal attached).
                // RunWslCommandAsync now uses non-interactive "bash -c" with the
                // OpenFOAM environment sourced explicitly inline instead, so it
                // no longer depends on ~/.bashrc staying uncorrupted or on an
                // interactive shell being viable under Process.Start.
                await RunWslCommandAsync("foamRun -solver incompressibleFluid", casePath, ct).ConfigureAwait(false);

                return casePath;
            }
            finally
            {
                PipelineLock.Release();
            }
        }

        /// <summary>
        /// Writes constant/triSurface/fan.stl from this design's own numbers
        /// (BladeStlGenerator) ? a first-pass solid, not a CAD export; see
        /// BladeStlGenerator's header comment for exactly what's approximated.
        /// Generator failures (bad HubRatio, corrupt stored profile, etc.) are
        /// wrapped as CfdSolverException so they surface the same way a
        /// solver failure would, instead of an unhandled exception with no
        /// case-path context.
        /// </summary>
        private static void GenerateBladeGeometry(
            string casePath, double tipRadiusM, double hubRatio, int bladeCount,
            double bladeAngleDeg, string? profileCoordinateJson, double rpm, double velocityMs)
        {
            string stlPath = Path.Combine(casePath, "constant", "triSurface", "fan.stl");
            int triCount;
            try
            {
                triCount = BladeStlGenerator.Generate(
                    stlPath, tipRadiusM, hubRatio, bladeCount, bladeAngleDeg, profileCoordinateJson,
                    rpm, velocityMs);
            }
            catch (Exception ex) when (ex is not CfdSolverException)
            {
                throw new CfdSolverException(
                    "Blade geometry generation failed before any OpenFOAM step ran.",
                    ex.ToString());
            }

            if (triCount <= 0)
                throw new CfdSolverException(
                    "Blade geometry generator produced zero triangles (bladeCount <= 0 after fallback?).",
                    stlPath);
        }

        private static void CopyTemplateDirectory(string sourceDir, string destDir)
        {
            foreach (var dirPath in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(dirPath.Replace(sourceDir, destDir));
            foreach (var filePath in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
                File.Copy(filePath, filePath.Replace(sourceDir, destDir), overwrite: true);
        }

        private void InjectCalculatedValues(string casePath, double rpm, double velocityMs, double radiusM)
        {
            double omegaRadS = rpm * Math.PI / 30.0;
            double domainRadius = radiusM * 3.0;
            double domainLength = radiusM * 4.0;

            // Blade disk sits ~1/3 of the domain length downstream of the
            // inlet (matches the comment in blockMeshDict). rotorZone (the
            // MRF cellZone carved by topoSetDict) spans one tip-radius of
            // axial thickness centred on that plane, with 10% radial
            // clearance beyond the tip so the whole swept envelope is inside
            // the rotating frame. Both assumptions must hold for the actual
            // blade STL once it's supplied ? see topoSetDict's header comment.
            double fanZ = domainLength / 3.0;
            double rotorHalfThickness = radiusM * 0.5;
            double rotorZoneRadius = radiusM * 1.1;
            double rotorZoneZMin = fanZ - rotorHalfThickness;
            double rotorZoneZMax = fanZ + rotorHalfThickness;

            // snappyHexMeshDict locationInMesh: off-axis (in case a hub/motor
            // pod extends along the centreline) and upstream of rotorZone, so
            // it stays in the fluid regardless of blade geometry details.
            // VERIFY once the real STL is in place.
            double locationX = radiusM * 0.7;
            double locationZ = domainLength * 0.1;
            string locationInMesh = FormattableString.Invariant($"{locationX:F4} 0 {locationZ:F4}");

            // Inlet turbulence quantities from intensity + length-scale
            // assumptions (no turbulence measurement available): I = 5%,
            // mixing length = 0.07 * inlet duct diameter (2x tip radius).
            const double turbulenceIntensity = 0.05;
            const double cMu = 0.09;
            double kInlet = Math.Max(1.5 * Math.Pow(turbulenceIntensity * velocityMs, 2), 1e-8);
            double mixingLength = 0.07 * (2.0 * radiusM);
            double omegaInlet = Math.Sqrt(kInlet) / (Math.Pow(cMu, 0.25) * mixingLength);

            var tokens = new Dictionary<string, string>
            {
                ["__RPM_TARGET__"] = rpm.ToString("F2"),
                ["__OMEGA_RAD_S__"] = omegaRadS.ToString("F6"),
                ["__VELOCITY_INLET__"] = velocityMs.ToString("F4"),
                ["__DOMAIN_RADIUS__"] = domainRadius.ToString("F4"),
                ["__DOMAIN_LENGTH__"] = domainLength.ToString("F4"),
                ["__MESH_DIVISIONS__"] = "24 24 36",
                ["__MRF_ZONE_NAME__"] = "rotorZone",
                ["__MAX_ITERATIONS__"] = "300",
                ["__ROTOR_ZONE_RADIUS__"] = rotorZoneRadius.ToString("F4"),
                ["__ROTOR_ZONE_Z_MIN__"] = rotorZoneZMin.ToString("F4"),
                ["__ROTOR_ZONE_Z_MAX__"] = rotorZoneZMax.ToString("F4"),
                ["__LOCATION_IN_MESH__"] = locationInMesh,
                ["__TI_K_INLET__"] = kInlet.ToString("F6"),
                ["__TI_OMEGA_INLET__"] = omegaInlet.ToString("F4"),
            };

            ReplaceTokensInFile(Path.Combine(casePath, "system", "controlDict"), tokens);
            ReplaceTokensInFile(Path.Combine(casePath, "system", "blockMeshDict"), tokens);
            ReplaceTokensInFile(Path.Combine(casePath, "system", "snappyHexMeshDict"), tokens);
            ReplaceTokensInFile(Path.Combine(casePath, "system", "topoSetDict"), tokens);
            // MRFProperties itself only carries __MRF_ZONE_NAME__ now; the
            // rotation numbers (__RPM_TARGET__) live in the #include'd
            // rotatingZoneProperties file (v13 simpleRushtonMRF pattern).
            ReplaceTokensInFile(Path.Combine(casePath, "constant", "MRFProperties"), tokens);
            ReplaceTokensInFile(Path.Combine(casePath, "constant", "rotatingZoneProperties"), tokens);
            ReplaceTokensInFile(Path.Combine(casePath, "0", "U"), tokens);
            ReplaceTokensInFile(Path.Combine(casePath, "0", "k"), tokens);
            ReplaceTokensInFile(Path.Combine(casePath, "0", "omega"), tokens);
        }

        private static void ReplaceTokensInFile(string filePath, Dictionary<string, string> tokens)
        {
            string text = File.ReadAllText(filePath);
            foreach (var kv in tokens) text = text.Replace(kv.Key, kv.Value);
            File.WriteAllText(filePath, text);
        }

        // Path inside WSL to the OpenFOAM install actually in use (confirmed
        // via `which blockMesh` -> /opt/openfoam13/platforms/.../bin/blockMesh
        // on this box, even though ~/.bashrc also has a stray `source
        // /opt/openfoam14/etc/bashrc` line further down that never wins).
        // Sourced explicitly below rather than relied on via ~/.bashrc,
        // because a non-interactive login shell (bash -lc) does NOT read
        // ~/.bashrc - only /etc/profile + ~/.bash_profile/~/.profile - which
        // is exactly why `blockMesh` came back "command not found" (exit 127)
        // even though the wsl.exe launch itself was working correctly.
        private const string OpenFoamBashrcPath = "/opt/openfoam13/etc/bashrc";

        private async Task RunWslCommandAsync(string command, string windowsCasePath, CancellationToken ct)
        {
            // windowsCasePath is a native Windows path (Path.GetTempPath()-based,
            // e.g. C:\Users\...\AppData\Local\Temp\AxialFanCFD_xxx) because this
            // app runs on Windows Server. OpenFOAM only exists inside WSL, so it
            // has no meaning as a WSL path until converted (WSL can't resolve
            // "C:\..." - it needs "/mnt/c/...").
            string wslCasePath = ConvertWindowsPathToWsl(windowsCasePath);

            var psi = new ProcessStartInfo
            {
                // This app is hosted on Windows Server; bash and OpenFOAM live
                // inside WSL, not on the Windows filesystem, so this must be
                // launched via wsl.exe rather than starting "/bin/bash" (or
                // "bash.exe") directly - Process.Start has no WSL-path
                // resolution of its own, hence "the system cannot find the
                // file specified" when FileName was set to "/bin/bash".
                FileName = "wsl.exe",
                // -e runs the given command line directly via the specified
                // interpreter (bash -lc "..."), rather than letting wsl.exe's
                // own default-shell translation get involved.
                // Non-interactive shell (-c, not -ic - interactive hung
                // indefinitely under IIS's worker process with no real
                // terminal attached, see note in RunPipelineAsync). Sources
                // OpenFOAM's bashrc explicitly first, since neither a
                // non-interactive login (-l) nor non-login shell reads
                // ~/.bashrc, which is where OpenFOAM's env normally lives.
                Arguments = $"-e bash -c \"source '{OpenFoamBashrcPath}' && cd '{wslCasePath}' && {command}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            var log = new StringBuilder();
            var tcs = new TaskCompletionSource<int>();
            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            process.OutputDataReceived += (s, e) => { if (e.Data != null) { log.AppendLine(e.Data); ReportProgressLine(command, e.Data); } };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) log.AppendLine(e.Data); };
            process.Exited += (s, e) => tcs.TrySetResult(process.ExitCode);

            using var ctReg = ct.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch { } });

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            int exitCode = await tcs.Task.ConfigureAwait(false);
            string fullLog = log.ToString();

            if (exitCode != 0 || fullLog.Contains("FOAM FATAL ERROR") || fullLog.Contains("blown up"))
                throw new CfdSolverException($"{command} failed or diverged (exit {exitCode}).", fullLog);
        }

        private static readonly Regex ResidualPattern =
            new Regex(@"Solving for (\w+), Initial residual = ([\d.eE+-]+)", RegexOptions.Compiled);

        private void ReportProgressLine(string stage, string line)
        {
            var match = ResidualPattern.Match(line);
            _progress?.Report(match.Success
                ? $"[{stage}] {match.Groups[1].Value} residual={match.Groups[2].Value}"
                : $"[{stage}] {line}");
        }

        // Converts a native Windows path (e.g. "C:\Users\...\Temp\Foo") to the
        // equivalent WSL mount path ("/mnt/c/Users/.../Temp/Foo") so it can be
        // used inside a `wsl.exe -e bash -lc "cd '...' && ..."` invocation.
        private static string ConvertWindowsPathToWsl(string windowsPath)
        {
            string drive = windowsPath.Substring(0, 1).ToLowerInvariant();
            string rest = windowsPath.Substring(2).Replace('\\', '/');
            return $"/mnt/{drive}{rest}";
        }
    }
}