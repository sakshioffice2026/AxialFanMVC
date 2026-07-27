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
        // IWebHostEnvironment.ContentRootPath — see registration snippet below.
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
                GenerateBladeGeometry(casePath, radiusM, hubRatio, bladeCount, bladeAngleDeg, bladeProfileCoordinateJson);
                InjectCalculatedValues(casePath, rpm, velocityMs, radiusM);

                string wslCasePath = ConvertWindowsPathToWsl(casePath);
                await RunWslCommandAsync("blockMesh", wslCasePath, ct).ConfigureAwait(false);
                await RunWslCommandAsync("surfaceFeatures", wslCasePath, ct).ConfigureAwait(false);
                await RunWslCommandAsync("snappyHexMesh -overwrite", wslCasePath, ct).ConfigureAwait(false);

                // Carves the rotorZone cellZone (system/topoSetDict) that
                // constant/fvOptions' MRFSource needs — must run after
                // snappyHexMesh has actually shaped the mesh around the blade.
                await RunWslCommandAsync("topoSet", wslCasePath, ct).ConfigureAwait(false);

                await RunWslCommandAsync("simpleFoam", wslCasePath, ct).ConfigureAwait(false);

                return casePath;
            }
            finally
            {
                PipelineLock.Release();
            }
        }

        /// <summary>
        /// Writes constant/triSurface/fan.stl from this design's own numbers
        /// (BladeStlGenerator) — a first-pass solid, not a CAD export; see
        /// BladeStlGenerator's header comment for exactly what's approximated.
        /// Generator failures (bad HubRatio, corrupt stored profile, etc.) are
        /// wrapped as CfdSolverException so they surface the same way a
        /// solver failure would, instead of an unhandled exception with no
        /// case-path context.
        /// </summary>
        private static void GenerateBladeGeometry(
            string casePath, double tipRadiusM, double hubRatio, int bladeCount,
            double bladeAngleDeg, string? profileCoordinateJson)
        {
            string stlPath = Path.Combine(casePath, "constant", "triSurface", "fan.stl");
            int triCount;
            try
            {
                triCount = BladeStlGenerator.Generate(
                    stlPath, tipRadiusM, hubRatio, bladeCount, bladeAngleDeg, profileCoordinateJson);
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
            // blade STL once it's supplied — see topoSetDict's header comment.
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
                ["__MESH_DIVISIONS__"] = "40 40 60",
                ["__MRF_ZONE_NAME__"] = "rotorZone",
                ["__MAX_ITERATIONS__"] = "800",
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
            ReplaceTokensInFile(Path.Combine(casePath, "constant", "MRFProperties"), tokens);
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

        private async Task RunWslCommandAsync(string command, string wslCasePath, CancellationToken ct)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "wsl.exe",
                Arguments = $"-e bash -ic \"cd '{wslCasePath}' && {command}\"",
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

        private static string ConvertWindowsPathToWsl(string windowsPath)
        {
            string drive = windowsPath.Substring(0, 1).ToLowerInvariant();
            string rest = windowsPath.Substring(2).Replace('\\', '/');
            return $"/mnt/{drive}{rest}";
        }
    }
}