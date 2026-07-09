using AxialFanMVC.Database;
using AxialFanMVC.Models;
using AxialFanMVC.Repositories;

namespace AxialFanMVC.Services
{
    // ═══════════════════════════════════════════════════════════════
    // EnvironmentCalcEngine — derives absolute pressure and moist-air
    // density from Temperature / Altitude / Atmospheric Pressure /
    // Relative Humidity / Media Type. These five fields were captured
    // on the wizard's Air Input step but never fed into any calculation
    // — DensityKgM3 was always left at its static 1.204 kg/m³ default,
    // labelled "Auto" in the UI without ever being computed.
    //
    // Formulas:
    //   Barometric (ISA, troposphere): P = 101325 * (1 - 2.25577e-5*h)^5.25588
    //   Saturation vapour pressure (Arden Buck, 1996), kPa:
    //     Psat = 0.61121 * exp[(18.678 - T/234.5) * (T/(257.14+T))]   (T in °C)
    //   Moist-air density (ideal gas, dry + vapour partial pressures):
    //     ρ = Pdry/(Rd·T) + Pvapour/(Rv·T)     Rd=287.058, Rv=461.495 J/(kg·K)
    //
    // Known scope limits (deliberately not papered over):
    //   - InletPressurePa is not exposed on any wizard step (it's a hidden
    //     field that always carries its 101325 Pa default), so it is NOT
    //     used as a pressure basis here — Altitude / AtmosphericPressureKPa
    //     are the only user-editable pressure inputs.
    //   - "Flue gas" / "Custom gas" MediaType options have no composition
    //     data captured anywhere (no molecular-weight/gas-constant input
    //     exists), so density for those falls back to dry-air properties
    //     with an explicit warning rather than an invented correction
    //     factor for a gas we have no data about.
    // ═══════════════════════════════════════════════════════════════
    public static class EnvironmentCalcEngine
    {
        private const double R_DryAir = 287.058;  // J/(kg·K)
        private const double R_Vapor = 461.495;   // J/(kg·K)

        public static EnvironmentResult Compute(DesignInput d)
        {
            var r = new EnvironmentResult();
            double tC = d.TemperatureCelsius;
            double tK = tC + 273.15;

            // 1. Absolute pressure basis
            double pressurePa;
            if (d.AltitudeM.HasValue && Math.Abs(d.AltitudeM.Value) > 1e-6)
            {
                double h = d.AltitudeM.Value;
                pressurePa = 101325.0 * Math.Pow(1 - 2.25577e-5 * h, 5.25588);
                r.PressureSource = $"ISA barometric formula at {h:F0} m altitude";
            }
            else if (d.AtmosphericPressureKPa.HasValue)
            {
                pressurePa = d.AtmosphericPressureKPa.Value * 1000.0;
                r.PressureSource = "user-entered atmospheric pressure";
            }
            else
            {
                pressurePa = 101325.0;
                r.PressureSource = "standard sea-level default (101.325 kPa)";
            }
            r.AbsolutePressurePa = pressurePa;

            // 2. Humidity correction
            double rh = Math.Clamp((d.RelativeHumidityPct ?? 0) / 100.0, 0, 1);
            double pSatKPa = 0.61121 * Math.Exp((18.678 - tC / 234.5) * (tC / (257.14 + tC)));
            double pVapor = rh * pSatKPa * 1000.0;
            double pDry = Math.Max(0, pressurePa - pVapor);

            r.DensityKgM3 = pDry / (R_DryAir * tK) + pVapor / (R_Vapor * tK);

            if (d.MediaType is "Flue gas" or "Custom gas")
            {
                r.Warnings.Add($"Info: '{d.MediaType}' selected, but no gas composition (molecular weight / gas " +
                    "constant) is captured anywhere in the design input — density is computed using standard " +
                    "air properties as a placeholder and should be replaced with real gas data for this media type.");
            }

            return r;
        }
    }

    public class EnvironmentResult
    {
        public double AbsolutePressurePa { get; set; }
        public double DensityKgM3 { get; set; }
        public string PressureSource { get; set; } = "";
        public List<string> Warnings { get; set; } = new();
    }

