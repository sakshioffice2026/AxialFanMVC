using AxialFanMVC.Database;
using AxialFanMVC.Models;

namespace AxialFanMVC.Services
{
    // ═══════════════════════════════════════════════════════════════
    // PinnFeatureEngine — converts a DesignInput (+ its AeroCalcResult /
    // blade profile) into the dimensionless feature vector used by the
    // performance-curve correction model. Deliberately reuses geometry
    // already resolved by AeroCalcEngine (tip/hub radius, chord) rather
    // than recomputing it, so the ML feature set can never drift out of
    // sync with the deterministic engine it's meant to correct.
    //
    // Administrative/motor/drive/acoustic/accessory fields on DesignInput
    // are intentionally NOT included — see design discussion: they have
    // no causal relationship to aerodynamic curve shape and would only
    // add noise. FlowRateM3s / StaticPressurePa / TargetEfficiencyPct are
    // also excluded — those are the target duty point, not a geometry
    // input, and including them would leak the answer.
    // ═══════════════════════════════════════════════════════════════
    public static class PinnFeatureEngine
    {
        private const double GasConstantAir = 287.058; // J/(kg·K)

        public static PinnFeatureVector Compute(DesignInput d, AeroCalcResult aero, BladeProfileData? profile)
        {
            double tipRadius = d.TipDiameterMm / 2000.0;
            double hubRadius = tipRadius * d.HubRatio;
            double meanRadius = (tipRadius + hubRadius) / 2.0;
            double chordM = aero.ChordLengthMm / 1000.0;

            double omega = 2 * Math.PI * d.SpeedRpm / 60.0;
            double tipSpeed = omega * tipRadius;

            double tK = d.TemperatureCelsius + 273.15;
            double speedOfSound = 331.3 * Math.Sqrt(1.0 + d.TemperatureCelsius / 273.15);
            double tipMach = tipSpeed / speedOfSound;

            // Flow / pressure coefficients — same definitions as AeroCalcEngine's
            // header comment (Φ = Va/U_tip, Ψ = ΔP/(ρ·U_tip²)), recomputed here
            // from the AeroCalcResult's own axial velocity/output so the feature
            // vector always matches what the deterministic engine actually used.
            double flowCoeff = tipSpeed > 0 ? aero.AxialVelocityMs / tipSpeed : 0;
            double dP = d.TotalPressurePa;
            double pressureCoeff = (d.DensityKgM3 > 0 && tipSpeed > 0)
                ? dP / (d.DensityKgM3 * tipSpeed * tipSpeed)
                : 0;

            // Solidity at mean radius: σ = (blade count × chord) / (2π × r_mean)
            double solidity = meanRadius > 0 ? (d.BladeCount * chordM) / (2 * Math.PI * meanRadius) : 0;

            // Reynolds number at mean blade section — NEW calculation, not
            // present anywhere else in the codebase. Needed because two
            // designs at identical Φ/Ψ but different absolute scale behave
            // differently (viscous loss/stall margin depend on Re, which
            // coefficient-space alone doesn't capture). Dynamic viscosity
            // via Sutherland's law rather than a fixed constant, since
            // TemperatureCelsius already varies meaningfully across designs.
            double mu = SutherlandViscosity(tK);
            double reynolds = (d.DensityKgM3 * tipSpeed * chordM) / mu;

            // Specific speed — same definition as AeroCalcEngine header:
            // Ns = ω·Q^0.5 / (ΔP/ρ)^0.75. Uses the design's own target flow
            // only to establish an operating-point-independent size/speed
            // descriptor for the geometry, NOT as a leaked target value —
            // it collapses to a function of D, N, and blade loading, the
            // same role it plays in AeroCalcEngine's aero design constraints.
            double specificSpeed = 0;
            if (d.DensityKgM3 > 0 && dP > 0 && d.FlowRateM3s > 0)
                specificSpeed = omega * Math.Pow(d.FlowRateM3s, 0.5) / Math.Pow(dP / d.DensityKgM3, 0.75);

            return new PinnFeatureVector
            {
                FlowCoefficient = flowCoeff,
                PressureCoefficient = pressureCoeff,
                SpecificSpeed = specificSpeed,
                TipMachNumber = tipMach,
                Solidity = solidity,
                ReynoldsNumber = reynolds,
                TipSpeedMs = tipSpeed,
                ChordLengthMm = aero.ChordLengthMm,
                HubRatio = d.HubRatio,
                BladeCount = d.BladeCount,
                MaxCamberPct = profile?.MaxCamberPct,
                MaxThicknessPct = profile?.MaxThicknessPct,
                HasBladeProfile = profile != null
            };
        }

