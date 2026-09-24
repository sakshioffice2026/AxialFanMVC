
using System;
using System.ComponentModel;
using AxialFanMVC.Database;
using Microsoft.SemanticKernel;

namespace AxialFanMVC.Repositories.Plugins;

/// <summary>
/// Semantic Kernel plugin that converts a plain-English axial fan duty
/// description into a fully populated <see cref="DesignInput"/> using
/// Fan Laws and turbomachinery engineering heuristics.
/// </summary>
public sealed class NaturalLanguageDesignPlugin
{
    // ── Constants ────────────────────────────────────────────────────────
    private const double Pi = Math.PI;
    private const double GravityMs2 = 9.81;
    private const double MotorServiceFactor = 1.20;   // 20 % power margin
    private const double AssumedEfficiency = 0.82;   // first-pass shaft power estimate

    // ── KernelFunction ───────────────────────────────────────────────────

    [KernelFunction("CreateNewDesign")]
    [Description(
        "Interprets a plain-English axial fan duty requirement and returns a fully " +
        "populated DesignInput with all engineering parameters calculated from Fan Laws " +
        "and turbomachinery heuristics. " +
        "Call this whenever the user describes a fan duty in natural language, e.g. " +
        "'Set up a cooling-tower fan at 12,000 CFM and 0.8 inWG static pressure'.")]
    public DesignInput CreateNewDesign(

        [Description(
            "Project id to associate this design with. " +
            "Extract from context or ask the user if not mentioned.")]
        int projectId,

        [Description(
            "Volume flow rate the fan must deliver, expressed in m³/s. " +
            "Unit conversions: 1 CFM = 0.000471947 m³/s | 1 m³/h = 1/3600 m³/s | " +
            "1 l/s = 0.001 m³/s. " +
            "Example: '12,000 CFM' → 12000 × 0.000471947 = 5.663 m³/s.")]
        double flowRateM3s,

        [Description(
            "Total pressure rise the fan must generate, in Pascals. " +
            "Unit conversions: 1 inWG = 249.089 Pa | 1 mmWG = 9.807 Pa | 1 mbar = 100 Pa. " +
            "If the user states only static pressure, add estimated velocity pressure " +
            "(~8–15 % of static) to obtain total pressure. " +
            "Example: '0.8 inWG static' → 0.8 × 249.089 × 1.10 ≈ 219 Pa total.")]
        double totalPressurePa,

        [Description(
            "Static pressure rise in Pascals. " +
            "If the user gives static pressure directly, convert and use it here. " +
            "If not stated, set to 90 % of totalPressurePa.")]
        double staticPressurePa,

        [Description(
            "Air or gas temperature at fan inlet, in °C. " +
            "Default 20 if not mentioned. " +
            "High-temperature duty (> 80 °C) affects density and blade material choice.")]
        double temperatureCelsius = 20,

        [Description(
            "Air density at fan inlet in kg/m³. " +
            "If not stated, the function will compute it from temperature using " +
            "ρ = 1.293 × 273.15 / (273.15 + T). " +
            "Standard air at 20 °C = 1.204 kg/m³.")]
        double densityKgM3 = 1.204,

        [Description(
            "Hard maximum allowable tip diameter in mm imposed by the installation. " +
            "Set 0 (default) to let the algorithm size the fan freely from the duty point.")]
        double maxTipDiameterMm = 0,

        [Description(
            "Drive arrangement. Choose: " +
            "'Direct Drive' – fan shaft coupled directly to motor, no speed change, lowest maintenance; " +
            "'V-Belt Drive' – use when fan speed must differ from available motor speed, or when the motor must sit outside the airstream; " +
            "'Variable Speed (VFD)' – use when the user mentions variable flow, energy saving, or speed control. " +
            "Default 'Direct Drive'.")]
        string driveType = "Direct Drive",

        [Description(
            "Motor enclosure and efficiency class. Choose: " +
            "'TEFC IE3' – standard choice for industrial applications, IP55, IE3 premium efficiency; " +
            "'TEFC IE2' – lower-cost alternative where IE3 is not mandated; " +
            "'EC Motor' – electronically commutated, best for variable-flow or low-noise duty; " +
            "'Ex-rated ATEX' – required when the medium is flammable or explosive. " +
            "Default 'TEFC IE3'.")]
        string motorType = "TEFC IE3",

        [Description(
            "Blade material. Choose: " +
            "'Aluminum 6061-T6' – standard, lightweight, clean air up to 150 °C; " +
            "'GRP' – glass-reinforced plastic for corrosive or chemically aggressive media; " +
            "'Mild Steel' – abrasive duty or high tip speed; " +
            "'Stainless Steel 316' – humid, corrosive, or marine environments. " +
            "Default 'Aluminum 6061-T6'.")]
        string bladeMaterial = "Aluminum 6061-T6",

        [Description(
            "Type of gas or medium moving through the fan, e.g. " +
            "'Air (standard)', 'Flue Gas', 'Humid Air', 'Saw Dust Laden Air'. " +
            "Default 'Air (standard)'.")]
        string mediaType = "Air (standard)",

        [Description(
            "Mains supply frequency in Hz. 50 for most of the world; 60 for North America, " +
            "parts of Japan and Saudi Arabia. Default 50.")]
        int frequencyHz = 50,

        [Description(
            "Preferred number of blades if explicitly stated by the user (2–24). " +
            "Set 0 to let the algorithm choose based on specific speed and application.")]
        int preferredBladeCount = 0,

        [Description(
            "Short description of the application. Used to fine-tune heuristics for blade count, " +
            "duty label and hub ratio. Examples: 'cooling tower', 'tunnel ventilation', " +
            "'HVAC supply', 'mine ventilation', 'industrial process exhaust'.")]
        string applicationDescription = "")

