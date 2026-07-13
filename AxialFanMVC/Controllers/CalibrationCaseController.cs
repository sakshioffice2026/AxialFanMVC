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

        // GET /CalibrationCases/ExportTrainingCsv
        //
        // Turns every stored calibration point into one training row for the
        // PINN correction model. Each row's "residual" columns are what
        // CurveCorrectionService.Predict is actually supposed to learn:
        //     residual = actual measured value − the live app's baseline
        // at that same (geometry, RPM, Q) — computed with the SAME
        // BladeElementEngine.EvaluateBaselinePoint the live app uses (via
        // AeroCalcEngine.GenerateCurves), so the exported targets can never
        // drift from what the model will be correcting at inference time.
        //
        // NOTE — baseline changed from a tuned formula to real Blade
        // Element Theory: any training CSV exported before this change (or
        // any ONNX model trained against it) used a different baseline and
        // must be re-exported/retrained — its residual targets no longer
        // match what CurveCorrectionService is actually correcting against.
        //
        // NOTE — blade profile resolution: CalibrationCase stores a profile
        // *designation* (string, e.g. "NACA 2412") and camber/thickness %,
        // not a full BladeProfileData/AeroParams object. This now resolves
        // that designation via BladeProfileEngine.ResolveProfileDataFromDesignation
        // (mirrors ResolveProfileData's NACA4/NACA5 dispatch, just from a bare
        // string instead of a saved BladeProfile entity) and passes the real
        // profile into EvaluateBaselinePoint, so a case with a known profile
        // trains against that profile's real Cl/Cd curve instead of the
        // generic flat-plate-like fallback. Cases with no designation (or an
        // unparseable one) still fall back to flat-plate, same as before —
        // that's the correct behavior when there's genuinely no profile
        // shape info available, not a bug.
        //
        // NOTE on Φ/Ψ/specific speed: the live app computes these once per
        // design (at the design's duty-point flow) and holds them fixed while
        // sweeping Q. A calibration case has no single "duty point" — it's a
        // full measured curve — so this export instead computes Φ/Ψ/specific
        // speed PER POINT, from that point's own flow rate. That is the more
        // physically correct pairing (local flow condition vs. local Q), but
        // it's a genuine mismatch against how CurveCorrectionService.Predict
        // is called live (fixed features + swept q). If you retrain against
        // this export, consider also changing CurveCorrectionService to
        // recompute Φ/Ψ per q rather than once per design — see chat.
        public async Task<IActionResult> ExportTrainingCsv()
        {
            var cases = await _db.calibration_cases
                .Include(c => c.Points)
                .AsNoTracking()
                .ToListAsync();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("case_id,source_type,flow_coefficient,pressure_coefficient,specific_speed," +
                          "tip_mach_number,solidity,reynolds_number,max_camber_pct,max_thickness_pct,q_m3s," +
                          "baseline_dp_pa,baseline_eta_pct,actual_dp_pa,actual_eta_pct,dp_residual,eta_residual");

            foreach (var c in cases)
            {
                // Resolved once per case (depends only on designation + chord,
                // not on the per-point flow rate) — null if the case has no
                // designation or it doesn't parse as NACA4/NACA5, in which case
                // EvaluateBaselinePoint falls back to the generic flat-plate
                // polar exactly as before.
                var profile = BladeProfileEngine.ResolveProfileDataFromDesignation(
                    c.BladeProfileDesignation, c.ChordLengthMm);

                foreach (var p in c.Points)
                {
                    var (baselineDp, baselineEta, _, _) = BladeElementEngine.EvaluateBaselinePoint(
                        p.FlowRateM3s, c.TipDiameterMm, c.HubRatio, c.ChordLengthMm,
                        c.BladeCount, c.BladeAngleDeg, c.SpeedRpm, c.DensityKgM3, profile);

                    double specificSpeed = 0;
                    if (c.DensityKgM3 > 0 && p.PressureRisePa > 0 && p.FlowRateM3s > 0)
                    {
                        double omega = 2 * Math.PI * c.SpeedRpm / 60.0;
                        specificSpeed = omega * Math.Pow(p.FlowRateM3s, 0.5)
                            * Math.Pow(p.PressureRisePa / c.DensityKgM3, -0.75);
                    }

                    double dpResidual = p.PressureRisePa - baselineDp;
                    double etaResidual = p.EfficiencyPct - baselineEta;

                    sb.AppendLine(string.Join(",", new[]
                    {
                        c.Id.ToString(),
                        c.SourceType,
                        p.FlowCoefficient.ToString("G6"),
                        p.PressureCoefficient.ToString("G6"),
                        specificSpeed.ToString("G6"),
                        c.TipMachNumber.ToString("G6"),
                        c.Solidity.ToString("G6"),
                        c.ReynoldsNumber.ToString("G6"),
                        (c.MaxCamberPct ?? 0).ToString("G6"),
                        (c.MaxThicknessPct ?? 0).ToString("G6"),
                        p.FlowRateM3s.ToString("G6"),
                        baselineDp.ToString("G6"),
                        baselineEta.ToString("G6"),
                        p.PressureRisePa.ToString("G6"),
                        p.EfficiencyPct.ToString("G6"),
                        dpResidual.ToString("G6"),
                        etaResidual.ToString("G6"),
                    }));
                }
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
            return File(bytes, "text/csv", "pinn_training_data.csv");
        }
    }
}