    // ═══════════════════════════════════════════════════════════════
    // AeroCalcEngine — axial fan aerodynamic design calculations
    // Formulas follow standard turbomachinery conventions:
    //   Ns  = ω * Q^0.5 / (ΔP/ρ)^0.75   (dimensionless specific speed)
    //   Φ   = Va / U_tip                  (flow coefficient)
    //   Ψ   = ΔP / (ρ * U_tip²)          (pressure coefficient)
    //   U   = π * D * N / 60             (tip speed m/s)
    // ═══════════════════════════════════════════════════════════════
    public static class AeroCalcEngine
    {
        private const double Pi = Math.PI;

        // Shared with PinnFeatureEngine so the 0.45 solidity assumption
        // and chord formula only exist in one place.
        public static double ComputeMeanChordMm(double tipDiameterMm, double hubRatio, int bladeCount)
        {
            double tipRadius = tipDiameterMm / 2000.0;
            double hubRadius = tipRadius * hubRatio;
            double meanRadius = (tipRadius + hubRadius) / 2.0;
            const double solidity = 0.45;
            return solidity * 2 * Math.PI * meanRadius / bladeCount * 1000;
        }

        public static AeroCalcResult Calculate(
     DesignInput d, BladeProfileData? profile = null, List<CalibrationCase>? calibrationCandidates = null)
        {
            var r = new AeroCalcResult();

            // 0. Environment: compute real air density from Temperature /
            //    Altitude / AtmosphericPressureKPa / RelativeHumidityPct /
            //    MediaType, instead of leaving d.DensityKgM3 at its static
            //    1.204 kg/m³ default. This mutates the tracked DesignInput
            //    entity in place so the corrected value is what gets saved,
            //    and it's what all formulas below use via d.DensityKgM3.
            var env = EnvironmentCalcEngine.Compute(d);
            d.DensityKgM3 = env.DensityKgM3;
            r.Warnings.Add($"Info: air density computed as {env.DensityKgM3:F4} kg/m³ " +
                $"(T={d.TemperatureCelsius:F1}°C, RH={d.RelativeHumidityPct ?? 0:F0}%, " +
                $"pressure basis: {env.PressureSource}).");
            r.Warnings.AddRange(env.Warnings);

            // 0b. Drive-type resolution. "Direct Drive" and "Coupled" are
            //     1:1 mechanical connections, so the manually entered Fan
            //     Speed *is* the fan speed. "V-Belt Drive" and "Direct VFD"
            //     were previously captured on the wizard but never actually
            //     changed the fan speed used anywhere in the calculation —
            //     this mutates d.SpeedRpm (same pattern as the density fix
            //     above) so every downstream formula (tip speed, specific
            //     speed, structural stress, etc.) uses the real driven speed.
            switch (d.DriveType)
            {
                case "V-Belt Drive":
                    if (d.MotorRpm.HasValue && d.MotorRpm.Value > 0 &&
                        d.PulleyRatio.HasValue && d.PulleyRatio.Value > 0)
                    {
                        int beltFanRpm = (int)Math.Round(d.MotorRpm.Value / d.PulleyRatio.Value);
                        if (beltFanRpm != d.SpeedRpm)
                            r.Warnings.Add($"Info: V-Belt drive — fan speed computed as {beltFanRpm} RPM " +
                                $"({d.MotorRpm} RPM motor ÷ {d.PulleyRatio:F3} pulley ratio), overriding the " +
                                $"{d.SpeedRpm} RPM entered on the Motor step.");
                        d.SpeedRpm = beltFanRpm;
                    }
                    else
                    {
                        r.Warnings.Add("V-Belt Drive selected but Motor RPM / Pulley Ratio are not fully " +
                            "specified — falling back to the manually entered Fan Speed, which will not " +
                            "reflect the actual belt reduction.");
                    }
                    break;

                case "Direct VFD":
                    if (d.VfdMinRpm.HasValue && d.VfdMaxRpm.HasValue && d.VfdMaxRpm.Value >= d.VfdMinRpm.Value)
                    {
                        double speedPct = d.VfdSpeedPct ?? 100.0; // DesignInput's copy is nullable; default to 100%
                        double band = d.VfdMaxRpm.Value - d.VfdMinRpm.Value;
                        int vfdRpm = (int)Math.Round(d.VfdMinRpm.Value + band * (speedPct / 100.0));
                        if (vfdRpm != d.SpeedRpm)
                            r.Warnings.Add($"Info: Direct VFD drive — fan speed computed as {vfdRpm} RPM " +
                                $"({speedPct:F0}% of the {d.VfdMinRpm}-{d.VfdMaxRpm} RPM band), overriding " +
                                $"the {d.SpeedRpm} RPM entered on the Motor step.");
                        d.SpeedRpm = vfdRpm;
                    }
                    else
                    {
                        r.Warnings.Add("Direct VFD selected but the Min/Max RPM band is not fully specified — " +
                            "falling back to the manually entered Fan Speed, which will not reflect the VFD " +
                            "operating point.");
                    }
                    break;

                default:
                    // "Direct Drive", "Coupled", or unset: SpeedRpm as entered
                    // is the fan speed — no override needed.
                    break;
            }

            // 1. Basic geometry
            double tipRadius = d.TipDiameterMm / 2000.0;
            double hubRadius = tipRadius * d.HubRatio;
            r.HubDiameterMm = hubRadius * 2 * 1000;
            r.BladeSpanMm = (tipRadius - hubRadius) * 1000;

            // 2. Tip speed
            r.TipSpeedMs = Pi * (d.TipDiameterMm / 1000.0) * d.SpeedRpm / 60.0;

            // 3. Annulus area
            double annulusArea = Pi * (Math.Pow(tipRadius, 2) - Math.Pow(hubRadius, 2));

            // 4. Axial velocity
            r.AxialVelocityMs = d.FlowRateM3s / annulusArea;

            // 5. Specific speed (dimensionless)
            double omega = 2 * Pi * d.SpeedRpm / 60.0;
            r.SpecificSpeed = omega * Math.Sqrt(d.FlowRateM3s)
                              / Math.Pow(d.TotalPressurePa / d.DensityKgM3, 0.75);

            // 6. Flow and pressure coefficients
            r.FlowCoefficient = r.AxialVelocityMs / r.TipSpeedMs;
            r.PressureCoefficient = d.TotalPressurePa / (d.DensityKgM3 * Math.Pow(r.TipSpeedMs, 2));

            // 7. Mean radius chord

            r.ChordLengthMm = ComputeMeanChordMm(d.TipDiameterMm, d.HubRatio, d.BladeCount);

            // 8. Shaft power and efficiency
          
            double hydraulicPower = d.FlowRateM3s * d.TotalPressurePa;

            // Speed of sound at this design's temperature — needed for the tip Mach
            // term in the calibration-matching distance metric. (Standard linear
            // approximation; good to a few tenths of a percent in this temp range.)
            double speedOfSound = 331.3 + 0.606 * d.TemperatureCelsius;
            double tipMach = r.TipSpeedMs / speedOfSound;

            var effResult = EfficiencyEstimator.Estimate(d, r, tipMach, calibrationCandidates ?? new());
            r.OverallEfficiencyPct = Math.Round(effResult.EfficiencyPct, 2);
            r.ShaftPowerKw = hydraulicPower / (r.OverallEfficiencyPct / 100.0) / 1000.0;

            // Keep the user's TargetEfficiencyPct as a sanity check now that we
            // have a real estimate to check it against, instead of just accepting
            // it — a large gap here usually means either the target is unrealistic
            // or the calculated design isn't hitting its intended operating point.
            double targetGap = Math.Abs(d.TargetEfficiencyPct - r.OverallEfficiencyPct);
            if (targetGap > 10.0)
                r.Warnings.Add($"Target efficiency ({d.TargetEfficiencyPct:F1}%) differs from the " +
                    $"{effResult.Method}-based estimate ({r.OverallEfficiencyPct:F1}%) by {targetGap:F1} points " +
                    $"— {string.Join(" ", effResult.Notes)}");
            else
                r.Warnings.Add($"Info: efficiency estimated via {effResult.Method}" +
                    (effResult.MatchedCaseDescription != null ? $" ({effResult.MatchedCaseDescription})" : "") + ".");

            // 9. Tip clearance
            const double targetClearanceFraction = 0.005;  // 0.5% of tip diameter
            const double minClearanceMm = 1.5;             // manufacturing floor

            r.TipClearanceMm = Math.Max(minClearanceMm, d.TipDiameterMm * targetClearanceFraction);

            if (r.TipClearanceMm / d.TipDiameterMm > 0.01)
                r.Warnings.Add($"Tip clearance {r.TipClearanceMm:F2} mm exceeds 1% of tip diameter — efficiency loss expected.");
            // 10. Stall check
            if (r.FlowCoefficient < 0.15)
                r.Warnings.Add("Flow coefficient < 0.15 — risk of blade stall. Increase flow rate or reduce RPM.");

            // 11. Motor power check
            if (r.ShaftPowerKw > d.MotorPowerKw * 0.95)
                r.Warnings.Add($"Shaft power {r.ShaftPowerKw:F2} kW approaches motor rating {d.MotorPowerKw} kW. Consider upsizing motor.");

            // 12. Motor synchronous-speed feasibility (FrequencyHz × MotorPoles).
            //     Only meaningful for a 1:1 mechanical connection (Direct
            //     Drive / Coupled) where the fan turns at motor speed.
            //     V-Belt and Direct VFD drives deliberately run the fan at a
            //     different speed than the motor's synchronous speed, so this
            //     check does not apply to them — their effective speed is
            //     already resolved above from PulleyRatio / VFD band instead.
            if (string.IsNullOrEmpty(d.DriveType) || d.DriveType is "Direct Drive" or "Coupled")
            {
                int? poles = ParsePoleCount(d.MotorPoles);
                int freqHz = d.FrequencyHz ?? 50;
                if (poles is > 0)
                {
                    double syncRpm = 120.0 * freqHz / poles.Value;
                    if (d.SpeedRpm > syncRpm)
                        r.Warnings.Add($"Fan speed {d.SpeedRpm} RPM exceeds the {poles}-pole synchronous speed " +
                            $"({syncRpm:F0} RPM at {freqHz} Hz) — not physically achievable with a direct-drive induction motor.");
                    else if (d.SpeedRpm < syncRpm * 0.90)
                        r.Warnings.Add($"Fan speed {d.SpeedRpm} RPM is more than 10% below the {poles}-pole " +
                            $"synchronous speed ({syncRpm:F0} RPM at {freqHz} Hz) for a direct-drive motor — confirm this is intended.");
                }
            }

            // 13. Installation type — AMCA 210 system effect advisory.
            //     The three dropdown options correspond to AMCA 210 test
            //     configurations; only "Ducted – Ducted (Type D)" is tested
            //     ducted both sides, so anything else can see installed
            //     performance diverge from rated performance. This is an
            //     advisory note, not a computed correction — no numeric
            //     system effect factor is applied because that requires
            //     duct geometry data this tool doesn't collect.
            if (!string.IsNullOrEmpty(d.InstallationType) && d.InstallationType != "Ducted – Ducted (Type D)")
                r.Warnings.Add($"Info: installation type '{d.InstallationType}' differs from the AMCA 210 " +
                    "Type D (ducted/ducted) test configuration — installed performance may differ from the " +
                    "figures above; consult AMCA 210 system effect factors for the actual ductwork.");

            // 14. Duty cycle advisory. Intermittent duty (IEC 60034-1 S2/S3)
            //     can allow a smaller/thermally-derated motor than a
            //     continuous-duty (S1) assumption — advisory only, no
            //     numeric derate is applied since that depends on the
            //     specific duty cycle timing, which isn't captured here.
            if (d.Duty == "Intermittent")
                r.Warnings.Add("Info: 'Intermittent' duty selected — the motor-power check above assumes " +
                    "continuous (S1) duty; an intermittent duty cycle (IEC 60034-1 S2/S3) may permit a smaller " +
                    "motor. Confirm actual thermal duty cycle with the motor supplier.");

            // 15. Fan Constraints (wizard step 3) — these were captured but
            //     never checked against the actual design output.
            if (d.MaxTipDiameterMm.HasValue && d.TipDiameterMm > d.MaxTipDiameterMm.Value)
                r.Warnings.Add($"Tip diameter {d.TipDiameterMm:F0} mm exceeds the max constraint of {d.MaxTipDiameterMm:F0} mm.");

            if (d.MaxSpeedRpm.HasValue && d.SpeedRpm > d.MaxSpeedRpm.Value)
                r.Warnings.Add($"Fan speed {d.SpeedRpm} RPM exceeds the max constraint of {d.MaxSpeedRpm} RPM.");

            if (d.MaxMotorPowerKw.HasValue && d.MotorPowerKw > d.MaxMotorPowerKw.Value)
                r.Warnings.Add($"Selected motor {d.MotorPowerKw:F2} kW exceeds the max constraint of {d.MaxMotorPowerKw:F2} kW.");

            if (d.MinEfficiencyPct.HasValue && r.OverallEfficiencyPct < d.MinEfficiencyPct.Value)
                r.Warnings.Add($"Overall efficiency {r.OverallEfficiencyPct:F1}% is below the min constraint of " +
                    $"{d.MinEfficiencyPct:F1}%. Note: OverallEfficiencyPct is currently just the target efficiency " +
                    "input, not an independently computed value — see the separate defect for that calculation.");

            // MaxNoiseDbA is intentionally NOT checked here: there is no
            // acoustic prediction model anywhere in this codebase, and
            // inventing a plausible-looking dB(A) number with no basis
            // would be worse than leaving this constraint unchecked. A
            // real fix needs a documented sound-power correlation (e.g.
            // AMCA 301) or vendor test data, not a guess.

            // "Info:"-prefixed entries (density basis, media-type disclosure,
            // installation/duty advisories) are disclosures, not problems —
            // they shouldn't flip the overall status to "warning" the way a
            // real constraint violation or stall/stress warning should.
            r.Status = r.Warnings.Any(w => !w.StartsWith("Info:")) ? "warning" : "ok";
            return r;
        }

