using AxialFanMVC.Models;
using AxialFanMVC.Repositories.Inteface;


namespace AxialFanMVC.Repositories
{
    public class PhysicsValidationEngine : IPhysicsValidationEngine
    {
        // Soft ceiling — see Part 1, Rule 2. No axial fan in this design
        // envelope should credibly exceed this.
        private const double MaxCredibleEfficiencyPct = 92.0;

        // Rule 7 — design-time tip Mach advisory threshold.
        private const double TipMachDesignWarningThreshold = 0.7;

        public PhysicsValidationResult Validate(
            PerformanceCurveData curve, PinnFeatureVector features, PhysicsValidationContext context)
        {
            if (curve == null) throw new ArgumentNullException(nameof(curve));
            if (features == null) throw new ArgumentNullException(nameof(features));

            var flags = new List<PhysicsValidationFlag>();
            var corrected = CloneCurve(curve);

            CheckEfficiencyBounds(corrected, flags);
            CheckNonNegativePressure(curve, corrected, flags); // reads original pre-clamp intent from caller's raw curve
            CheckPowerConsistency(corrected, flags);
            CheckTipMachDesignPoint(features, flags);
            CheckAffinityConsistency(corrected, context, flags);
            CheckMlExtrapolationGuard(features, flags);

            return new PhysicsValidationResult { CorrectedCurve = corrected, Flags = flags };
        }

        // Rule 2 — efficiency bounds
        private void CheckEfficiencyBounds(PerformanceCurveData c, List<PhysicsValidationFlag> flags)
        {
            for (int i = 0; i < c.EtaValues.Count; i++)
            {
                double eta = c.EtaValues[i];
                if (eta > MaxCredibleEfficiencyPct)
                {
                    flags.Add(new PhysicsValidationFlag
                    {
                        Rule = "EfficiencyBounds",
                        Severity = "corrected",
                        Message = $"Efficiency {eta:F1}% at Q={c.QValues[i]:F2} m³/s exceeds the realistic " +
                                  $"ceiling for axial fans (~{MaxCredibleEfficiencyPct:F0}%) — clamped.",
                        OriginalValue = eta,
                        CorrectedValue = MaxCredibleEfficiencyPct,
                        FlowRateAtViolation = c.QValues[i]
                    });
                    c.EtaValues[i] = MaxCredibleEfficiencyPct;
                }
                else if (eta < 0)
                {
                    flags.Add(new PhysicsValidationFlag
                    {
                        Rule = "EfficiencyBounds",
                        Severity = "corrected",
                        Message = $"Negative efficiency ({eta:F1}%) at Q={c.QValues[i]:F2} m³/s is not physically " +
                                   "meaningful — clamped to 0.",
                        OriginalValue = eta,
                        CorrectedValue = 0,
                        FlowRateAtViolation = c.QValues[i]
                    });
                    c.EtaValues[i] = 0;
                }
            }
        }

        // Rule 5 — non-negative pressure. AeroCalcEngine already applies
        // Math.Max(0, dp) before this runs, which silently hides the
        // violation — so we detect it by checking whether ΔP is sitting
        // exactly at the floor at a point where neighboring points suggest
        // it would have gone negative (a heuristic, since the true
        // pre-clamp value isn't passed through). This is flagged as
        // "info" rather than "corrected" since we can't recover the
        // original value to report it.
        private void CheckNonNegativePressure(PerformanceCurveData original, PerformanceCurveData c, List<PhysicsValidationFlag> flags)
        {
            for (int i = 1; i < c.DpValues.Count; i++)
            {
                bool sittingAtFloor = c.DpValues[i] == 0;
                bool trendWasDownward = c.DpValues[i - 1] > 0 &&
                    (i < 2 || c.DpValues[i - 1] < c.DpValues[i - 2]);

                if (sittingAtFloor && trendWasDownward)
                {
                    flags.Add(new PhysicsValidationFlag
                    {
                        Rule = "NonNegativePressure",
                        Severity = "info",
                        Message = $"ΔP is floored at 0 Pa at Q={c.QValues[i]:F2} m³/s — the requested operating " +
                                   "point is likely beyond what this geometry can achieve at this flow rate. " +
                                   "The pre-clamp value could not be recovered for this flag; consider re-checking " +
                                   "AeroCalcEngine.GenerateCurves to surface the raw value here in a future revision.",
                        FlowRateAtViolation = c.QValues[i]
                    });
                }
            }
        }

        // Rule 6 — power/energy consistency. Recomputes kW from Q, ΔP, η
        // and compares to the stored kW; also specifically catches the
        // near-zero-efficiency "reads as exactly 0 kW" masking case.
        private void CheckPowerConsistency(PerformanceCurveData c, List<PhysicsValidationFlag> flags)
        {
            for (int i = 0; i < c.QValues.Count; i++)
            {
                double q = c.QValues[i], dp = c.DpValues[i], eta = c.EtaValues[i], kw = c.KwValues[i];

                if (eta <= 1.0 && dp > 0 && q > 0)
                {
                    flags.Add(new PhysicsValidationFlag
                    {
                        Rule = "PowerConsistency",
                        Severity = "flagged",
                        Message = $"At Q={q:F2} m³/s, efficiency is only {eta:F2}% while ΔP={dp:F0} Pa is still " +
                                   "positive — the reported 0 kW at this point is a divide-by-near-zero artifact, " +
                                   "not a physically meaningful zero-power state. Needs engineering review rather " +
                                   "than an automatic correction, since the real power at this point is undefined " +
                                   "from the curve data alone.",
                        FlowRateAtViolation = q
                    });
                    continue;
                }

                if (eta > 1.0)
                {
                    double expectedKw = (q * dp) / (eta / 100.0 * 1000.0);
                    if (Math.Abs(expectedKw - kw) > Math.Max(0.01, expectedKw * 0.02)) // >2% disagreement
                    {
                        flags.Add(new PhysicsValidationFlag
                        {
                            Rule = "PowerConsistency",
                            Severity = "corrected",
                            Message = $"Stored power {kw:F3} kW at Q={q:F2} m³/s doesn't match Q·ΔP/η " +
                                       $"({expectedKw:F3} kW) — recalculated.",
                            OriginalValue = kw,
                            CorrectedValue = expectedKw,
                            FlowRateAtViolation = q
                        });
                        c.KwValues[i] = Math.Round(expectedKw, 3);
                    }
                }
            }
        }

