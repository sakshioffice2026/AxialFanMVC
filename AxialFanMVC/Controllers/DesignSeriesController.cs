using AxialFanMVC.Database;
using AxialFanMVC.Services;
using AxialFanMVC.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using static AxialFanMVC.ViewModels.DesignSummaryViewModel;

namespace AxialFanMVC.Controllers
{
    [Authorize]
    public class DesignSeriesController : Controller
    {
        private readonly AxialFanDbContext _db;
        public DesignSeriesController(AxialFanDbContext db) => _db = db;

        private int CurrentUserId =>
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // GET /DesignSeries/Index?projectId=5
        // GET /DesignSeries/Index
        public async Task<IActionResult> Index()
        {
            var series = await _db.design_series
                .AsNoTracking()
                .Include(s => s.Project)
                .Include(s => s.BaseDesignInput)
                .Include(s => s.Variants)
                .Where(s => s.Project.UserId == CurrentUserId)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();

            var vm = new DesignSeriesListViewModel
            {
                Series = series.Select(s => new DesignSeriesSummaryViewModel
                {
                    Id = s.Id,
                    Name = s.Name,
                    BaseDesignLabel = $"{s.BaseDesignInput.TipDiameterMm:F0}mm, {s.BaseDesignInput.BladeCount} blades",
                    VariantCount = s.Variants.Count,
                    MinFlowM3s = s.Variants.Any()
                        ? s.Variants.Min(v => v.FlowRateM3s)
                        : s.BaseDesignInput.FlowRateM3s,
                    MaxFlowM3s = s.Variants.Any()
                        ? s.Variants.Max(v => v.FlowRateM3s)
                        : s.BaseDesignInput.FlowRateM3s,

                    // We'll use this in the next step.
                    ProjectName = s.Project.Name,

                    CreatedAt = s.CreatedAt
                }).ToList()
            };

            return View(vm);
        }

        // GET /DesignSeries/Create?projectId=5
        public async Task<IActionResult> Create(int projectId)
        {
            var project = await _db.Projects
                .FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == CurrentUserId);
            if (project == null) return NotFound();

            var baseDesigns = await _db.design_inputs
                .AsNoTracking()
                .Where(d => d.ProjectId == projectId)
                .OrderByDescending(d => d.CreatedAt)
                .ToListAsync();

            if (!baseDesigns.Any())
            {
                TempData["Error"] = "This project has no designs yet. Create a design in the wizard first, then generate a series from it.";
                return RedirectToAction("Index", "Projects");
            }

            var vm = new DesignSeriesCreateViewModel
            {
                ProjectId = projectId,
                ProjectName = project.Name,
                CatalogDiametersMm = DesignSeriesCatalog.StandardDiametersMm,
                AvailableBaseDesigns = baseDesigns.Select(d => new BaseDesignOptionViewModel
                {
                    Id = d.Id,
                    Label = $"{d.TipDiameterMm:F0}mm, {d.BladeCount} blades, {d.FlowRateM3s:F1} m³/s ({d.CreatedAt:dd MMM yyyy})"
                }).ToList()
            };

            return View(vm);
        }

        // POST /DesignSeries/Create
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(DesignSeriesCreateViewModel vm)
        {
            var project = await _db.Projects
                .FirstOrDefaultAsync(p => p.Id == vm.ProjectId && p.UserId == CurrentUserId);
            if (project == null) return NotFound();

            var baseInput = await _db.design_inputs
                .FirstOrDefaultAsync(d => d.Id == vm.BaseDesignInputId && d.ProjectId == vm.ProjectId);
            if (baseInput == null)
            {
                ModelState.AddModelError("BaseDesignInputId", "Selected base design not found.");
            }

            if (vm.SelectedDiametersMm == null || !vm.SelectedDiametersMm.Any())
            {
                ModelState.AddModelError("SelectedDiametersMm", "Select at least one size to generate.");
            }

            if (!ModelState.IsValid || baseInput == null)
            {
                vm.CatalogDiametersMm = DesignSeriesCatalog.StandardDiametersMm;
                var baseDesigns = await _db.design_inputs
                    .AsNoTracking()
                    .Where(d => d.ProjectId == vm.ProjectId)
                    .OrderByDescending(d => d.CreatedAt)
                    .ToListAsync();
                vm.AvailableBaseDesigns = baseDesigns.Select(d => new BaseDesignOptionViewModel
                {
                    Id = d.Id,
                    Label = $"{d.TipDiameterMm:F0}mm, {d.BladeCount} blades, {d.FlowRateM3s:F1} m³/s ({d.CreatedAt:dd MMM yyyy})"
                }).ToList();
                vm.ProjectName = project.Name;
                return View(vm);
            }

            await using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var series = new DesignSeries
                {
                    ProjectId = vm.ProjectId,
                    BaseDesignInputId = vm.BaseDesignInputId,
                    Name = vm.Name
                };
                _db.design_series.Add(series);
                await _db.SaveChangesAsync(); // need series.Id before linking variants

                foreach (var diameter in vm.SelectedDiametersMm.Distinct())
                {
                    var variant = DesignSeriesEngine.GenerateVariant(baseInput, diameter);
                    variant.DesignSeriesId = series.Id;
                    _db.design_inputs.Add(variant);
                }

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                TempData["Success"] = $"Series \"{series.Name}\" created with {vm.SelectedDiametersMm.Distinct().Count()} variant(s).";
                return RedirectToAction(nameof(Details), new { seriesId = series.Id });
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        // GET /DesignSeries/Details?seriesId=3
        public async Task<IActionResult> Details(int seriesId)
        {
            var series = await _db.design_series
                .AsNoTracking()
                .Include(s => s.Project)
                .Include(s => s.BaseDesignInput)
                .Include(s => s.Variants)
                    .ThenInclude(v => v.DesignResult)
                .FirstOrDefaultAsync(s => s.Id == seriesId && s.Project.UserId == CurrentUserId);

            if (series == null) return NotFound();

            var vm = new DesignSeriesDetailsViewModel
            {
                Id = series.Id,
                Name = series.Name,
                ProjectId = series.ProjectId,
                ProjectName = series.Project.Name,
                BaseDesignLabel = $"{series.BaseDesignInput.TipDiameterMm:F0}mm, {series.BaseDesignInput.BladeCount} blades",
                CreatedAt = series.CreatedAt,
                Variants = series.Variants
                    .OrderBy(v => v.TipDiameterMm)
                    .Select(v => new DesignSeriesVariantViewModel
                    {
                        DesignInputId = v.Id,
                        TipDiameterMm = v.TipDiameterMm,
                        FlowRateM3s = v.FlowRateM3s,
                        TotalPressurePa = v.TotalPressurePa,
                        MotorPowerKw = v.MotorPowerKw,
                        HasBeenCalculated = v.DesignResult != null,
                        ResultId = v.DesignResult?.Id
                    }).ToList()
            };

            return View(vm);
        }
    }
}