        // Extracts the leading integer from strings like "4-pole / 50 Hz".
        private static int? ParsePoleCount(string? motorPoles)
        {
            if (string.IsNullOrWhiteSpace(motorPoles)) return null;
            string digits = new(motorPoles.TakeWhile(char.IsDigit).ToArray());
            return int.TryParse(digits, out int n) ? n : null;
        }

        public static PerformanceCurveData GenerateCorrectedCurves(
            DesignInput input, AeroCalcResult aero, BladeProfileData? profile, double bladeAngleDeg, int rpm)
        {
            var baseline = GenerateCurves(input, aero, profile, bladeAngleDeg, rpm);
            var corrected = new PerformanceCurveData { BladeAngleDeg = bladeAngleDeg, SpeedRpm = rpm };
            var features = PinnFeatureEngine.Compute(input, aero, profile);

            for (int i = 0; i < baseline.QValues.Count; i++)
            {
                double q = baseline.QValues[i];
                var (dpCorr, etaCorr) = CurveCorrectionService.Predict(features, q);
                double dp = Math.Max(0, baseline.DpValues[i] + dpCorr);
                double eta = Math.Clamp(baseline.EtaValues[i] + etaCorr, 0, 100);
                double kw = eta > 1 ? (q * dp) / (eta / 100.0 * 1000.0) : 0;

                corrected.QValues.Add(q);
                corrected.DpValues.Add(Math.Round(dp, 1));
                corrected.EtaValues.Add(Math.Round(eta, 2));
                corrected.KwValues.Add(Math.Round(kw, 3));
            }
            return corrected;
        }