        // Rule 7 — tip Mach design-point advisory (not per-curve-point;
        // this is a property of the design geometry/speed, so it's
        // reported once rather than per Q).
        private void CheckTipMachDesignPoint(PinnFeatureVector features, List<PhysicsValidationFlag> flags)
        {
            if (features.TipMachNumber > TipMachDesignWarningThreshold)
            {
                flags.Add(new PhysicsValidationFlag
                {
                    Rule = "TipMachDesignLimit",
                    Severity = "flagged",
                    Message = $"Design-point tip Mach number ({features.TipMachNumber:F3}) exceeds the " +
                               $"{TipMachDesignWarningThreshold:F1} advisory threshold — expect elevated noise " +
                                "and compressibility losses not captured by this curve model. Needs engineering " +
                                "review of blade tip speed / diameter, not an automatic correction.",
                    OriginalValue = features.TipMachNumber
                });
            }
        }

        // Rule 1 — affinity laws. Only runs when the caller supplies a
        // comparison curve at a different RPM for the SAME blade angle
        // (see PhysicsValidationContext) — affinity laws don't apply
        // across a blade-angle change (different cascade geometry).
        private void CheckAffinityConsistency(PerformanceCurveData c, PhysicsValidationContext context, List<PhysicsValidationFlag> flags)
        {
            var cmp = context?.ComparisonCurveAtDifferentRpm;
            if (cmp == null || cmp.SpeedRpm == c.SpeedRpm || Math.Abs(cmp.BladeAngleDeg - c.BladeAngleDeg) > 0.01)
                return;

            double ratio = (double)c.SpeedRpm / cmp.SpeedRpm;
            double expectedPressureRatio = ratio * ratio;

            // Compare at the point with the highest ΔP in the comparison curve
            // (peak is the most reliable/least-noisy point to compare on).
            int peakIdx = cmp.DpValues.IndexOf(cmp.DpValues.Max());
            if (peakIdx < 0 || peakIdx >= c.DpValues.Count || cmp.DpValues[peakIdx] <= 0) return;

            double actualRatio = c.DpValues[peakIdx] / cmp.DpValues[peakIdx];
            double deviationPct = Math.Abs(actualRatio - expectedPressureRatio) / expectedPressureRatio * 100.0;

            if (deviationPct > 15.0) // tolerance for a simplified curve model, not exact CFD
            {
                flags.Add(new PhysicsValidationFlag
                {
                    Rule = "AffinityLaws",
                    Severity = "flagged",
                    Message = $"Pressure ratio between {c.SpeedRpm} RPM and {cmp.SpeedRpm} RPM curves " +
                               $"({actualRatio:F2}) deviates {deviationPct:F0}% from the affinity-law expectation " +
                               $"({expectedPressureRatio:F2} = (N₂/N₁)²) at the same blade angle. This usually " +
                                "means the flow axis isn't shifting with speed in the curve generator — see the " +
                                "known AeroCalcEngine.GenerateCurves defect discussed separately. Not auto-corrected " +
                                "since fixing it requires changing how the curve is generated, not just its output.",
                    OriginalValue = actualRatio,
                    CorrectedValue = expectedPressureRatio
                });
            }
        }

        // Rule 8 — ML extrapolation guard. Placeholder bounds only — these
        // are NOT calibrated against real training data (no calibration
        // dataset was available to derive real bounds), and are marked as
        // such so nobody mistakes this for a validated range. Replace with
        // real bounds once calibration_cases data is analyzed.
        private void CheckMlExtrapolationGuard(PinnFeatureVector f, List<PhysicsValidationFlag> flags)
        {
            bool outOfRange =
                f.ReynoldsNumber < 1e4 || f.ReynoldsNumber > 5e6 ||
                f.Solidity < 0.1 || f.Solidity > 2.0 ||
                f.FlowCoefficient < 0 || f.FlowCoefficient > 1.0;

            if (outOfRange)
            {
                flags.Add(new PhysicsValidationFlag
                {
                    Rule = "MlExtrapolationGuard",
                    Severity = "flagged",
                    Message = "One or more inputs to the PINN correction model (Reynolds number, solidity, or " +
                               "flow coefficient) fall outside a conservative placeholder range for this design " +
                               "space — the ONNX correction may be extrapolating beyond data it was trained on. " +
                               "These bounds are provisional (not yet calibrated against real training data) " +
                               "and should be tightened once calibration_cases are analyzed."
                });
            }
        }

        private static PerformanceCurveData CloneCurve(PerformanceCurveData c) => new()
        {
            BladeAngleDeg = c.BladeAngleDeg,
            SpeedRpm = c.SpeedRpm,
            QValues = new List<double>(c.QValues),
            DpValues = new List<double>(c.DpValues),
            EtaValues = new List<double>(c.EtaValues),
            KwValues = new List<double>(c.KwValues)
        };
    }
}