        // Sutherland's law for dynamic viscosity of air, μ0/T0/C standard
        // reference values. Returns Pa·s.
        private static double SutherlandViscosity(double tKelvin)
        {
            const double mu0 = 1.716e-5; // Pa·s at T0
            const double t0 = 273.15;    // K
            const double sutherlandC = 110.4; // K, air

            return mu0 * Math.Pow(tKelvin / t0, 1.5) * (t0 + sutherlandC) / (tKelvin + sutherlandC);
        }
        // Geometry-only features — independent of operating point, computed
        // once per calibration case (not per point). Used both for the
        // manual-entry form and, later, for live DesignInput calculations.
        public static CaseGeometryFeatures ComputeGeometryFeatures(
            double tipDiameterMm, double hubRatio, double bladeAngleDeg,
            int bladeCount, int speedRpm, double densityKgM3, double temperatureCelsius)
        {
            double tipRadius = tipDiameterMm / 2000.0;
            double omega = 2 * Math.PI * speedRpm / 60.0;
            double tipSpeed = omega * tipRadius;

            double tK = temperatureCelsius + 273.15;
            double speedOfSound = 331.3 * Math.Sqrt(1.0 + temperatureCelsius / 273.15);
            double tipMach = tipSpeed / speedOfSound;

            double chordMm = AeroCalcEngine.ComputeMeanChordMm(tipDiameterMm, hubRatio, bladeCount);
            double chordM = chordMm / 1000.0;

            double hubRadius = tipRadius * hubRatio;
            double meanRadius = (tipRadius + hubRadius) / 2.0;
            double solidity = meanRadius > 0 ? (bladeCount * chordM) / (2 * Math.PI * meanRadius) : 0;

            double mu = SutherlandViscosity(tK);
            double reynolds = (densityKgM3 * tipSpeed * chordM) / mu;

            return new CaseGeometryFeatures
            {
                ChordLengthMm = chordMm,
                TipSpeedMs = tipSpeed,
                TipMachNumber = tipMach,
                Solidity = solidity,
                ReynoldsNumber = reynolds
            };
        }

        // Per-point coefficients — depend on that point's own flow rate and
        // pressure rise, at the case's fixed geometry/tip speed.
        public static (double Phi, double Psi) ComputePointCoefficients(
            double flowRateM3s, double pressureRisePa,
            double tipDiameterMm, double hubRatio, double tipSpeedMs, double densityKgM3)
        {
            double tipRadius = tipDiameterMm / 2000.0;
            double hubRadius = tipRadius * hubRatio;
            double annulusArea = Math.PI * (Math.Pow(tipRadius, 2) - Math.Pow(hubRadius, 2));

            double axialVelocity = annulusArea > 0 ? flowRateM3s / annulusArea : 0;
            double phi = tipSpeedMs > 0 ? axialVelocity / tipSpeedMs : 0;
            double psi = (densityKgM3 > 0 && tipSpeedMs > 0)
                ? pressureRisePa / (densityKgM3 * tipSpeedMs * tipSpeedMs)
                : 0;

            return (phi, psi);
        }
    }
    public class CaseGeometryFeatures
    {
        public double ChordLengthMm { get; set; }
        public double TipSpeedMs { get; set; }
        public double TipMachNumber { get; set; }
        public double Solidity { get; set; }
        public double ReynoldsNumber { get; set; }
    }

    // Feature vector consumed by the correction model. Kept as a distinct,
    // explicit type (not a Dictionary<string,double>) so the exact input
    // contract to the model is visible and refactor-safe at compile time.
    public class PinnFeatureVector
    {
        public double FlowCoefficient { get; set; }
        public double PressureCoefficient { get; set; }
        public double SpecificSpeed { get; set; }
        public double TipMachNumber { get; set; }
        public double Solidity { get; set; }
        public double ReynoldsNumber { get; set; }
        public double TipSpeedMs { get; set; }
        public double ChordLengthMm { get; set; }
        public double HubRatio { get; set; }
        public int BladeCount { get; set; }
        public double? MaxCamberPct { get; set; }
        public double? MaxThicknessPct { get; set; }
        public bool HasBladeProfile { get; set; }
    }
}