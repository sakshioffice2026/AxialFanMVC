using System.Security.Claims;
using AxialFanMVC.Database;
using AxialFanMVC.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AxialFanMVC.Controllers
{
    [Authorize]
    public class CfdController : Controller
    {
        private readonly AxialFanDbContext _db;
        private readonly ICfdJobSignal _jobSignal;

        private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        public CfdController(AxialFanDbContext db, ICfdJobSignal jobSignal)
        {
            _db = db;
            _jobSignal = jobSignal;
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

            return Json(new
            {
                jobId = job.Id,
                status = job.Status,
                errorMessage = job.ErrorMessage,
                pngUrl = job.PngPath != null ? Url.Content("~/" + job.PngPath) : null,
                vtpUrl = job.VtpPath != null ? Url.Content("~/" + job.VtpPath) : null
            });
        }
    }
}
