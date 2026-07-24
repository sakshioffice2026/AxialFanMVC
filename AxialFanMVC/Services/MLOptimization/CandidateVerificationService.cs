using AxialFanMVC.Database;
using AxialFanMVC.Models;
using Microsoft.EntityFrameworkCore;

namespace AxialFanMVC.Services.MLOptimization
{
    // ═══════════════════════════════════════════════════════════════
    // CandidateVerificationService — the "Step 4: verification" piece
    // from the surrogate+optimizer framework that wasn't built yet.
    //
    // optimizer_service.py returns 3 candidates (Budget/Silent/Premium)
    // based purely on the surrogate's millisecond ONNX predictions.
    // Before those numbers reach a user, this replays each candidate's
    // geometry through the SAME deterministic engine chain the rest of
    // the app treats as ground truth (Aero -> Struct -> Sound -> Bom) —
    // the identical call pattern SyntheticDataFactory uses to generate
    // the surrogate's own training data in the first place.
    //
    // All 3 candidates are verified up front (not just the one the user
    // picks), per the confirmed decision — so every option shown in the
    // wizard already carries a real, checked number, not just a guess.
    //
    // This never touches the DB beyond reads (cost rates + the project's
    // latest DesignInput as a baseline) — nothing here is persisted; the
    // verified numbers are written back onto the same OptimizeCandidateDto
    // objects and round-trip through OptimizationJob.ResultJson exactly
    // like the predicted_* fields already do.
    // ═══════════════════════════════════════════════════════════════
    public static class CandidateVerificationService
    {
        // Tolerances for flagging a meaningful surrogate/reality mismatch.
        // These are deliberately conservative starting values, not derived
        // from any statistical analysis of surrogate error — tighten or
        // loosen once real divergence data comes in from actual jobs.
        private const double EfficiencyDivergenceTolerancePct = 3.0;   // absolute percentage points
        private const double MinAcceptableSafetyFactor = 1.2;          // matches optimizer_service.py's default min_safety_factor

        public static async Task VerifyAllAsync(
            AxialFanDbContext db, int projectId, List<OptimizeCandidateDto> candidates)
        {
            var baseline = await db.design_inputs
                .AsNoTracking()
                .Where(d => d.ProjectId == projectId)
                .OrderByDescending(d => d.CreatedAt)
                .FirstOrDefaultAsync();

            if (baseline is null)
            {
                // Should not happen in practice — OptimizationController.Start
                // already requires a DesignInput to exist before a job can be
                // queued. If it's gone by the time verification runs (deleted
                // mid-flight), fail closed rather than show unverified numbers
                // as if they were checked.
                foreach (var c in candidates)
                {
                    c.Verified = false;
                    c.VerificationWarnings.Add(
                        "No design input found for this project — could not verify against the real calculation engines.");
                }
                return;
            }

            var rates = await db.cost_rates.AsNoTracking().ToListAsync();

            foreach (var candidate in candidates)
            {
                try
                {
                    var d = CloneBaseline(baseline);

                    // Override only what the optimizer actually searched over —
                    // everything else (environment, accessories, drive type,
                    // acoustic setup) stays as the real project's baseline, so
                    // the verified numbers reflect the actual installation, not
                    // a generic training-data default.
                    d.TipDiameterMm = candidate.TipDiameterMm;
                    d.HubRatio = candidate.HubRatio;
                    d.BladeAngleDeg = candidate.BladeAngleDeg;
                    d.BladeCount = candidate.BladeCount;
                    d.SpeedRpm = (int)Math.Round(candidate.SpeedRpm);

                    double chordMm = AeroCalcEngine.ComputeMeanChordMm(d.TipDiameterMm, d.HubRatio, d.BladeCount);
                    var profile = BladeProfileEngine.ResolveProfileDataFromDesignation(candidate.BladeProfile, chordMm);

                    var aero = AeroCalcEngine.Calculate(d, profile);

                    // Same 15% motor sizing margin SyntheticDataFactory uses —
                    // kept identical deliberately so a verified candidate's cost
                    // isn't computed on a different motor-sizing rule than the
                    // one the surrogate was trained against.
                    d.MotorPowerKw = Math.Round(aero.ShaftPowerKw * 1.15, 2);

                    var structResult = StructCalcEngine.Calculate(d, aero);
                    var sound = SoundCalcEngine.Calculate(d, aero);
                    var bom = BomCostingEngine.Calculate(d, structResult, rates);

                    candidate.VerifiedEfficiencyPct = aero.OverallEfficiencyPct;
                    candidate.VerifiedNoiseDbA = sound.LpOverallDba;
                    candidate.VerifiedSafetyFactor = structResult.SafetyFactor;
                    candidate.VerifiedCostTotal = bom.GrandTotal;
                    candidate.Verified = true;

                    double efficiencyDelta = Math.Abs(candidate.VerifiedEfficiencyPct - candidate.PredictedEfficiencyPct);
                    if (efficiencyDelta > EfficiencyDivergenceTolerancePct)
                    {
                        candidate.VerificationWarnings.Add(
                            $"Predicted efficiency ({candidate.PredictedEfficiencyPct:F1}%) diverged from the verified " +
                            $"result ({candidate.VerifiedEfficiencyPct:F1}%) by {efficiencyDelta:F1} points — " +
                            "the surrogate's estimate for this region of the design space may be less reliable.");
                    }

                    if (candidate.VerifiedSafetyFactor < MinAcceptableSafetyFactor)
                    {
                        candidate.VerificationWarnings.Add(
                            $"Verified safety factor ({candidate.VerifiedSafetyFactor:F2}) is below the required " +
                            $"minimum ({MinAcceptableSafetyFactor:F2}) — this candidate should not be presented as feasible " +
                            "even though the surrogate predicted it would pass.");
                    }

                    if (aero.Status.StartsWith("error"))
                    {
                        candidate.Verified = false;
                        candidate.VerificationWarnings.Add($"Deterministic aero calculation reported: {aero.Status}");
                    }
                }
                catch (Exception ex)
                {
                    // A candidate that throws during verification is a real,
                    // informative outcome (e.g. a degenerate geometry combo the
                    // surrogate never actually saw during training) — surface it
                    // as an unverified/failed candidate rather than letting one
                    // bad candidate take down verification for the other two.
                    candidate.Verified = false;
                    candidate.VerificationWarnings.Add(
                        $"Verification failed: {ex.GetType().Name} — {ex.Message}");
                }
            }
        }

