using AxialFanMVC.Database;
using AxialFanMVC.Repositories.Inteface;
using AxialFanMVC.Services;
using AxialFanMVC.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;

namespace AxialFanMVC.Controllers
{
    [Authorize]
    public class ResultsController : Controller
    {
        private readonly IDesignResultRepository _repo;
        private readonly ICurveGeneration _curveService;

        public ResultsController(IDesignResultRepository repo, ICurveGeneration curveService)
        {
            _repo = repo;
            _curveService = curveService;
        }

        private int CurrentUserId =>
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // GET /Results/Result/7
        public async Task<IActionResult> Result(int resultId)
        {
            var result = await _repo.GetResultForUserAsync(resultId, CurrentUserId);
            if (result == null) return NotFound();

            var warnings = result.WarningMessages != null
                ? JsonSerializer.Deserialize<List<string>>(result.WarningMessages) ?? new()
                : new List<string>();

            // Most recent Baseline / PINN curves — ordered explicitly rather
            // than trusting FirstOrDefault() on an unordered collection,
            // which is what let the old duplicate-save bug silently pick
            // whichever row EF happened to return first.
            var baselineCurve = result.PerformanceCurves
                .Where(c => c.Source == "Baseline")
                .OrderByDescending(c => c.GeneratedAt)
                .FirstOrDefault();

            var correctedCurve = result.PerformanceCurves
                .Where(c => c.Source == "PINN")
                .OrderByDescending(c => c.GeneratedAt)
                .FirstOrDefault();

            var manualCurves = result.PerformanceCurves
                .Where(c => c.Source == "Manual")
                .OrderByDescending(c => c.GeneratedAt)
                .ToList();


            var curveJson = BuildCurveJson(baselineCurve, correctedCurve, manualCurves);
            

            var di = result.DesignInput;

            var vm = new DesignResultViewModel
            {
                DesignInputId = di.Id,
                ResultId = result.Id,
                ProjectId = di.ProjectId,
                ProjectName = di.Project.Name,

                FlowRateM3s = di.FlowRateM3s,
                TotalPressurePa = di.TotalPressurePa,
                SpeedRpm = di.SpeedRpm,
                BladeCount = di.BladeCount,
                TipDiameterMm = di.TipDiameterMm,
                BladeAngleDeg = di.BladeAngleDeg,
                BladeProfileName = di.BladeProfile?.Name,

                SpecificSpeed = result.SpecificSpeed,
                TipSpeedMs = result.TipSpeedMs,
                HubDiameterMm = result.HubDiameterMm,
                ChordLengthMm = result.ChordLengthMm,
                BladeSpanMm = result.BladeSpanMm,
                ShaftPowerKw = result.ShaftPowerKw,
                OverallEfficiencyPct = result.OverallEfficiencyPct,
                FlowCoefficient = result.FlowCoefficient,
                PressureCoefficient = result.PressureCoefficient,
                TipClearanceMm = result.TipClearanceMm,

                BladeStressMpa = result.BladeStressMpa,
                SafetyFactor = result.SafetyFactor,

                OverallNoiseDbA = result.OverallNoiseDbA,
                SoundPowerLevelDb = result.SoundPowerLevelDb,
                BladePassingFrequencyHz = result.BladePassingFrequencyHz,
                TipMachNumber = result.TipMachNumber,
                NoiseRatingValue = result.NoiseRatingValue,
                NoiseRating = result.NoiseRating,
                OctaveBandLwJson = result.OctaveBandLwJson,

                Status = result.Status,
                Warnings = warnings,
                CalculatedAt = result.CalculatedAt,
                CurveJson = curveJson,

                // Previously never set — the comparison card in the view
                // was dead code. Now populated from the same curves used
                // for CurveJson, so both stay in sync.
                BaselineComparison = baselineCurve != null
                    ? BuildComparison(baselineCurve, di) : null,
                PinnComparison = correctedCurve != null
                    ? BuildComparison(correctedCurve, di) : null,

                Drawings = result.Drawings.Select(d => new DrawingViewModel
                {
                    Id = d.Id,
                    DrawingType = d.DrawingType,
                    SvgData = d.SvgData,
                    HasDxf = d.DxfPath != null,
                    HasPdf = d.PdfPath != null,
                    GeneratedAt = d.GeneratedAt
                }).ToList()
            };

            return View(vm);
        }

