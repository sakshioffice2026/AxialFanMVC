using System;
using System.IO;
using System.Security.Claims;
using AxialFanMVC.Business.Cfd;
using AxialFanMVC.Database;
using AxialFanMVC.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AxialFanMVC.Controllers
{
    [Authorize]
    public class CfdController : Controller
    {
        private readonly AxialFanDbContext _db;
        private readonly ICfdJobSignal _jobSignal;
        private readonly IWebHostEnvironment _env;

        private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        public CfdController(AxialFanDbContext db, ICfdJobSignal jobSignal, IWebHostEnvironment env)
        {
            _db = db;
            _jobSignal = jobSignal;
            _env = env;
        }

        // POST /Cfd/Start?resultId=7
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Start(int resultId)
        {
            var result = await _db.design_results
                .Include(r => r.DesignInput)
                .FirstOrDefaultAsync(r => r.Id == resultId && r.DesignInput.Project.UserId == CurrentUserId);

            if (result is null) return NotFound();

            var job = new CfdJob
            {
                ResultId = resultId,
                UserId = CurrentUserId,
                Status = "Queued"
            };

            _db.cfd_jobs.Add(job);
            await _db.SaveChangesAsync();

            _jobSignal.NotifyJobQueued(job.Id);

            return AcceptedAtAction(nameof(Status), new { jobId = job.Id }, new { jobId = job.Id });
        }

        // GET /Cfd/Status/{jobId} — polled every few seconds by the Results page
        [HttpGet]
        public async Task<IActionResult> Status(int jobId)
        {
            var job = await _db.cfd_jobs.FirstOrDefaultAsync(j => j.Id == jobId && j.UserId == CurrentUserId);
            if (job is null) return NotFound();

            if (job.Status == "Completed")
            {
                PersistCfdResult(job);
            }

            return Json(new
            {
                jobId = job.Id,
                status = job.Status,
                errorMessage = job.ErrorMessage,
                pngUrl = job.PngPath != null ? Url.Content("~/" + job.PngPath) : null,
                vtpUrl = job.VtpPath != null ? Url.Content("~/" + job.VtpPath) : null
            });
        }

        // GET /Cfd/BladeStl/7 — real STL blade geometry for the 3D viewer on
        // the Results page. Generated once from this design's own calculated
        // numbers (tip radius, hub ratio, blade count, RPM, axial velocity,
        // and its BladeProfile airfoil if one is set — see BladeStlGenerator)
        // and cached under wwwroot/blade-models/{resultId}/fan.stl so a
        // repeat view/refresh doesn't regenerate it. Ownership is checked the
        // same way as Start()/Status() above: DesignResult -> DesignInput ->
        // Project.UserId == CurrentUserId.
        [HttpGet]
        public async Task<IActionResult> BladeStl(int resultId)
        {
            var result = await _db.design_results
                .Include(r => r.DesignInput).ThenInclude(di => di.Project)
                .Include(r => r.DesignInput).ThenInclude(di => di.BladeProfile)
                .FirstOrDefaultAsync(r => r.Id == resultId && r.DesignInput.Project.UserId == CurrentUserId);

            if (result is null) return NotFound();

            var di = result.DesignInput;

            var stlDir = Path.Combine(_env.WebRootPath, "blade-models", resultId.ToString());
            var stlPath = Path.Combine(stlDir, "fan.stl");

            if (!System.IO.File.Exists(stlPath))
            {
                // Same annulus-area -> axial-velocity calculation already used
                // by ResultsController.BuildBaselineComparisonAsync, kept
                // consistent here so the blade twist matches this design's
                // actual operating point rather than an assumed default.
                double tipRadiusM = di.TipDiameterMm / 2000.0;
                double hubRadiusM = tipRadiusM * di.HubRatio;
                double annulusAreaM2 = Math.PI * (tipRadiusM * tipRadiusM - hubRadiusM * hubRadiusM);
                double axialVelocityMs = annulusAreaM2 > 0 ? di.FlowRateM3s / annulusAreaM2 : 0.0;

                try
                {
                    BladeStlGenerator.Generate(
                        stlFilePath: stlPath,
                        tipRadiusM: tipRadiusM,
                        hubRatio: di.HubRatio,
                        bladeCount: di.BladeCount,
                        bladeAngleDeg: di.BladeAngleDeg,
                        profileCoordinateJson: di.BladeProfile?.CoordinateData,
                        rpm: di.SpeedRpm,
                        axialVelocityMs: axialVelocityMs);
                }
                catch (Exception ex)
                {
                    return Problem(
                        title: "Unable to generate blade geometry",
                        detail: ex.Message,
                        statusCode: 500);
                }
            }

            return PhysicalFile(stlPath, "model/stl", "fan.stl");
        }

        // Copies this job's rendered output into a stable, ResultId-keyed
        // location under wwwroot/cfd/{ResultId}/ so ResultsController can
        // find it again on a later page load/refresh (jobs themselves are
        // per-run and not otherwise addressable from the Results page).
        // Best-effort: a copy failure here shouldn't break the Status
        // response the polling JS is waiting on, so it's swallowed and the
        // user just won't get the persistent card until the next
        // successful run.
        private void PersistCfdResult(CfdJob job)
        {
            try
            {
                var destDir = Path.Combine(_env.WebRootPath, "cfd", job.ResultId.ToString());
                Directory.CreateDirectory(destDir);

                CopyIfNeeded(job.PngPath, Path.Combine(destDir, "pressure_slice.png"));
                CopyIfNeeded(job.VtpPath, Path.Combine(destDir, "pressure_slice.vtp"));
            }
            catch
            {
                // Non-fatal — see method comment above.
            }
        }

        private void CopyIfNeeded(string? relativeSourcePath, string destPath)
        {
            if (relativeSourcePath is null) return;

            var srcPath = Path.Combine(_env.WebRootPath, relativeSourcePath.Replace('/', Path.DirectorySeparatorChar));
            if (!System.IO.File.Exists(srcPath)) return;

            if (string.Equals(Path.GetFullPath(srcPath), Path.GetFullPath(destPath), StringComparison.OrdinalIgnoreCase))
                return; // already the canonical file

            System.IO.File.Copy(srcPath, destPath, overwrite: true);
        }
    }
}