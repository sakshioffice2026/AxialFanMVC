using AxialFanMVC.Database;
using AxialFanMVC.Models;
using AxialFanMVC.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AxialFanMVC.Controllers
{
    [Authorize]
    public class CalibrationCasesController : Controller
    {
        private readonly AxialFanDbContext _db;

        public CalibrationCasesController(AxialFanDbContext db) => _db = db;

        // GET /CalibrationCases
        public async Task<IActionResult> Index()
        {
            var cases = await _db.calibration_cases
                .Include(c => c.Points)
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();
            return View(cases);
        }

        // GET /CalibrationCases/Create
        public IActionResult Create() => View(new CalibrationCaseCreateViewModel());

        // POST /CalibrationCases/Create
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CalibrationCaseCreateViewModel vm)
        {
            // Only rows where all three required values were actually filled
            // in count as real data — blank rows in the fixed 15-row grid
            // are just unused capacity, not validation errors.
            var realPoints = vm.Points
                .Where(p => p.FlowRateM3s.HasValue && p.PressureRisePa.HasValue && p.EfficiencyPct.HasValue)
                .ToList();

            if (realPoints.Count < 3)
                ModelState.AddModelError("", "Enter at least 3 real data points to form a usable curve.");

            if (!ModelState.IsValid)
                return View(vm);

            var geom = PinnFeatureEngine.ComputeGeometryFeatures(
                vm.TipDiameterMm, vm.HubRatio, vm.BladeAngleDeg, vm.BladeCount,
                vm.SpeedRpm, vm.DensityKgM3, vm.TemperatureCelsius);

            var entity = new CalibrationCase
            {
                SourceType = vm.SourceType,
                SourceDescription = vm.SourceDescription,
                TipDiameterMm = vm.TipDiameterMm,
                HubRatio = vm.HubRatio,
                BladeAngleDeg = vm.BladeAngleDeg,
                BladeCount = vm.BladeCount,
                SpeedRpm = vm.SpeedRpm,
                DensityKgM3 = vm.DensityKgM3,
                TemperatureCelsius = vm.TemperatureCelsius,
                BladeProfileDesignation = vm.BladeProfileDesignation,
                MaxCamberPct = vm.MaxCamberPct,
                MaxThicknessPct = vm.MaxThicknessPct,
                ChordLengthMm = geom.ChordLengthMm,
                TipSpeedMs = geom.TipSpeedMs,
                TipMachNumber = geom.TipMachNumber,
                Solidity = geom.Solidity,
                ReynoldsNumber = geom.ReynoldsNumber
            };

            foreach (var p in realPoints)
            {
                var (phi, psi) = PinnFeatureEngine.ComputePointCoefficients(
                    p.FlowRateM3s!.Value, p.PressureRisePa!.Value,
                    vm.TipDiameterMm, vm.HubRatio, geom.TipSpeedMs, vm.DensityKgM3);

                entity.Points.Add(new CalibrationCasePoint
                {
                    FlowRateM3s = p.FlowRateM3s.Value,
                    PressureRisePa = p.PressureRisePa.Value,
                    EfficiencyPct = p.EfficiencyPct!.Value,
                    PowerKw = p.PowerKw,
                    FlowCoefficient = phi,
                    PressureCoefficient = psi
                });
            }

            _db.calibration_cases.Add(entity);
            await _db.SaveChangesAsync();

            TempData["Success"] = $"Calibration case saved with {entity.Points.Count} points.";
            return RedirectToAction(nameof(Index));
        }
    }
}