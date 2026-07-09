using AxialFanMVC.Database;
using AxialFanMVC.Models;

namespace AxialFanMVC.Services
{
    // ═══════════════════════════════════════════════════════════════
    // BladeElementEngine — replaces AeroCalcEngine.GenerateCurves' tuned-
    // constant curve fit with a real Blade Element Theory (BET) calc.
    //
    // Method (Wallis, "Axial Flow Fans and Ducts", 1961; Dixon, "Fluid
    // Mechanics and Thermodynamics of Turbomachinery", Ch.7):
    //
    //   For N radial stations from hub to tip, at a given flow rate Q:
    //     1. Va = Q / annulusArea                     (actuator-disk —
    //        uniform axial velocity across the span)
    //     2. U(r) = ω·r                                (local blade speed)
    //     3. β1(r) = atan(Va / U(r))                   (relative inflow angle)
    //     4. α(r) = bladeAngleDeg − β1(r)               (angle of attack)
    //     5. Cl(r), Cd(r) from the profile's own thin-aerofoil data
    //        (BladeProfileEngine.AeroParams), clipped at stall
    //     6. Elemental lift/drag resolved through β1 into axial (pressure)
    //        and tangential (torque) components
    //     7. Simpson's-rule integration over r → total ΔP, shaft power, η
    //
    // Deliberate, documented simplifications (same spirit as this
    // codebase's other "known scope limits" comments — not papered over):
    //   - Constant chord and constant blade angle across the span. No
    //     radial twist/taper distribution is captured anywhere in the
    //     wizard, so every station uses the same mean chord (from
    //     AeroCalcEngine.ComputeMeanChordMm) and the same BladeAngleDeg.
    //     A real fan blade twists from hub to tip; this doesn't model that.
    //   - Single inflow angle (β1), not an iterated inlet/exit velocity
    //     triangle. This is the standard *isolated-aerofoil* blade-element
    //     method, not a cascade-corrected or free-vortex design method —
    //     it does not capture blade-to-blade interference beyond the
    //     solidity-independent Cl/Cd curves themselves.
    //   - Cl/Cd come from BladeProfileEngine's thin-aerofoil-theory
    //     estimates (ClAlpha = 2π, parabolic drag polar), not wind-tunnel
    //     or CFD polar data. Accurate in trend, not to the last percent.
    //   - When no blade profile is selected (ResolveProfileData returns
    //     null — a valid, common case), a generic flat-plate-like fallback
    //     polar is used (see FallbackClAlpha/FallbackCd0 below), clearly
    //     less accurate than a real selected profile.
    //
    // This is a real elemental physics calculation — a large step up from
    // the previous hand-tuned-constant curve fit — but it is still a
    // preliminary design estimate, not a CFD or cascade-test result.
    // ═══════════════════════════════════════════════════════════════
    public static class BladeElementEngine
    {
        private const int StationCount = 21; // odd count → Simpson's rule needs even # of intervals
        private const double DegToRad = Math.PI / 180.0;

        // Fallback polar when no blade profile is selected (ResolveProfileData
        // returned null). A generic thin symmetric plate — deliberately
        // conservative (low Cl, no camber benefit) rather than guessing a
        // shape that was never specified.
        private const double FallbackClAlpha = 2 * Math.PI; // per radian, thin-aerofoil theory
        private const double FallbackAlpha0Deg = 0.0;
        private const double FallbackCd0 = 0.02;
        private const double FallbackMaxCl = 1.0;
        private const double FallbackStallAngleDeg = 12.0;
        private const double FallbackDesignCl = 0.4;

        // Parabolic drag-polar curvature coefficient: Cd = Cd0 + k*(Cl-ClDesign)^2.
        // k=0.02 is a typical value for thin low-Reynolds aerofoils absent real
        // polar data — see BladeProfileEngine's own "Note" disclaimer on
        // AeroParameters for the same caveat applied to Cl/Cd generally.
        private const double DragPolarK = 0.02;

        // Post-stall drag penalty multiplier — once |α| exceeds the stall
        // angle, Cd rises sharply (separated flow) rather than continuing
        // the parabolic polar, which would understate drag badly here.
        private const double PostStallCdPenalty = 3.0;