        public static PerformanceCurveData GenerateCurves(DesignInput input, AeroCalcResult aero, BladeProfileData? profile, double bladeAngleDeg, int rpm)
        {
            var curve = new PerformanceCurveData { BladeAngleDeg = bladeAngleDeg, SpeedRpm = rpm };

            // NOTE: features/CurveCorrectionService intentionally NOT used here.
            // This method must stay a pure baseline — GenerateCorrectedCurves()
            // is the only place the ONNX correction is applied. (Previously this
            // method applied CurveCorrectionService.Predict() itself AND
            // GenerateCorrectedCurves() applied it again on top — a double
            // correction. Fixed here.)

            // The curve used to always stop at Q=10 m3/s regardless of the
            // design's own flow rate. Designs above 10 m3/s (e.g. 20 m3/s)
            // never had their actual operating point represented on the
            // curve — ResultsController.BuildComparison would silently fall
            // back to the last point (Q=10), which usually isn't the real
            // duty point and can even land on a Baseline value of exactly
            // 0 Pa, producing a nonsensical "∞%" difference on the Result
            // page. Extend the sampled range to always cover the design's
            // flow rate (with 20% headroom past it), while keeping the same
            // 21-point resolution the rest of the app (charts, DB storage)
            // already assumes.
            double qMax = Math.Max(10.0, input.FlowRateM3s * 1.2);
            double step = qMax / 20.0;

            for (int i = 0; i <= 20; i++)
            {
                double q = i * step;
                var (dp, eta) = EvaluateBaselinePoint(bladeAngleDeg, rpm, q);
                double kw = eta > 1 ? (q * dp) / (eta / 100.0 * 1000.0) : 0;

                curve.QValues.Add(Math.Round(q, 2));
                curve.DpValues.Add(Math.Round(dp, 1));
                curve.EtaValues.Add(Math.Round(eta, 2));
                curve.KwValues.Add(Math.Round(kw, 3));
            }
            return curve;
        }