        // Copies every field the deterministic engines read from a DesignInput,
        // EXCEPT the geometry knobs the optimizer searched over (those get
        // overridden by the caller right after this returns) and BladeProfileId
        // (left null deliberately — a candidate's blade profile is resolved by
        // designation string via BladeProfileEngine, not via this FK, since the
        // candidate may use a different profile than the project's baseline).
        // This is never saved (AsNoTracking baseline, never passed to
        // SaveChanges) — purely an in-memory calculation input.
        private static DesignInput CloneBaseline(DesignInput b) => new()
        {
            ProjectId = b.ProjectId,
            BladeMaterial = b.BladeMaterial,
            MediaType = b.MediaType,
            TemperatureCelsius = b.TemperatureCelsius,
            InletPressurePa = b.InletPressurePa,
            DensityKgM3 = b.DensityKgM3,
            AltitudeM = b.AltitudeM,
            AtmosphericPressureKPa = b.AtmosphericPressureKPa,
            RelativeHumidityPct = b.RelativeHumidityPct,
            Direction = b.Direction,
            InstallationType = b.InstallationType,
            Duty = b.Duty,
            FrequencyHz = b.FrequencyHz,
            MaxTipDiameterMm = b.MaxTipDiameterMm,
            MinEfficiencyPct = b.MinEfficiencyPct,
            MaxNoiseDbA = b.MaxNoiseDbA,
            MaxMotorPowerKw = b.MaxMotorPowerKw,
            PreferredBladeCount = b.PreferredBladeCount,
            MaxSpeedRpm = b.MaxSpeedRpm,
            FlowRateM3s = b.FlowRateM3s,
            StaticPressurePa = b.StaticPressurePa,
            TotalPressurePa = b.TotalPressurePa,
            MotorPoles = b.MotorPoles,
            MotorType = b.MotorType,
            VoltageSpec = b.VoltageSpec,
            InsulationClass = b.InsulationClass,
            StartingMethod = b.StartingMethod,
            AccInletGuard = b.AccInletGuard,
            AccOutletGuard = b.AccOutletGuard,
            AccVibrationIsolators = b.AccVibrationIsolators,
            AccFlexibleConnector = b.AccFlexibleConnector,
            AccSilencer = b.AccSilencer,
            AccBackdraftDamper = b.AccBackdraftDamper,
            AccessoryNotes = b.AccessoryNotes,
            TargetEfficiencyPct = b.TargetEfficiencyPct,
            HubDiameterMm = b.HubDiameterMm,
            RangeMinFlowM3h = b.RangeMinFlowM3h,
            RangeMaxFlowM3h = b.RangeMaxFlowM3h,
            RangeMinPressurePa = b.RangeMinPressurePa,
            RangeMaxPressurePa = b.RangeMaxPressurePa,
            RangeMinSpeedRpm = b.RangeMinSpeedRpm,
            RangeMaxSpeedRpm = b.RangeMaxSpeedRpm,
            DriveType = b.DriveType,
            MotorRpm = b.MotorRpm,
            FanRpm = b.FanRpm,
            BeltType = b.BeltType,
            PulleyRatio = b.PulleyRatio,
            NumberOfBelts = b.NumberOfBelts,
            CentreDistanceMm = b.CentreDistanceMm,
            VfdMinRpm = b.VfdMinRpm,
            VfdMaxRpm = b.VfdMaxRpm,
            VfdSpeedPct = b.VfdSpeedPct,
            CapacityFlowM3h = b.CapacityFlowM3h,
            CapacityStaticPa = b.CapacityStaticPa,
            CapacitySpeedRpm = b.CapacitySpeedRpm,
            CapacityMotorKw = b.CapacityMotorKw,
            CapacityEfficiencyPct = b.CapacityEfficiencyPct,
            DrawingTagNo = b.DrawingTagNo,
            NameplateText = b.NameplateText,
            ReceiverDistanceM = b.ReceiverDistanceM,
            AcousticEnvironment = b.AcousticEnvironment,
            DirectivityIndexDb = b.DirectivityIndexDb,
            InletAttenuationDb = b.InletAttenuationDb,
            OutletAttenuationDb = b.OutletAttenuationDb,
            CasingTransmissionLossDb = b.CasingTransmissionLossDb,
            SilencerAttenuationDb = b.SilencerAttenuationDb,
            RoomCorrectionDb = b.RoomCorrectionDb,
            BackgroundNoiseDbA = b.BackgroundNoiseDbA,
            SafetyMarginDb = b.SafetyMarginDb,
            DesignSeriesId = b.DesignSeriesId,
        };
    }
}