        public static PerformanceCurveData GenerateCurves(
            DesignInput input, AeroCalcResult aero, BladeProfileData? profile,
            double bladeAngleDeg, int rpm, List<string>? warnings = null)
        {
            var curve = new PerformanceCurveData { BladeAngleDeg = bladeAngleDeg, SpeedRpm = rpm };
            int bladeCount = Math.Max(1, input.BladeCount);
            bool anyStationStalled = false;

            // Sample out to at least the design's own flow rate (with 20%
            // headroom), not just a fixed 10 m3/s — otherwise designs above
            // 10 m3/s never have their real duty point represented on their
            // own curve (same issue discussed for the old tuned-formula
            // baseline; applies here too since this is now the baseline).
            double qMax = Math.Max(10.0, input.FlowRateM3s * 1.2);
            double step = qMax / 20.0;

            for (int i = 0; i <= 20; i++)
            {
                double q = i * step;
                var (dp, eta, kw, stalled) = EvaluateBaselinePoint(
                    q, input.TipDiameterMm, input.HubRatio, aero.ChordLengthMm,
                    bladeCount, bladeAngleDeg, rpm, input.DensityKgM3, profile);

                if (stalled) anyStationStalled = true;

                curve.QValues.Add(Math.Round(q, 2));
                curve.DpValues.Add(Math.Round(dp, 1));
                curve.EtaValues.Add(Math.Round(eta, 2));
                curve.KwValues.Add(Math.Round(kw, 3));
            }

            if (anyStationStalled)
            {
                warnings?.Add("Info: one or more radial stations exceeded the profile's estimated stall " +
                    "angle somewhere on this curve — expect reduced accuracy (and a real fan would show a " +
                    "stall break) in that region. See BladeElementEngine's post-stall drag model.");
            }

            return curve;
        }

        // Baseline ΔP/η/kW (+ whether this station stalled) at a single (Q)
        // operating point, from raw geometry values rather than a full
        // DesignInput — so callers that don't have a DesignInput (e.g.
        // CalibrationCasesController's training-data export, which only has
        // a CalibrationCase's own geometry snapshot) can compute the exact
        // same baseline the live app uses, without constructing a fake
        // DesignInput just to call GenerateCurves. This is the ONE
        // implementation both paths share — see AeroCalcEngine.GenerateCurves'
        // comment about why that matters for PINN residual training targets
        // not drifting from inference. Callers that don't care about stall
        // (e.g. the CSV export) can just discard the 4th tuple element.
        public static (double DpPa, double EtaPct, double KwPa, bool Stalled) EvaluateBaselinePoint(
            double q, double tipDiameterMm, double hubRatio, double chordLengthMm,
            int bladeCount, double bladeAngleDeg, int rpm, double densityKgM3,
            BladeProfileData? profile = null)
        {
            double tipRadius = tipDiameterMm / 2000.0;
            double hubRadius = tipRadius * hubRatio;
            double chordM = chordLengthMm / 1000.0;
            double omega = 2 * Math.PI * rpm / 60.0;
            var polar = ResolvePolar(profile);

            var station = IntegrateStations(
                q, omega, tipRadius, hubRadius, chordM, Math.Max(1, bladeCount),
                bladeAngleDeg, densityKgM3, polar, out bool stalled);

            return (station.DeltaPPa, station.EfficiencyPct, station.ShaftPowerKw, stalled);
        }

        private readonly record struct Polar(
            double ClAlpha, double Alpha0Deg, double Cd0, double MaxCl, double StallAngleDeg, double DesignCl);

        private static Polar ResolvePolar(BladeProfileData? profile)
        {
            if (profile?.AeroParams == null)
            {
                return new Polar(FallbackClAlpha, FallbackAlpha0Deg, FallbackCd0,
                    FallbackMaxCl, FallbackStallAngleDeg, FallbackDesignCl);
            }

            var a = profile.AeroParams;
            return new Polar(
                a.LiftCurveSlope > 0 ? a.LiftCurveSlope : FallbackClAlpha,
                a.ApproxZeroLiftAngle,
                a.ApproxMinDrag > 0 ? a.ApproxMinDrag : FallbackCd0,
                a.ApproxMaxCl > 0 ? a.ApproxMaxCl : FallbackMaxCl,
                a.ApproxStallAngle > 0 ? a.ApproxStallAngle : FallbackStallAngleDeg,
                a.DesignLiftCoeff);
        }

