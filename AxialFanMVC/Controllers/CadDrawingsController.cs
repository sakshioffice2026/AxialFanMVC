using AxialFanMVC.Database;
using AxialFanMVC.Repositories.Inteface;
using AxialFanMVC.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AxialFanMVC.ViewModels
{
    public class CadDrawingListItem
    {
        public int ResultId { get; set; }
        public string ProjectName { get; set; } = string.Empty;
        public double FlowRateM3s { get; set; }
        public double StaticPressurePa { get; set; }
        public int SpeedRpm { get; set; }
        public double OverallEfficiencyPct { get; set; }
        public DateTime CalculatedAt { get; set; }
    }
}

namespace AxialFanMVC.Controllers
{
    [Authorize]
    public class CadDrawingsController : Controller
    {
        private readonly AxialFanDbContext _db;
        private readonly IExceptionHandlerRepository _exceptionHandlerRepository;

        public CadDrawingsController(
            AxialFanDbContext db,
            IExceptionHandlerRepository exceptionHandlerRepository)
        {
            _db = db;
            _exceptionHandlerRepository = exceptionHandlerRepository;
        }

        private int CurrentUserId =>
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // ── GET /CadDrawings ──────────────────────────────────────────
        public async Task<IActionResult> Index()
        {
            try
            {
                var items = await _db.design_results
                    .AsNoTracking()
                    .Where(r => r.DesignInput.Project.UserId == CurrentUserId)
                    .OrderByDescending(r => r.CalculatedAt)
                    .Select(r => new CadDrawingListItem
                    {
                        ResultId = r.Id,
                        ProjectName = r.DesignInput.Project.Name,
                        FlowRateM3s = r.DesignInput.FlowRateM3s,
                        StaticPressurePa = r.DesignInput.StaticPressurePa,
                        SpeedRpm = r.DesignInput.SpeedRpm,
                        OverallEfficiencyPct = r.OverallEfficiencyPct,
                        CalculatedAt = r.CalculatedAt
                    })
                    .ToListAsync();

                return View(items);
            }
            catch (Exception ex)
            {
                _exceptionHandlerRepository.SaveException(
                    nameof(CadDrawingsController),
                    nameof(Index),
                    ex.ToString());

                return RedirectToAction("Index", "Home");
            }
        }
    }
}