    {
        // ── Step 1: Recalculate density if it is still at default but T ≠ 20 ──
        if (Math.Abs(densityKgM3 - 1.204) < 0.001 && Math.Abs(temperatureCelsius - 20.0) > 0.5)
            densityKgM3 = 1.293 * 273.15 / (273.15 + temperatureCelsius);

        // ── Step 2: Default static pressure ──
        if (staticPressurePa <= 0)
            staticPressurePa = totalPressurePa * 0.90;

        // ── Step 3: Fan speed – select nearest synchronous speed for duty ──
        int speedRpm = SelectSynchronousSpeed(totalPressurePa, frequencyHz);

        // ── Step 4: Dimensionless specific speed ──
        //   Ω_s = ω · Q^0.5 / (g·H)^0.75   where H = ΔP / (ρ·g)
        double omega = speedRpm * 2.0 * Pi / 60.0;
        double headM = totalPressurePa / (densityKgM3 * GravityMs2);
        double nsD = omega * Math.Pow(flowRateM3s, 0.5) /
                          Math.Pow(GravityMs2 * headM, 0.75);

        // ── Step 5: Hub ratio from specific speed ──
        double hubRatio = SelectHubRatio(nsD);

        // ── Step 6: Tip diameter from flow coefficient ──
        //   Q = φ · (π/4) · D²·(1 − HR²) · U_tip   where U_tip = π·D·N/60
        //   ⟹  D³ = 60·Q / [ φ · (π²/4) · (1 − HR²) · N ]
        double phi = SelectFlowCoefficient(nsD);
        double tipDiameterM = Math.Pow(
            60.0 * flowRateM3s /
            (phi * (Pi * Pi / 4.0) * (1.0 - hubRatio * hubRatio) * speedRpm),
            1.0 / 3.0);
        double tipDiameterMm = tipDiameterM * 1000.0;

        // ── Step 7: Apply diameter constraint and re-snap speed if needed ──
        if (maxTipDiameterMm > 0 && tipDiameterMm > maxTipDiameterMm)
        {
            tipDiameterMm = maxTipDiameterMm;
            tipDiameterM = tipDiameterMm / 1000.0;

            // Fan Law: Q ∝ N·D³ → recompute N to maintain duty
            double rawRpm = 60.0 * flowRateM3s /
                (phi * (Pi * Pi / 4.0) * (1.0 - hubRatio * hubRatio) *
                 Math.Pow(tipDiameterM, 3));
            speedRpm = SnapToSynchronousSpeed((int)rawRpm, frequencyHz);
        }

        // Round tip diameter to nearest 50 mm standard size
        tipDiameterMm = Math.Round(tipDiameterMm / 50.0) * 50.0;
        tipDiameterMm = Math.Clamp(tipDiameterMm, 200.0, 5000.0);

        // ── Step 8: Blade count ──
        int bladeCount = preferredBladeCount > 0
            ? Math.Clamp(preferredBladeCount, 2, 24)
            : SelectBladeCount(nsD, applicationDescription);

        // ── Step 9: Blade pitch angle from pressure coefficient ──
        //   ψ = ΔP / (0.5 · ρ · U_tip²)
        double tipSpeedMs = Pi * (tipDiameterMm / 1000.0) * speedRpm / 60.0;
        double pressureCoeff = totalPressurePa / (0.5 * densityKgM3 * tipSpeedMs * tipSpeedMs);
        double bladeAngleDeg = SelectBladeAngle(pressureCoeff);

        // ── Step 10: Motor shaft power with service factor ──
        double shaftPowerKw = flowRateM3s * totalPressurePa / (AssumedEfficiency * 1000.0);
        double motorPowerKw = SnapToStandardMotorKw(shaftPowerKw * MotorServiceFactor);

        // ── Step 11: Motor poles string ──
        string motorPoles = BuildMotorPolesLabel(speedRpm, frequencyHz);

        // ── Step 12: Target efficiency estimate from specific speed curve ──
        double targetEfficiency = EstimateTargetEfficiency(nsD);

        // ── Step 13: Assemble DesignInput ──
        return new DesignInput
        {
            ProjectId = projectId,
            MediaType = mediaType,
            TemperatureCelsius = temperatureCelsius,
            InletPressurePa = 101325,
            DensityKgM3 = Math.Round(densityKgM3, 4),
            FlowRateM3s = Math.Round(flowRateM3s, 4),
            StaticPressurePa = Math.Round(staticPressurePa, 1),
            TotalPressurePa = Math.Round(totalPressurePa, 1),
            SpeedRpm = speedRpm,
            BladeCount = bladeCount,
            TipDiameterMm = tipDiameterMm,
            HubRatio = Math.Round(hubRatio, 2),
            BladeAngleDeg = bladeAngleDeg,
            TargetEfficiencyPct = Math.Round(targetEfficiency, 1),
            MotorPowerKw = motorPowerKw,
            BladeMaterial = bladeMaterial,
            MotorType = motorType,
            MotorPoles = motorPoles,
            DriveType = driveType,
            FrequencyHz = frequencyHz,
            Duty = DeriveApplicationLabel(applicationDescription),
            CreatedAt = DateTime.UtcNow
        };
    }

