using System.Globalization;
using System.Text;
using AxialFanMVC.Database;
using AxialFanMVC.Models;
using AxialFanMVC.Services; // AeroCalcEngine, BladeProfileEngine, StructCalcEngine, SoundCalcEngine, BomCostingEngine
using Microsoft.EntityFrameworkCore;

namespace AxialFanMVC.Services.MLOptimization
{
    // ═══════════════════════════════════════════════════════════════
    // SyntheticDataFactory — Part 1 of the "Optimize for me" pipeline.
    //
    // Treats the existing deterministic engines (Aero -> Struct -> Sound
    // -> Bom) as a data factory: Latin Hypercube Sample the design space,
    // run every sample through the real chain, dump inputs+outputs to CSV.
    //
    // IMPORTANT — what "design space" means here:
    // AeroCalcEngine.Calculate does NOT take geometry alone. It evaluates
    // a geometry at a specific duty point (d.FlowRateM3s, d.TotalPressurePa)
    // via BladeElementEngine.ComputeAtPoint. That means the surrogate we
    // train on this CSV is a function of (duty point + geometry), not
    // geometry alone. The optimizer (Part 3) will hold duty point fixed
    // to whatever the user actually requested and search geometry only —
    // but the surrogate has to have SEEN a spread of duty points during
    // training or it will extrapolate blindly outside them. So duty point
    // (flow, pressure) is sampled here too, not fixed.
    //
    // Sample bounds below are the surrogate's entire valid domain. The
    // optimizer in Part 3 MUST clamp its search to these same bounds —
    // if it wanders outside them the surrogate is extrapolating and its
    // predictions are not trustworthy, however confident they look.
    // ═══════════════════════════════════════════════════════════════
    public static class SyntheticDataFactory
    {
        // Verify these designations match what's actually seeded in the
        // blade_profiles table before running — this list is written from
        // the README's stated seed data (NACA 4412/2412/0012 + Flat plate),
        // not queried live, so a mismatch here silently degrades every
        // sample that picks a bad designation to profile = null (flat-plate
        // BladeElementEngine fallback) instead of throwing.
        private static readonly string[] BladeProfileDesignations =
        {
            "NACA 4412", "NACA 2412", "NACA 0012", "Flat Plate"
        };

        private static readonly int[] BladeCountOptions = { 4, 6, 8, 10, 12 };

        public sealed record Bounds(
            double TipDiameterMmMin, double TipDiameterMmMax,
            double HubRatioMin, double HubRatioMax,
            double BladeAngleDegMin, double BladeAngleDegMax,
            int SpeedRpmMin, int SpeedRpmMax,
            double FlowRateM3sMin, double FlowRateM3sMax,
            double TotalPressurePaMin, double TotalPressurePaMax,
            double TemperatureCelsiusMin, double TemperatureCelsiusMax)
        {
            // Sensible industrial axial-fan envelope — widen deliberately
            // if your actual customer designs fall outside this range,
            // since the optimizer can never propose outside what this
            // generated.
            public static Bounds Default => new(
                TipDiameterMmMin: 300, TipDiameterMmMax: 3000,
                HubRatioMin: 0.30, HubRatioMax: 0.60,
                BladeAngleDegMin: 10, BladeAngleDegMax: 35,
                SpeedRpmMin: 500, SpeedRpmMax: 3000,
                FlowRateM3sMin: 0.5, FlowRateM3sMax: 20,
                TotalPressurePaMin: 50, TotalPressurePaMax: 2000,
                TemperatureCelsiusMin: -10, TemperatureCelsiusMax: 50);
        }

