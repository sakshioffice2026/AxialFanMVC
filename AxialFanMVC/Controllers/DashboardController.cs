using AxialFanMVC.Database;
using AxialFanMVC.Models;
using AxialFanMVC.Repositories.Inteface;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AxialFanMVC.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly AxialFanDbContext _db;
        private readonly  IExceptionHandlerRepository _exceptionHandlerRepository;

        public DashboardController(AxialFanDbContext db, IExceptionHandlerRepository exceptionHandlerRepository)
        {
            _db = db;
            _exceptionHandlerRepository = exceptionHandlerRepository;
        }

        private int CurrentUserId =>
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        public async Task<IActionResult> Index()
        {
            try
            {
                var userId = CurrentUserId;

                var projectCount = await _db.Projects.CountAsync(p => p.UserId == userId);

                var resultsQuery = _db.design_results
                    .Include(r => r.DesignInput).ThenInclude(di => di.Project)
                    .Include(r => r.DesignInput).ThenInclude(di => di.BladeProfile)
                    .Where(r => r.DesignInput.Project.UserId == userId);

                var totalDesigns = await resultsQuery.CountAsync();

                var vm = new DashboardViewModel
                {
                    ProjectCount = projectCount,
                    TotalDesigns = totalDesigns
                };

                if (totalDesigns == 0)
                {
                    return View(vm);
                }

                vm.AvgEfficiencyPct = await resultsQuery.AverageAsync(r => r.OverallEfficiencyPct);
                vm.AvgSafetyFactor = await resultsQuery.AverageAsync(r => r.SafetyFactor);
                vm.OkCount = await resultsQuery.CountAsync(r => r.Status == "ok");
                vm.WarningCount = await resultsQuery.CountAsync(r => r.Status == "warning");
                vm.ErrorCount = await resultsQuery.CountAsync(r => r.Status == "error");

                // Efficiency trend — last 12 designs in calculation order, oldest first.
                vm.EfficiencyTrend = await resultsQuery
                    .OrderByDescending(r => r.CalculatedAt)
                    .Take(12)
                    .OrderBy(r => r.CalculatedAt)
                    .Select(r => new DashboardTrendPoint
                    {
                        Label = r.CalculatedAt.ToString("dd MMM"),
                        EfficiencyPct = r.OverallEfficiencyPct
                    })
                    .ToListAsync();

                // Recent designs feed.
                vm.RecentDesigns = await resultsQuery
                    .OrderByDescending(r => r.CalculatedAt)
                    .Take(6)
                    .Select(r => new DashboardRecentDesign
                    {
                        ResultId = r.Id,
                        ProjectName = r.DesignInput.Project.Name,
                        BladeProfileName = r.DesignInput.BladeProfile != null
                            ? r.DesignInput.BladeProfile.Name
                            : "—",
                        FlowRateM3s = r.DesignInput.FlowRateM3s,
                        TotalPressurePa = r.DesignInput.TotalPressurePa,
                        OverallEfficiencyPct = r.OverallEfficiencyPct,
                        Status = r.Status,
                        CalculatedAt = r.CalculatedAt
                    })
                    .ToListAsync();

                // Blade profile usage.
                vm.BladeProfileUsage = await resultsQuery
                    .Where(r => r.DesignInput.BladeProfile != null)
                    .GroupBy(r => r.DesignInput.BladeProfile!.Name)
                    .Select(g => new DashboardProfileUsage
                    {
                        ProfileName = g.Key,
                        Count = g.Count()
                    })
                    .OrderByDescending(g => g.Count)
                    .Take(5)
                    .ToListAsync();

                var flaggedCurveCount = await _db.performance_curves
                    .Where(c => c.DesignResult.DesignInput.Project.UserId == userId
                        && c.ValidationStatus == "flagged")
                    .Select(c => c.DesignResultId)
                    .Distinct()
                    .CountAsync();

                vm.FlaggedCurveDesignCount = flaggedCurveCount;

                vm.RecentExportCount = await _db.export_logs
                    .CountAsync(e => e.UserId == userId &&
                                     e.ExportedAt >= DateTime.UtcNow.AddDays(-7));

                return View(vm);
            }
            catch (Exception ex)
            {
                _exceptionHandlerRepository.SaveException(
                    nameof(DashboardController),
                    nameof(Index),
                    ex.ToString());

                TempData["Error"] = "Unable to load dashboard.";

                return RedirectToAction("Index", "Projects");
            }
        }
    }
}