    // ── Engineering Heuristics ───────────────────────────────────────────

    /// <summary>
    /// Selects the synchronous motor speed (RPM) best suited to the pressure
    /// level.  Higher pressure → higher speed to keep diameter manageable.
    /// </summary>
    private static int SelectSynchronousSpeed(double totalPressurePa, int freqHz)
    {
        // 50 Hz sync speeds: 3000 / 1500 / 1000 / 750
        // 60 Hz sync speeds: 3600 / 1800 / 1200 / 900
        if (freqHz == 60)
        {
            if (totalPressurePa > 900) return 1800;
            if (totalPressurePa > 450) return 1200;
            return 900;
        }

        if (totalPressurePa > 900) return 1500;
        if (totalPressurePa > 450) return 1000;
        return 750;
    }

    /// <summary>
    /// Snaps an arbitrary RPM value to the nearest synchronous motor speed.
    /// </summary>
    private static int SnapToSynchronousSpeed(int rawRpm, int freqHz)
    {
        int[] pool = freqHz == 60
            ? new[] { 900, 1200, 1800, 3600 }
            : new[] { 750, 1000, 1500, 3000 };

        int best = pool[0];
        int minDiff = Math.Abs(rawRpm - best);
        foreach (int s in pool)
        {
            int diff = Math.Abs(rawRpm - s);
            if (diff < minDiff) { minDiff = diff; best = s; }
        }
        return best;
    }

    /// <summary>
    /// Hub-to-tip ratio from dimensionless specific speed Ω_s.
    /// Higher Ω_s (high-flow, low-pressure) → smaller hub, larger annulus.
    /// </summary>
    private static double SelectHubRatio(double nsD)
    {
        if (nsD < 0.40) return 0.62;
        if (nsD < 0.70) return 0.55;
        if (nsD < 1.10) return 0.50;
        if (nsD < 1.60) return 0.45;
        return 0.35;
    }

    /// <summary>
    /// Flow coefficient φ from dimensionless specific speed.
    /// Typical axial fan range: 0.15 – 0.32.
    /// </summary>
    private static double SelectFlowCoefficient(double nsD)
    {
        if (nsD < 0.50) return 0.15;
        if (nsD < 1.00) return 0.20;
        if (nsD < 1.50) return 0.25;
        return 0.30;
    }