        // POST /Results/GenerateCurve — AJAX endpoint
        [HttpPost]
        public async Task<IActionResult> GenerateCurve(int resultId, double bladeAngleDeg, int speedRpm)
        {
            try
            {
                var gen = await _curveService.GenerateAndSaveAsync(resultId, CurrentUserId, bladeAngleDeg, speedRpm);

                return Json(new
                {
                    baseline = new
                    {
                        q = gen.Baseline.QValues,
                        dp = gen.Baseline.DpValues,
                        eta = gen.Baseline.EtaValues,
                        kw = gen.Baseline.KwValues,
                        validationStatus = gen.BaselineFlags.Count == 0 ? "ok"
                            : gen.BaselineFlags.Any(f => f.Severity == "flagged") ? "flagged" : "corrected",
                        flags = gen.BaselineFlags
                    },
                    corrected = new
                    {
                        q = gen.Corrected.QValues,
                        dp = gen.Corrected.DpValues,
                        eta = gen.Corrected.EtaValues,
                        kw = gen.Corrected.KwValues,
                        validationStatus = gen.CorrectedFlags.Count == 0 ? "ok"
                            : gen.CorrectedFlags.Any(f => f.Severity == "flagged") ? "flagged" : "corrected",
                        flags = gen.CorrectedFlags
                    }
                });
            }
            catch (InvalidOperationException)
            {
                return NotFound();
            }
        }

        // POST /Results/SaveManualCurve — Feature 2: manual curve entry
        [HttpPost]
        public async Task<IActionResult> SaveManualCurve([FromBody] SaveManualCurveRequest req)
        {
            try
            {
                var curve = await _curveService.SaveManualCurveAsync(
                    req.ResultId, CurrentUserId, req.Label, req.BladeAngleDeg, req.SpeedRpm,
                    req.Q, req.Dp, req.Eta, req.Kw);

                return Json(new
                {
                    id = curve.Id,
                    label = curve.Label,
                    q = req.Q,
                    dp = req.Dp,
                    eta = req.Eta,
                    kw = req.Kw
                });
            }
            catch (InvalidOperationException)
            {
                return NotFound();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        // GET /Results/Download/7/dxf
        public async Task<IActionResult> Download(int drawingId, string format)
        {
            var drawing = await _repo.GetDrawingForUserAsync(drawingId, CurrentUserId);
            if (drawing == null) return NotFound();

            string? path = format.ToLower() switch
            {
                "dxf" => drawing.DxfPath,
                "pdf" => drawing.PdfPath,
                _ => null
            };

            if (path == null || !System.IO.File.Exists(path))
                return NotFound("File not yet generated.");

            var mimeType = format.ToLower() == "pdf" ? "application/pdf" : "application/dxf";
            return PhysicalFile(path, mimeType, Path.GetFileName(path));
        }

        // ── Private helpers — kept here since they're pure view-shaping,
        // not DB access, so they don't belong in the repository. ──

        private static string BuildCurveJson(PerformanceCurve? baseline, PerformanceCurve? corrected, List<PerformanceCurve> manualCurves)
        {
            object? ParseCurve(PerformanceCurve? c) => c == null ? null : new
            {
                q = c.QValues.Split(',').Select(double.Parse),
                dp = c.DpValues.Split(',').Select(double.Parse),
                eta = c.EtaValues.Split(',').Select(double.Parse),
                kw = c.KwValues.Split(',').Select(double.Parse),
                validationStatus = c.ValidationStatus,
                flags = string.IsNullOrEmpty(c.ValidationFlagsJson)
                    ? null : JsonSerializer.Deserialize<object>(c.ValidationFlagsJson)
            };

            return JsonSerializer.Serialize(new
            {
                baseline = ParseCurve(baseline),
                corrected = ParseCurve(corrected),
                manual = manualCurves.Select(c => new
                {
                    id = c.Id,
                    label = c.Label,
                    q = c.QValues.Split(',').Select(double.Parse),
                    dp = c.DpValues.Split(',').Select(double.Parse),
                    eta = c.EtaValues.Split(',').Select(double.Parse),
                    kw = c.KwValues.Split(',').Select(double.Parse)
                })
            });
        }

        private static CurveComparisonViewModel BuildComparison(PerformanceCurve curve, DesignInput di)
        {
            var q = curve.QValues.Split(',').Select(double.Parse).ToList();
            var dp = curve.DpValues.Split(',').Select(double.Parse).ToList();
            var eta = curve.EtaValues.Split(',').Select(double.Parse).ToList();
            var kw = curve.KwValues.Split(',').Select(double.Parse).ToList();

            // Nearest generated point to the design-input flow rate —
            // the curve is a discrete sample, not continuous, so exact
            // match isn't guaranteed.
            int designIdx = 0;
            double bestDelta = double.MaxValue;
            for (int i = 0; i < q.Count; i++)
            {
                double delta = Math.Abs(q[i] - di.FlowRateM3s);
                if (delta < bestDelta) { bestDelta = delta; designIdx = i; }
            }

            return new CurveComparisonViewModel
            {
                PressurePa = dp[designIdx],
                PeakPressurePa = dp.Max(),
                EfficiencyPct = eta[designIdx],
                PeakEfficiencyPct = eta.Max(),
                PowerKw = kw[designIdx]
            };
        }

        public class SaveManualCurveRequest
        {
            public int ResultId { get; set; }
            public string Label { get; set; } = "";
            public double BladeAngleDeg { get; set; }
            public int SpeedRpm { get; set; }
            public List<double> Q { get; set; } = new();
            public List<double> Dp { get; set; } = new();
            public List<double> Eta { get; set; } = new();
            public List<double> Kw { get; set; } = new();
        }
    }
}