using AxialFanMVC.Models;
using AxialFanMVC.Repositories.Inteface;
using System.Text.Json;

namespace AxialFanMVC.Services
{
    public class CurveGeneration : ICurveGeneration
    {
        private readonly IDesignResultRepository _repo;
        private readonly IPhysicsValidationEngine _validator;
        private readonly ICalibrationCaseRepository _calibrationRepo;

        // Matches ASP.NET Core MVC's default JSON output policy (used by
        // Controller.Json() in GenerateCurve). Keeping ValidationFlagsJson
        // in the same casing as that endpoint means BuildCurveJson's
        // pass-through and the AJAX response agree without a client-side
        // shim — see flagField() in Result.cshtml, which this makes
        // obsolete for any curve saved after this change.
        private static readonly JsonSerializerOptions FlagsJsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public CurveGeneration(IDesignResultRepository repo, IPhysicsValidationEngine validator, ICalibrationCaseRepository calibrationRepo)
        {
            _repo = repo;
            _validator = validator;
            _calibrationRepo = calibrationRepo;
        }

        public async Task<CurveGenerationResult> GenerateAndSaveAsync(
    int resultId, int userId, double bladeAngleDeg, int speedRpm)
        {
            var result = await _repo.GetResultForUserAsync(resultId, userId)
                ?? throw new InvalidOperationException($"DesignResult {resultId} not found or not owned by user {userId}.");

            // ── Profile resolved BEFORE Calculate now, same reordering as
            // DesignController.Wizard — Calculate needs profile + calibration
            // candidates to produce a real OverallEfficiencyPct instead of
            // falling back to the generic Cordier correlation every time this
            // runs. Uses a provisional chord (geometry-only, doesn't depend on
            // efficiency) since aero.ChordLengthMm isn't available yet at this
            // point — matches DesignController's approach exactly, so both
            // entry points stay consistent with each other. ──
            var bladeProfile = result.DesignInput.BladeProfileId.HasValue
                ? await _repo.GetBladeProfileAsync(result.DesignInput.BladeProfileId.Value)
                : null;
            double provisionalChordMm = AeroCalcEngine.ComputeMeanChordMm(
                result.DesignInput.TipDiameterMm, result.DesignInput.HubRatio, result.DesignInput.BladeCount);
            var profileData = BladeProfileEngine.ResolveProfileData(bladeProfile, provisionalChordMm);

            var calibrationCandidates = await _calibrationRepo.GetAllWithPointsAsync();

            var aero = AeroCalcEngine.Calculate(result.DesignInput, profileData, calibrationCandidates);

            var features = PinnFeatureEngine.Compute(result.DesignInput, aero, profileData);

            // ── Baseline ─────────────────────────────────────────────
            // BladeElementEngine.GenerateCurves (Wallis 1961 / Dixon Ch.7
            // BET) replaces AeroCalcEngine.GenerateCurves' unsourced tuned-
            // constant curve fit here — same physics already used for the
            // single-point efficiency in AeroCalcEngine.Calculate, now also
            // driving the full saved/exported curve.
            var baseline = BladeElementEngine.GenerateCurves(
                result.DesignInput, aero, profileData, bladeAngleDeg, speedRpm);

            var baselineValidation = _validator.Validate(
                baseline, features, new PhysicsValidationContext { CurveSource = "Baseline" });

            var baselineEntity = new PerformanceCurve
            {
                DesignResultId = resultId,
                Source = "Baseline",
                BladeAngleDeg = baselineValidation.CorrectedCurve.BladeAngleDeg,
                SpeedRpm = baselineValidation.CorrectedCurve.SpeedRpm,
                QValues = string.Join(",", baselineValidation.CorrectedCurve.QValues),
                DpValues = string.Join(",", baselineValidation.CorrectedCurve.DpValues),
                EtaValues = string.Join(",", baselineValidation.CorrectedCurve.EtaValues),
                KwValues = string.Join(",", baselineValidation.CorrectedCurve.KwValues),
                ValidationStatus = baselineValidation.OverallStatus,
                ValidationFlagsJson = JsonSerializer.Serialize(baselineValidation.Flags, FlagsJsonOptions)
            };
            await _repo.AddPerformanceCurveAsync(baselineEntity);

            // ── PINN corrected ───────────────────────────────────────
            var corrected = AeroCalcEngine.GenerateCorrectedCurves(
                result.DesignInput, aero, profileData, bladeAngleDeg, speedRpm);

            var correctedValidation = _validator.Validate(
                corrected, features, new PhysicsValidationContext
                {
                    CurveSource = "PINN",
                    MlModelAvailable = CurveCorrectionService.IsModelAvailable,
                    ComparisonCurveAtDifferentRpm = null
                });

            var correctedEntity = new PerformanceCurve
            {
                DesignResultId = resultId,
                Source = "PINN",
                BladeAngleDeg = correctedValidation.CorrectedCurve.BladeAngleDeg,
                SpeedRpm = correctedValidation.CorrectedCurve.SpeedRpm,
                QValues = string.Join(",", correctedValidation.CorrectedCurve.QValues),
                DpValues = string.Join(",", correctedValidation.CorrectedCurve.DpValues),
                EtaValues = string.Join(",", correctedValidation.CorrectedCurve.EtaValues),
                KwValues = string.Join(",", correctedValidation.CorrectedCurve.KwValues),
                ValidationStatus = correctedValidation.OverallStatus,
                ValidationFlagsJson = JsonSerializer.Serialize(correctedValidation.Flags, FlagsJsonOptions)
            };
            await _repo.AddPerformanceCurveAsync(correctedEntity);

            await _repo.SaveChangesAsync();

            return new CurveGenerationResult
            {
                Baseline = baselineValidation.CorrectedCurve,
                Corrected = correctedValidation.CorrectedCurve,
                BaselineFlags = baselineValidation.Flags,
                CorrectedFlags = correctedValidation.Flags
            };
        }

        public async Task<PerformanceCurve> SaveManualCurveAsync(
            int resultId, int userId, string label, double bladeAngleDeg, int speedRpm,
            List<double> q, List<double> dp, List<double> eta, List<double> kw)
        {
            // Ownership check even though AddManualCurveAsync doesn't take
            // userId directly for the insert — we must never let a user
            // attach a curve to a design result they don't own.Z
            var result = await _repo.GetResultForUserAsync(resultId, userId)
                ?? throw new InvalidOperationException($"DesignResult {resultId} not found or not owned by user {userId}.");

            if (q.Count != dp.Count || q.Count != eta.Count || q.Count != kw.Count || q.Count == 0)
                throw new ArgumentException("Q, ΔP, η, and kW arrays must be the same non-zero length.");

            return await _repo.AddManualCurveAsync(
                resultId, userId, label, bladeAngleDeg, speedRpm, q, dp, eta, kw);
        }
    }



}