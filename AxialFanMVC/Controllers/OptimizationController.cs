using System.Security.Claims;
using System.Text.Json;
using AxialFanMVC.Database;
using AxialFanMVC.Models;
using AxialFanMVC.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AxialFanMVC.Controllers
{
    [Authorize]
    public class OptimizationController : Controller
    {
        private readonly AxialFanDbContext _db;
        private readonly IOptimizationJobSignal _jobSignal;

        private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        public OptimizationController(AxialFanDbContext db, IOptimizationJobSignal jobSignal)
        {
            _db = db;
            _jobSignal = jobSignal;
        }

        // POST /Optimization/Start?projectId=5
        // Two callers, two data sources:
        //  - Results page (finished design): no body sent -> falls back to
        //    reading the project's most recent saved DesignInput.
        //  - Design wizard (Review & Confirm, step 8): nothing is saved to
        //    DesignInput yet at this point — the whole wizard lives in
        //    TempData until the final "Calculate & Save" step — so the
        //    wizard sends its current in-memory field values as the body
        //    instead of us reading a stale/nonexistent DB row.
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Start(int projectId, [FromBody] OptimizeRequestDto? liveInput = null)
        {
            OptimizeRequestDto request;

            if (liveInput != null && liveInput.FlowRateM3s > 0 && liveInput.TotalPressurePa > 0)
            {
                request = liveInput;
            }
            else
            {
                var input = await _db.design_inputs
                    .Where(d => d.ProjectId == projectId && d.Project.UserId == CurrentUserId)
                    .OrderByDescending(d => d.CreatedAt)
                    .FirstOrDefaultAsync();

                if (input is null)
                    return NotFound("No design found for this project to optimize from.");

                if (input.FlowRateM3s <= 0 || input.TotalPressurePa <= 0)
                    return BadRequest("The design needs a valid flow rate and pressure duty point before optimizing.");

                request = new OptimizeRequestDto
                {
                    FlowRateM3s = input.FlowRateM3s,
                    TotalPressurePa = input.TotalPressurePa,
                    TemperatureCelsius = input.TemperatureCelsius,
                    MinEfficiencyPct = input.MinEfficiencyPct,
                    MaxNoiseDbA = input.MaxNoiseDbA,
                    MaxMotorPowerKw = input.MaxMotorPowerKw,
                    MaxTipDiameterMm = input.MaxTipDiameterMm
                };
            }

            var job = new OptimizationJob
            {
                ProjectId = projectId,
                UserId = CurrentUserId,
                Status = "Queued",
                RequestJson = JsonSerializer.Serialize(request)
            };

            _db.optimization_jobs.Add(job);
            await _db.SaveChangesAsync();

            _jobSignal.NotifyJobQueued(job.Id);

            // 202 Accepted, not 200 — the job isn't done, the frontend must poll.
            return AcceptedAtAction(nameof(Status), new { jobId = job.Id }, new { jobId = job.Id });
        }

        // GET /Optimization/Status/{jobId} — polled by the frontend every
        // few seconds until Status is Completed or Failed.
        [HttpGet]
        public async Task<IActionResult> Status(int jobId)
        {
            var job = await _db.optimization_jobs
                .FirstOrDefaultAsync(j => j.Id == jobId && j.UserId == CurrentUserId);

            if (job is null) return NotFound();

            return Json(new
            {
                jobId = job.Id,
                status = job.Status,
                errorMessage = job.ErrorMessage,
                candidates = job.ResultJson is null
                    ? null
                    : JsonSerializer.Deserialize<List<OptimizeCandidateDto>>(job.ResultJson)
            });
        }
    }
}