        public static async Task<int> GenerateAsync(
            AxialFanDbContext db, Stream output, int sampleCount, Bounds bounds, int? randomSeed = null)
        {
            var rates = await db.cost_rates.AsNoTracking().ToListAsync();
            var rng = randomSeed.HasValue ? new Random(randomSeed.Value) : new Random();

            // Continuous dims get proper Latin Hypercube stratification;
            // BladeCount/ProfileDesignation are categorical/discrete and
            // are sampled uniformly at random per row instead — LHS doesn't
            // add value for a 5-value discrete set, and mixing continuous
            // LHS with discrete columns in one matrix just adds complexity
            // for no statistical benefit here.
            // 7 continuous dims: tipDia, hubRatio, bladeAngle, rpm, flow, pressure, temp.
            var lhs = LatinHypercubeSample(sampleCount, 7, rng);

            await using var writer = new StreamWriter(output, Encoding.UTF8, leaveOpen: true);
            await writer.WriteLineAsync(
                "tip_diameter_mm,hub_ratio,blade_angle_deg,blade_count,speed_rpm,blade_profile," +
                "flow_rate_m3s,total_pressure_pa,temperature_celsius," +
                "efficiency_pct,shaft_power_kw,noise_dba,safety_factor,cost_total,is_feasible,status");

            int written = 0;
            for (int i = 0; i < sampleCount; i++)
            {
                double tipDia = Scale(lhs[i][0], bounds.TipDiameterMmMin, bounds.TipDiameterMmMax);
                double hubRatio = Scale(lhs[i][1], bounds.HubRatioMin, bounds.HubRatioMax);
                double bladeAngle = Scale(lhs[i][2], bounds.BladeAngleDegMin, bounds.BladeAngleDegMax);
                int rpm = (int)Math.Round(Scale(lhs[i][3], bounds.SpeedRpmMin, bounds.SpeedRpmMax));
                double flow = Scale(lhs[i][4], bounds.FlowRateM3sMin, bounds.FlowRateM3sMax);
                double pressure = Scale(lhs[i][5], bounds.TotalPressurePaMin, bounds.TotalPressurePaMax);
                double temp = Scale(lhs[i][6], bounds.TemperatureCelsiusMin, bounds.TemperatureCelsiusMax);

                int bladeCount = BladeCountOptions[rng.Next(BladeCountOptions.Length)];
                string profileDesignation = BladeProfileDesignations[rng.Next(BladeProfileDesignations.Length)];

                var d = new DesignInput
                {
                    ProjectId = 0, // synthetic — never persisted as a real design
                    TipDiameterMm = tipDia,
                    HubRatio = hubRatio,
                    BladeAngleDeg = bladeAngle,
                    BladeCount = bladeCount,
                    SpeedRpm = rpm,
                    FlowRateM3s = flow,
                    StaticPressurePa = pressure,
                    TotalPressurePa = pressure,
                    TemperatureCelsius = temp,
                    DensityKgM3 = 1.204, // EnvironmentCalcEngine recomputes this inside Calculate()
                    BladeMaterial = "Aluminum",
                    DriveType = "Direct Drive",
                    MotorPoles = "4-pole / 50 Hz"
                };

                bool feasible = true;
                string status = "ok";
                double efficiency = 0, shaftKw = 0, noiseDba = 0, safetyFactor = 0, costTotal = 0;

                try
                {
                    double chordMm = AeroCalcEngine.ComputeMeanChordMm(tipDia, hubRatio, bladeCount);
                    var profile = BladeProfileEngine.ResolveProfileDataFromDesignation(profileDesignation, chordMm);

                    var aero = AeroCalcEngine.Calculate(d, profile);
                    status = aero.Status;
                    efficiency = aero.OverallEfficiencyPct;
                    shaftKw = aero.ShaftPowerKw;

                    // Motor sizing margin — 15% over shaft power, matching
                    // common practice. VERIFY against whatever margin
                    // DesignController actually applies on the live wizard
                    // path before trusting this for cost training; if the
                    // real app uses a different factor the surrogate's
                    // cost predictions will be systematically off.
                    d.MotorPowerKw = Math.Round(shaftKw * 1.15, 2);

                    var structResult = StructCalcEngine.Calculate(d, aero);
                    safetyFactor = structResult.SafetyFactor;

                    var sound = SoundCalcEngine.Calculate(d, aero);
                    noiseDba = sound.LpOverallDba;

                    var bom = BomCostingEngine.Calculate(d, structResult, rates);
                    costTotal = bom.GrandTotal;

                    feasible = status == "ok"
                               && safetyFactor >= 1.0
                               && efficiency is > 0 and <= 100
                               && !double.IsNaN(efficiency) && !double.IsNaN(noiseDba) && !double.IsNaN(costTotal);
                }
                catch (Exception ex)
                {
                    // A thrown sample (e.g. divide-by-zero from a degenerate
                    // geometry combo) is a real, informative data point —
                    // record it as infeasible instead of silently dropping
                    // it, so the surrogate/optimizer learn to avoid that
                    // region instead of never seeing it.
                    feasible = false;
                    status = "error: " + ex.GetType().Name;
                }

                await writer.WriteLineAsync(string.Join(",", new[]
                {
                    tipDia.ToString("F2", CultureInfo.InvariantCulture),
                    hubRatio.ToString("F4", CultureInfo.InvariantCulture),
                    bladeAngle.ToString("F2", CultureInfo.InvariantCulture),
                    bladeCount.ToString(CultureInfo.InvariantCulture),
                    rpm.ToString(CultureInfo.InvariantCulture),
                    profileDesignation.Replace(",", ";"),
                    flow.ToString("F4", CultureInfo.InvariantCulture),
                    pressure.ToString("F2", CultureInfo.InvariantCulture),
                    temp.ToString("F2", CultureInfo.InvariantCulture),
                    efficiency.ToString("F3", CultureInfo.InvariantCulture),
                    shaftKw.ToString("F3", CultureInfo.InvariantCulture),
                    noiseDba.ToString("F2", CultureInfo.InvariantCulture),
                    safetyFactor.ToString("F3", CultureInfo.InvariantCulture),
                    costTotal.ToString("F2", CultureInfo.InvariantCulture),
                    feasible ? "1" : "0",
                    status.Replace(",", ";")
                }));

                written++;
            }

            await writer.FlushAsync();
            return written;
        }

        // Classic LHS: stratify each dimension into n equal-probability bins,
        // one sample per bin, then independently shuffle each column so
        // dimensions aren't correlated with each other.
        private static double[][] LatinHypercubeSample(int n, int dims, Random rng)
        {
            var result = new double[n][];
            for (int i = 0; i < n; i++) result[i] = new double[dims];

            for (int dim = 0; dim < dims; dim++)
            {
                var perm = Enumerable.Range(0, n).ToArray();
                // Fisher-Yates shuffle
                for (int i = n - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (perm[i], perm[j]) = (perm[j], perm[i]);
                }

                for (int i = 0; i < n; i++)
                {
                    // Random offset within the stratum, not just the bin center —
                    // avoids every sample sitting on a rigid grid.
                    double jitter = rng.NextDouble();
                    result[i][dim] = (perm[i] + jitter) / n;
                }
            }

            return result;
        }

        private static double Scale(double unit01, double min, double max) => min + unit01 * (max - min);
    }
}