    /// <summary>
    /// Blade count guided by specific speed and application type.
    /// Low specific speed (high pressure) → more blades for better pressure rise.
    /// High specific speed (high flow)    → fewer blades for lower drag.
    /// </summary>
    private static int SelectBladeCount(double nsD, string appDesc)
    {
        string a = appDesc.ToLowerInvariant();

        if (a.Contains("cooling tower")) return 6;   // large, slow, low noise
        if (a.Contains("tunnel") ||
            a.Contains("mine")) return 8;   // robust, high efficiency
        if (a.Contains("low noise") ||
            a.Contains("silent")) return 6;
        if (a.Contains("high pressure") ||
            a.Contains("booster")) return 10;

        // Specific-speed based default
        if (nsD < 0.50) return 10;
        if (nsD < 0.80) return 8;
        if (nsD < 1.30) return 6;
        return 4;
    }

    /// <summary>
    /// Blade pitch angle at mid-span derived from the pressure coefficient ψ.
    /// ψ = ΔP / (0.5 · ρ · U_tip²).  Typical axial fan ψ range: 0.05 – 0.40.
    /// Higher ψ requires steeper blade angle to deliver more work.
    /// </summary>
    private static double SelectBladeAngle(double pressureCoeff)
    {
        if (pressureCoeff > 0.38) return 34.0;
        if (pressureCoeff > 0.28) return 28.0;
        if (pressureCoeff > 0.18) return 22.0;
        if (pressureCoeff > 0.10) return 18.0;
        return 14.0;
    }

    /// <summary>
    /// Snaps motor power to the nearest IEC standard rating (kW) equal to or
    /// above the required value.
    /// </summary>
    private static double SnapToStandardMotorKw(double requiredKw)
    {
        double[] iec = {
            0.09, 0.12, 0.18, 0.25, 0.37, 0.55, 0.75,
            1.1,  1.5,  2.2,  3.0,  4.0,  5.5,  7.5,
            11,   15,   18.5, 22,   30,   37,   45,
            55,   75,   90,   110,  132,  160,  200,
            250,  315,  400,  500
        };
        foreach (double s in iec)
            if (s >= requiredKw) return s;
        return Math.Ceiling(requiredKw / 50.0) * 50.0;
    }

    /// <summary>
    /// Builds the motor poles / frequency label stored in DesignInput.MotorPoles.
    /// </summary>
    private static string BuildMotorPolesLabel(int speedRpm, int freqHz)
    {
        if (freqHz == 60)
        {
            if (speedRpm >= 3000) return "2-pole / 60 Hz";
            if (speedRpm >= 1500) return "4-pole / 60 Hz";
            if (speedRpm >= 1000) return "6-pole / 60 Hz";
            return "8-pole / 60 Hz";
        }

        if (speedRpm >= 2500) return "2-pole / 50 Hz";
        if (speedRpm >= 1200) return "4-pole / 50 Hz";
        if (speedRpm >= 800) return "6-pole / 50 Hz";
        return "8-pole / 50 Hz";
    }

    /// <summary>
    /// Estimates peak total-to-total efficiency from the dimensionless
    /// specific speed using the standard axial fan efficiency hill chart.
    /// </summary>
    private static double EstimateTargetEfficiency(double nsD)
    {
        if (nsD < 0.30) return 70.0;   // poor match for axial type
        if (nsD < 0.50) return 75.0;
        if (nsD < 0.80) return 79.0;
        if (nsD < 1.10) return 83.0;   // optimum axial range
        if (nsD < 1.50) return 85.0;
        if (nsD < 2.00) return 83.0;
        return 80.0;
    }

    /// <summary>
    /// Maps free-text application description to a standardised duty label.
    /// </summary>
    private static string DeriveApplicationLabel(string appDesc)
    {
        string a = appDesc.ToLowerInvariant();
        if (a.Contains("cooling tower")) return "Cooling Tower";
        if (a.Contains("tunnel")) return "Tunnel Ventilation";
        if (a.Contains("mine")) return "Mine Ventilation";
        if (a.Contains("hvac") || a.Contains("supply") ||
            a.Contains("extract") || a.Contains("ahu")) return "HVAC";
        if (a.Contains("process") || a.Contains("boiler")) return "Process Air";
        if (a.Contains("industrial")) return "Industrial Ventilation";
        if (a.Contains("exhaust")) return "Exhaust";
        return string.IsNullOrWhiteSpace(appDesc)
            ? "General Ventilation"
            : appDesc;
    }
}