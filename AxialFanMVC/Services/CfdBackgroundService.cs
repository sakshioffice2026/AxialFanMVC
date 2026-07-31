using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using AxialFanMVC.Business.Cfd;
using AxialFanMVC.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AxialFanMVC.Services
{
    public class CfdBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly CfdJobChannel _channel;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<CfdBackgroundService> _logger;

        public CfdBackgroundService(
            IServiceScopeFactory scopeFactory,
            CfdJobChannel channel,
            IWebHostEnvironment env,
            ILogger<CfdBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _channel = channel;
            _env = env;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var sweepTimer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            _ = SweepLoopAsync(sweepTimer, stoppingToken);

            await foreach (var jobId in _channel.Reader.ReadAllAsync(stoppingToken))
                await ProcessJobAsync(jobId, stoppingToken);
        }

        private async Task SweepLoopAsync(PeriodicTimer timer, CancellationToken stoppingToken)
        {
            do
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AxialFanDbContext>();
                    var stuck = await db.cfd_jobs.Where(j => j.Status == "Queued")
                        .Select(j => j.Id).ToListAsync(stoppingToken);
                    foreach (var id in stuck) await ProcessJobAsync(id, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "CFD job sweep failed.");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }

        private async Task ProcessJobAsync(int jobId, CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AxialFanDbContext>();

            var job = await db.cfd_jobs.FirstOrDefaultAsync(j => j.Id == jobId, stoppingToken);
            if (job is null || job.Status != "Queued") return;

            job.Status = "Running";
            job.StartedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(stoppingToken);

            try
            {
                var result = await db.design_results
                .Include(r => r.DesignInput)
                    .ThenInclude(d => d.BladeProfile)
                .FirstOrDefaultAsync(r => r.Id == job.ResultId, stoppingToken)
                ?? throw new InvalidOperationException("DesignResult not found for this CFD job.");

                string templateRoot = Path.Combine(_env.ContentRootPath, "..", "AxialFanMVC.Business", "Cfd", "CfdTemplates");
                var orchestrator = new LocalCfdOrchestrator(templateRoot);

                var di = result.DesignInput;

                // Same annulus-area calc ResultsController.BuildBaselineComparisonAsync
                // already uses for the baseline comparison card — kept identical here
                // so the CFD inlet velocity matches what the rest of the app assumes.
                double tipRadiusM = di.TipDiameterMm / 2000.0;
                double hubRadiusM = tipRadiusM * di.HubRatio;
                double annulusAreaM2 = Math.PI * (tipRadiusM * tipRadiusM - hubRadiusM * hubRadiusM);
                double axialVelocityMs = annulusAreaM2 > 0 ? di.FlowRateM3s / annulusAreaM2 : 0;
                double rpm = di.SpeedRpm;

                if (axialVelocityMs <= 0)
                    throw new InvalidOperationException("Could not derive a valid inlet velocity from this design's geometry.");

                string casePath = await orchestrator.RunPipelineAsync(
                              rpm,
                              axialVelocityMs,
                              tipRadiusM,
                              di.BladeCount,
                              di.HubRatio,
                              di.BladeAngleDeg,
                              di.BladeProfile?.CoordinateData,
                              stoppingToken);

                string outputDir = Path.Combine(_env.WebRootPath, "cfd-results", job.ResultId.ToString());
                var (pngPath, vtpPath, streamlinesVtpPath) = CfdVtkRenderer.RenderOffscreen(casePath, outputDir);

                job.PngPath = Path.Combine("cfd-results", job.ResultId.ToString(), Path.GetFileName(pngPath)).Replace('\\', '/');
                job.VtpPath = Path.Combine("cfd-results", job.ResultId.ToString(), Path.GetFileName(vtpPath)).Replace('\\', '/');
                // Null when this run's seed produced no streamlines - expected
                // for some geometries, not a failure (see render_result.py).
                job.StreamlinesVtpPath = streamlinesVtpPath != null
                    ? Path.Combine("cfd-results", job.ResultId.ToString(), Path.GetFileName(streamlinesVtpPath)).Replace('\\', '/')
                    : null;
                job.Status = "Completed";

                // TEMPORARILY DISABLED for debugging the blank-render issue —
                // this deletes the actual OpenFOAM case (mesh + solved time
                // directories) the instant a render succeeds, so there was
                // nothing left afterward to check whether "p" was ever
                // solved/written. Re-enable once renders are confirmed good:
                //     Directory.Delete(casePath, recursive: true);
                _logger.LogInformation("CFD case for job {JobId} kept at {CasePath} for inspection.", jobId, casePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CFD job {JobId} failed.", jobId);
                job.Status = "Failed";

                // CfdSolverException carries the full OpenFOAM stdout/stderr in
                // SolverLog — that's where the *actual* reason for a failure
                // like "blockMesh failed or diverged" lives (bad mesh, missing
                // binary, etc.). ex.Message alone is just the generic wrapper
                // text and isn't enough to debug from. Keep the tail of the
                // log (where solver errors are reported) rather than the
                // start, and cap the length so it fits comfortably in the
                // error_message column.
                if (ex is CfdSolverException solverEx && !string.IsNullOrEmpty(solverEx.SolverLog))
                {
                    const int maxLen = 4000;
                    string tail = solverEx.SolverLog.Length > maxLen
                        ? solverEx.SolverLog[^maxLen..]
                        : solverEx.SolverLog;
                    job.ErrorMessage = $"{ex.Message}\n\n--- solver log (tail) ---\n{tail}";
                }
                // CfdRenderException carries the render_dispatch.py/pyvista
                // log (or, for a schtasks/timeout failure, the schtasks
                // stderr) in RendererLog — previously this branch didn't
                // exist, so a render failure only ever surfaced the
                // generic wrapper message in job.ErrorMessage and diagnosing
                // it meant manually digging through Task Scheduler history,
                // Event Viewer, and Python logs on the box instead of the
                // job record itself.
                else if (ex is CfdRenderException renderEx && !string.IsNullOrEmpty(renderEx.RendererLog))
                {
                    const int maxLen = 4000;
                    string tail = renderEx.RendererLog.Length > maxLen
                        ? renderEx.RendererLog[^maxLen..]
                        : renderEx.RendererLog;
                    job.ErrorMessage = $"{ex.Message}\n\n--- renderer log (tail) ---\n{tail}";
                }
                else
                {
                    job.ErrorMessage = ex.Message;
                }
            }
            finally
            {
                job.CompletedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(stoppingToken);
            }
        }
    }
}