        private readonly record struct StationResult(double DeltaPPa, double EfficiencyPct, double ShaftPowerKw);

        private static StationResult IntegrateStations(
            double q, double omega, double tipRadius, double hubRadius, double chordM,
            int bladeCount, double bladeAngleDeg, double densityKgM3, Polar polar, out bool stalled)
        {
            stalled = false;
            double annulusArea = Math.PI * (tipRadius * tipRadius - hubRadius * hubRadius);

            if (q <= 0 || annulusArea <= 0 || omega <= 0)
                return new StationResult(0, 0, 0);

            double va = q / annulusArea;
            double dr = (tipRadius - hubRadius) / (StationCount - 1);

            // Elemental axial-force and torque integrands, sampled at each
            // station, then combined via Simpson's rule (needs an even
            // number of intervals — StationCount is chosen odd above).
            var dFxPerSpan = new double[StationCount];
            var dTorquePerSpan = new double[StationCount];

            for (int i = 0; i < StationCount; i++)
            {
                double r = hubRadius + i * dr;
                if (r <= 1e-9) r = 1e-9;

                double u = omega * r;
                double beta1Rad = Math.Atan2(va, u);
                double beta1Deg = beta1Rad * 180.0 / Math.PI;

                double alphaDeg = bladeAngleDeg - beta1Deg;
                double alphaRad = alphaDeg * DegToRad;

                double cl = polar.ClAlpha * (alphaRad - polar.Alpha0Deg * DegToRad);
                bool stationStalled = Math.Abs(alphaDeg) > polar.StallAngleDeg;
                if (stationStalled)
                {
                    stalled = true;
                    cl = Math.Sign(cl) * Math.Min(Math.Abs(cl), polar.MaxCl);
                }

                double cd = polar.Cd0 + DragPolarK * Math.Pow(cl - polar.DesignCl, 2);
                if (stationStalled) cd *= PostStallCdPenalty;

                double wSq = va * va + u * u; // relative velocity squared from the velocity triangle
                double dynPressure = 0.5 * densityKgM3 * wSq;

                // Elemental lift/drag per unit span, resolved through the
                // relative flow angle into axial (pressure-producing) and
                // tangential (torque-producing) components.
                double liftPerSpan = cl * dynPressure * chordM;
                double dragPerSpan = cd * dynPressure * chordM;

                double axialPerSpan = liftPerSpan * Math.Cos(beta1Rad) - dragPerSpan * Math.Sin(beta1Rad);
                double tangentialPerSpan = liftPerSpan * Math.Sin(beta1Rad) + dragPerSpan * Math.Cos(beta1Rad);

                dFxPerSpan[i] = bladeCount * axialPerSpan;
                dTorquePerSpan[i] = bladeCount * tangentialPerSpan * r;
            }

            double totalFx = SimpsonIntegrate(dFxPerSpan, dr);
            double totalTorque = SimpsonIntegrate(dTorquePerSpan, dr);

            double deltaPPa = totalFx / annulusArea;
            double shaftPowerW = totalTorque * omega;
            double hydraulicPowerW = q * deltaPPa;

            double etaPct = shaftPowerW > 1e-6
                ? Math.Clamp(hydraulicPowerW / shaftPowerW * 100.0, 0, 100)
                : 0;

            return new StationResult(Math.Max(0, deltaPPa), etaPct, Math.Max(0, shaftPowerW) / 1000.0);
        }

        // Composite Simpson's rule over evenly spaced samples. StationCount
        // must be odd (even number of intervals) — enforced by the constant.
        private static double SimpsonIntegrate(double[] values, double dr)
        {
            int n = values.Length - 1; // number of intervals, must be even
            if (n % 2 != 0) throw new InvalidOperationException("StationCount must be odd for Simpson's rule.");

            double sum = values[0] + values[n];
            for (int i = 1; i < n; i++)
                sum += values[i] * (i % 2 == 0 ? 2.0 : 4.0);

            return sum * dr / 3.0;
        }
    }
}