        // Pure baseline formula for a single (bladeAngleDeg, rpm, q) point —
        // extracted out of GenerateCurves so it has exactly one implementation.
        // Used by GenerateCurves itself, and by CalibrationCasesController's
        // training-data export, so the "what the PINN model is correcting
        // against" baseline can never silently drift between the live app
        // and the offline training pipeline.
        public static (double DpPa, double EtaPct) EvaluateBaselinePoint(double bladeAngleDeg, int rpm, double q)
        {
            double rScale = Math.Pow(rpm / 1450.0, 2);
            double aFactor = (bladeAngleDeg - 22.0) / 22.0;
            double peakQ = 5.0 + (bladeAngleDeg - 22.0) * 0.15;

            double dp = Math.Max(0, ((580 + aFactor * 160) - q * q * 5.8 * (1 - aFactor * 0.3)) * rScale);
            double d2 = q - peakQ;
            double eta = Math.Max(0, Math.Min(92, 82.0 * Math.Exp(-0.07 * d2 * d2) + (bladeAngleDeg - 22.0) * 0.25));

            return (dp, eta);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // StructCalcEngine — blade stress and structural checks
    // ═══════════════════════════════════════════════════════════════
    public static class StructCalcEngine
    {
        public static StructCalcResult Calculate(DesignInput d, AeroCalcResult aero)
        {
            var r = new StructCalcResult();

            double materialDensity = 2700;     // kg/m³ (Al alloy)
            double yieldStrength = 270e6;    // Pa (Al 6061-T6)
            double tipRadius = d.TipDiameterMm / 2000.0;
            double hubRadius = tipRadius * d.HubRatio;
            double chordM = aero.ChordLengthMm / 1000.0;
            double thickness = chordM * 0.12;  // 12% t/c ratio
            double omega = 2 * Math.PI * d.SpeedRpm / 60.0;

            double bladeVolume = chordM * thickness * (tipRadius - hubRadius);
            double bladeMass = bladeVolume * materialDensity;
            double centroidRadius = (tipRadius + hubRadius) / 2.0;
            double centrifugalForce = bladeMass * omega * omega * centroidRadius;
            double hubArea = chordM * thickness;
            r.BladeStressMpa = centrifugalForce / hubArea / 1e6;

            double liftPerSpan = d.TotalPressurePa * chordM;
            double span = tipRadius - hubRadius;
            double bendingMoment = liftPerSpan * span * span / 8.0;
            double sectionModulus = chordM * Math.Pow(thickness, 2) / 6.0;
            double bendStress = bendingMoment / sectionModulus / 1e6;

            r.TotalStressMpa = r.BladeStressMpa + bendStress;
            r.SafetyFactor = Math.Round(yieldStrength / 1e6 / r.TotalStressMpa, 2);

            if (r.SafetyFactor < 2.0)
                r.Warnings.Add($"Safety factor {r.SafetyFactor:F2} is below minimum of 2.0 — review blade geometry or material.");

            return r;
        }
        // Shared with PinnFeatureEngine so the 0.45 solidity assumption
        // and chord formula only exist in one place.
        public static double ComputeMeanChordMm(double tipDiameterMm, double hubRatio, int bladeCount)
        {
            double tipRadius = tipDiameterMm / 2000.0;
            double hubRadius = tipRadius * hubRatio;
            double meanRadius = (tipRadius + hubRadius) / 2.0;
            const double solidity = 0.45;
            return solidity * 2 * Math.PI * meanRadius / bladeCount * 1000;
        }
    }



    public class AeroCalcResult
    {
        public double HubDiameterMm { get; set; }
        public double BladeSpanMm { get; set; }
        public double TipSpeedMs { get; set; }
        public double AxialVelocityMs { get; set; }
        public double SpecificSpeed { get; set; }
        public double FlowCoefficient { get; set; }
        public double PressureCoefficient { get; set; }
        public double ChordLengthMm { get; set; }
        public double OverallEfficiencyPct { get; set; }
        public double ShaftPowerKw { get; set; }
        public double TipClearanceMm { get; set; }
        public string Status { get; set; } = "ok";
        public List<string> Warnings { get; set; } = new();
    }

    public class StructCalcResult
    {
        public double BladeStressMpa { get; set; }
        public double TotalStressMpa { get; set; }
        public double SafetyFactor { get; set; }
        public List<string> Warnings { get; set; } = new();
    }


}