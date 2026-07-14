using AxialFanMVC.Database;
using AxialFanMVC.Models;
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
        private readonly IExceptionHandlerRepository _exceptionHandlerRepository;
        private readonly IPhysicsValidationEngine _validator;

        public ResultsController(
        IDesignResultRepository repo,
        ICurveGeneration curveService,
        IExceptionHandlerRepository exceptionHandlerRepository,
        IPhysicsValidationEngine validator)
        {
            _repo = repo;
            _curveService = curveService;
            _exceptionHandlerRepository = exceptionHandlerRepository;
            _validator = validator;
        }
        private int CurrentUserId =>
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // GET /Results — entry point for the sidebar "Results" link.
        // There's no "list every result" view yet, and Result(int) needs a
        // specific id, so this jumps to the most recently calculated result
        // instead of leaving the link pointing at a route that doesn't
        // exist (which is what caused the blank 404 previously — Index
        // wasn't implemented on this controller at all). Falls back to
        // Projects if the user has no results yet (i.e. hasn't finished a
        // design wizard run).
        public async Task<IActionResult> Index()
        {
            try
            {
                var result = await _repo.GetMostRecentResultForUserAsync(CurrentUserId);

                if (result == null)
                {
                    TempData["Error"] = "No design results yet — finish the New Design wizard to create one.";
                    return RedirectToAction("Index", "Projects");
                }

                return RedirectToAction(nameof(Result), new { resultId = result.Id });
            }
            catch (Exception ex)
            {
                _exceptionHandlerRepository.SaveException(
                    nameof(ResultsController),
                    nameof(Index),
                    ex.ToString());

                TempData["Error"] = "An unexpected error occurred while loading results.";

                return RedirectToAction("Index", "Projects");
            }
        }



        // GET /Results/Result/7
        public async Task<IActionResult> Result(int resultId)
        {
            try
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
                //
                // FIX: every Regenerate-slider click on the results page saves
                // a NEW PerformanceCurve row (via GenerateCurve -> GenerateAndSaveAsync)
                // rather than overwriting anything, so "most recent by GeneratedAt"
                // could silently be a leftover exploratory curve at some other
                // blade angle / RPM the user was trying out — not the curve for
                // this design's own saved operating point. That mismatch is what
                // made the Baseline-vs-Adjusted comparison card show numbers that
                // didn't line up (Adjusted Estimate pulled from the wrong curve's
                // sample points). Now we prefer the curve whose BladeAngleDeg/SpeedRpm
                // match the design input's own saved values, and only fall back to
                // "most recent overall" if no such curve exists yet (e.g. an old
                // result saved before this fix).
                var baselineCandidates = result.PerformanceCurves.Where(c => c.Source == "Baseline");
                var correctedCandidates = result.PerformanceCurves.Where(c => c.Source == "PINN");

                var baselineCurve =
                    baselineCandidates
                        .Where(c => IsDesignPointCurve(c, di: result.DesignInput))
                        .OrderByDescending(c => c.GeneratedAt)
                        .FirstOrDefault()
                    ?? baselineCandidates.OrderByDescending(c => c.GeneratedAt).FirstOrDefault();

                var correctedCurve =
                    correctedCandidates
                        .Where(c => IsDesignPointCurve(c, di: result.DesignInput))
                        .OrderByDescending(c => c.GeneratedAt)
                        .FirstOrDefault()
                    ?? correctedCandidates.OrderByDescending(c => c.GeneratedAt).FirstOrDefault();

                var manualCurves = result.PerformanceCurves
                    .Where(c => c.Source == "Manual")
                    .OrderByDescending(c => c.GeneratedAt)
                    .ToList();


                // BuildCurveJson (below) already produces the {baseline, corrected,
                // manual} shape the view's JS expects (initialCurves.baseline /
                // .corrected / .manual), including validationStatus and the
                // deserialized flags — the inline flat-array version this replaces
                // was a structural mismatch (JS read initialCurves.baseline off an
                // array, which is always undefined), so curves silently failed to
                // populate on page load until the user clicked Regenerate.
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
                        ? await BuildBaselineComparisonAsync(baselineCurve, di, result) : null,
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
            catch (Exception ex)
            {
                _exceptionHandlerRepository.SaveException(
                    nameof(ResultsController),
                    nameof(Result),
                    ex.ToString());

                TempData["Error"] = "Unable to load the requested design result.";

                return RedirectToAction(nameof(Index));
            }
        }

        // POST /Results/GenerateCurve — AJAX endpoint

        [HttpPost]
        public async Task<IActionResult> GenerateCurve(int resultId, double bladeAngleDeg, int speedRpm)
        {
            try
            {
                var gen = await _curveService.GenerateAndSaveAsync(
                    resultId,
                    CurrentUserId,
                    bladeAngleDeg,
                    speedRpm);

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
            catch (Exception ex)
            {
                _exceptionHandlerRepository.SaveException(
                    nameof(ResultsController),
                    nameof(GenerateCurve),
                    ex.ToString());

                return Json(new
                {
                    success = false,
                    message = "An unexpected error occurred while generating the performance curve."
                });
            }
        }

        // POST /Results/SaveManualCurve — Feature 2: manual curve entry

        [HttpPost]
        public async Task<IActionResult> SaveManualCurve([FromBody] SaveManualCurveRequest req)
        {
            try
            {
                var curve = await _curveService.SaveManualCurveAsync(
                    req.ResultId,
                    CurrentUserId,
                    req.Label,
                    req.BladeAngleDeg,
                    req.SpeedRpm,
                    req.Q,
                    req.Dp,
                    req.Eta,
                    req.Kw);

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
            catch (Exception ex)
            {
                _exceptionHandlerRepository.SaveException(
                    nameof(ResultsController),
                    nameof(SaveManualCurve),
                    ex.ToString());

                return Json(new
                {
                    success = false,
                    message = "An unexpected error occurred while saving the manual curve."
                });
            }
        }
        // GET /Results/Download/7/dxf
        // GET /Results/Download/7/dxf
        public async Task<IActionResult> Download(int drawingId, string format)
        {
            try
            {
                var drawing = await _repo.GetDrawingForUserAsync(drawingId, CurrentUserId);

                if (drawing == null)
                    return NotFound();

                string? path = format.ToLower() switch
                {
                    "dxf" => drawing.DxfPath,
                    "pdf" => drawing.PdfPath,
                    _ => null
                };

                if (path == null || !System.IO.File.Exists(path))
                    return NotFound("File not yet generated.");

                var mimeType = format.ToLower() == "pdf"
                    ? "application/pdf"
                    : "application/dxf";

                return PhysicalFile(path, mimeType, Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                _exceptionHandlerRepository.SaveException(
                    nameof(ResultsController),
                    nameof(Download),
                    ex.ToString());

                TempData["Error"] = "Unable to download the requested file.";

                return RedirectToAction(nameof(Index));
            }
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

        private async Task<CurveComparisonViewModel> BuildBaselineComparisonAsync(
            PerformanceCurve curve, DesignInput di, DesignResult result)
        {
            var dp = curve.DpValues.Split(',').Select(double.Parse).ToList();
            var eta = curve.EtaValues.Split(',').Select(double.Parse).ToList();

            // Baseline row now recomputes the EXACT design-point value via
            // the same ComputeAtPoint call DesignController uses for the
            // top summary panel (result.OverallEfficiencyPct/ShaftPowerKw) —
            // instead of the old nearest-0.5-m³/s-sample lookup below, which
            // is what let this row and the top panel disagree (e.g. 0.0%
            // top panel vs 14.25% here) even though both were "using BEM":
            // they were evaluating BEM at two different flow rates.
            var bladeProfile = di.BladeProfileId.HasValue
                ? await _repo.GetBladeProfileAsync(di.BladeProfileId.Value)
                : null;
            var profileData = BladeProfileEngine.ResolveProfileData(bladeProfile, result.ChordLengthMm);

            // AxialVelocityMs computed inline (not via a full AeroCalcEngine.Calculate
            // re-run, which would also re-trigger environment/drive-type resolution
            // side effects unnecessarily) — it's the one extra field
            // PinnFeatureEngine.Compute reads off AeroCalcResult besides
            // ChordLengthMm, so this keeps the feature vector below consistent
            // with what the real curve's feature vector used.
            double tipRadiusM = di.TipDiameterMm / 2000.0;
            double hubRadiusM = tipRadiusM * di.HubRatio;
            double annulusAreaM2 = Math.PI * (tipRadiusM * tipRadiusM - hubRadiusM * hubRadiusM);
            var aeroForChord = new AeroCalcResult
            {
                ChordLengthMm = result.ChordLengthMm,
                AxialVelocityMs = annulusAreaM2 > 0 ? di.FlowRateM3s / annulusAreaM2 : 0
            };

            var (exactDp, exactEta, exactKw) = BladeElementEngine.ComputeAtPoint(
                di, aeroForChord, profileData, di.BladeAngleDeg, di.SpeedRpm, di.FlowRateM3s, out _);

            // Run this single point through the SAME PhysicsValidationEngine
            // pass the stored curve (dp/eta above) already went through when
            // it was generated (see CurveGeneration.GenerateAndSaveAsync).
            // Previously this method returned the raw, unvalidated BEM
            // output directly — which could legitimately exceed the stored
            // curve's own validated maximum, making "Overall Efficiency"
            // appear HIGHER than "Peak Efficiency" from the same curve
            // family. That's impossible by definition for two numbers meant
            // to describe the same physical curve, and was a real
            // apples-to-oranges mismatch: one raw, one clamped. Both are now
            // clamped consistently.
            var singlePointCurve = new PerformanceCurveData
            {
                BladeAngleDeg = di.BladeAngleDeg,
                SpeedRpm = di.SpeedRpm,
                QValues = new List<double> { di.FlowRateM3s },
                DpValues = new List<double> { exactDp },
                EtaValues = new List<double> { exactEta },
                KwValues = new List<double> { exactKw }
            };
            var features = PinnFeatureEngine.Compute(di, aeroForChord, profileData);
            var validated = _validator.Validate(
                singlePointCurve, features, new PhysicsValidationContext { CurveSource = "Baseline" });

            return new CurveComparisonViewModel
            {
                PressurePa = validated.CorrectedCurve.DpValues[0],
                PeakPressurePa = dp.Max(),
                EfficiencyPct = validated.CorrectedCurve.EtaValues[0],
                PeakEfficiencyPct = eta.Max(),
                PowerKw = validated.CorrectedCurve.KwValues[0]
            };
        }

        // Identifies whether a saved curve was generated AT the design's own
        // operating point (di.BladeAngleDeg / di.SpeedRpm) rather than at some
        // other angle/RPM the user tried via the Regenerate slider. Blade angle
        // is compared with a small tolerance since it round-trips through a
        // double; RPM is stored as an int so an exact match is fine. Used in
        // Result() above to pick the right Baseline/PINN curve pair for the
        // comparison card instead of just "whatever was generated last".
        private static bool IsDesignPointCurve(PerformanceCurve c, DesignInput di) =>
            Math.Abs(c.BladeAngleDeg - di.BladeAngleDeg) < 0.01 && c.SpeedRpm == di.SpeedRpm;

        // Corrected/PINN row still uses the nearest-sample lookup — its
        // design-point value includes the ONNX correction applied per-Q
        // during curve generation, which isn't something ComputeAtPoint
        // (BEM only, no ML correction) can reproduce exactly. This carries
        // up to ~0.25 m³/s of discretization error, smaller and more
        // defensible than the baseline mismatch this fixes, but a known
        // follow-up would be exposing a single-point corrected evaluation
        // (BEM + ONNX at the exact Q) the same way ComputeAtPoint does for
        // baseline-only.
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