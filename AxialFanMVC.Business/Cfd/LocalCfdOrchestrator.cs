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
            double rpm, double velocityMs, double radiusM, CancellationToken ct = default)
        {
            await PipelineLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                string casePath = Path.Combine(
                    Path.GetTempPath(), "AxialFanCFD_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(casePath);

                CopyTemplateDirectory(_templateRoot, casePath);
                InjectCalculatedValues(casePath, rpm, velocityMs, radiusM);

                string wslCasePath = ConvertWindowsPathToWsl(casePath);
                await RunWslCommandAsync("blockMesh", wslCasePath, ct).ConfigureAwait(false);
                await RunWslCommandAsync("snappyHexMesh -overwrite", wslCasePath, ct).ConfigureAwait(false);
                await RunWslCommandAsync("simpleFoam", wslCasePath, ct).ConfigureAwait(false);

                return casePath;
            }
            finally
            {
                PipelineLock.Release();
            }
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

            var tokens = new Dictionary<string, string>
            {
                ["__RPM_TARGET__"]     = rpm.ToString("F2"),
                ["__OMEGA_RAD_S__"]    = omegaRadS.ToString("F6"),
                ["__VELOCITY_INLET__"] = velocityMs.ToString("F4"),
                ["__DOMAIN_RADIUS__"]  = domainRadius.ToString("F4"),
                ["__DOMAIN_LENGTH__"]  = domainLength.ToString("F4"),
                ["__MESH_DIVISIONS__"] = "40 40 60",
                ["__MRF_ZONE_NAME__"]  = "rotorZone",
                ["__MAX_ITERATIONS__"] = "800",
            };

            ReplaceTokensInFile(Path.Combine(casePath, "system", "controlDict"), tokens);
            ReplaceTokensInFile(Path.Combine(casePath, "system", "blockMeshDict"), tokens);
            ReplaceTokensInFile(Path.Combine(casePath, "constant", "fvOptions"), tokens);
            ReplaceTokensInFile(Path.Combine(casePath, "0", "U"), tokens);
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
            process.ErrorDataReceived  += (s, e) => { if (e.Data != null) log.AppendLine(e.Data); };
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
