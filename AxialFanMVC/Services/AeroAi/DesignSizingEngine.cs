using System.Text.Json;
using AxialFanMVC.Database;
using AxialFanMVC.Models;

namespace AxialFanMVC.Services.AeroAi
{
    public class SizingSnapshot
    {
        public DesignInput Input { get; set; } = null!;
        public AeroCalcResult Aero { get; set; } = null!;
        public StructCalcEngine.StructCalcResult Struct { get; set; } = null!;
        public SoundCalcResult Sound { get; set; } = null!;
        public double Cost { get; set; }
        public List<string> Warnings { get; set; } = new();
    }

    public class SizingOutcome
    {
        public SizingSnapshot Baseline { get; set; } = null!;
        public SizingSnapshot Optimized { get; set; } = null!;
        public bool Improved { get; set; }
        public bool Feasible { get; set; }
        public int Evaluations { get; set; }
    }

    // Step 2 of the AeroAi optimize flow: multi-variable physics solver.
    // Coordinate-descent with shrinking steps over tip diameter, hub ratio,
    // blade angle, blade count and speed, replaying every trial through the
    // same deterministic engine chain the rest of the app uses
    // (Aero -> Struct -> Sound -> Bom). Nothing is persisted here.
    public static class DesignSizingEngine
    {
        private const double MinSafetyFactor = 1.2;
        private const double MotorSizingMargin = 1.15;
        private const int MaxEvaluations = 1500;
        private const int MaxPasses = 80;

        private sealed class Limits
        {
            public double? MinEff, MaxNoise, MaxMotor, MaxTip;
        }

        private sealed class Var
        {
            public Func<DesignInput, double> Get = null!;
            public Action<DesignInput, double> Set = null!;
            public double Step, MinStep, Min, Max;
            public bool IsInt;
        }

        public static SizingSnapshot Evaluate(
            DesignInput source, BladeProfile? bladeProfile,
            List<CalibrationCase> calibration, List<CostRate> rates, bool resizeMotor)
        {
            var d = DesignFlowStore.CloneScalars(source);

            double chordMm = AeroCalcEngine.ComputeMeanChordMm(d.TipDiameterMm, d.HubRatio, d.BladeCount);
            var profile = BladeProfileEngine.ResolveProfileData(bladeProfile, chordMm);

            var aero = AeroCalcEngine.Calculate(d, profile, calibration);
            if (resizeMotor)
                d.MotorPowerKw = Math.Round(aero.ShaftPowerKw * MotorSizingMargin, 2);

            var structResult = StructCalcEngine.Calculate(d, aero);
            var sound = SoundCalcEngine.Calculate(d, aero);
            var bom = BomCostingEngine.Calculate(d, structResult, rates);

            var warnings = new List<string>();
            warnings.AddRange(aero.Warnings);
            warnings.AddRange(structResult.Warnings);
            warnings.AddRange(sound.Warnings);

            return new SizingSnapshot
            {
                Input = d,
                Aero = aero,
                Struct = structResult,
                Sound = sound,
                Cost = bom.GrandTotal,
                Warnings = warnings
            };
        }

        public static SizingOutcome Optimize(
            DesignInput baselineInput, BladeProfile? bladeProfile,
            List<CalibrationCase> calibration, List<CostRate> rates)
        {
            var limits = new Limits
            {
                MinEff = baselineInput.MinEfficiencyPct,
                MaxNoise = baselineInput.MaxNoiseDbA,
                MaxMotor = baselineInput.MaxMotorPowerKw,
                MaxTip = baselineInput.MaxTipDiameterMm
            };

            var baselineSnap = Evaluate(baselineInput, bladeProfile, calibration, rates, false);
            var current = Evaluate(baselineInput, bladeProfile, calibration, rates, true);
            double currentScore = Score(current, limits);
            double baselineScore = Score(baselineSnap, limits);
            int evals = 2;

            var vars = BuildVars(baselineInput, limits);

            for (int pass = 0; pass < MaxPasses && evals < MaxEvaluations; pass++)
            {
                bool improved = false;

                foreach (var v in vars)
                {
                    foreach (int dir in new[] { 1, -1 })
                    {
                        double now = v.Get(current.Input);
                        double next = Math.Clamp(now + dir * v.Step, v.Min, v.Max);
                        if (v.IsInt) next = Math.Round(next);
                        if (Math.Abs(next - now) < 1e-9) continue;

                        var trial = DesignFlowStore.CloneScalars(current.Input);
                        v.Set(trial, next);

                        SizingSnapshot snap;
                        try { snap = Evaluate(trial, bladeProfile, calibration, rates, true); }
                        catch { continue; }

                        evals++;
                        double score = Score(snap, limits);

                        if (score > currentScore + 1e-6)
                        {
                            current = snap;
                            currentScore = score;
                            improved = true;
                            break;
                        }
                    }
                }

                if (!improved)
                {
                    bool shrunk = false;
                    foreach (var v in vars)
                    {
                        if (v.Step > v.MinStep * 1.0001)
                        {
                            v.Step = Math.Max(v.MinStep, v.Step / 2.0);
                            shrunk = true;
                        }
                    }
                    if (!shrunk) break;
                }
            }

            return new SizingOutcome
            {
                Baseline = baselineSnap,
                Optimized = current,
                Improved = currentScore > baselineScore + 0.05,
                Feasible = Violation(current, limits) <= 0,
                Evaluations = evals
            };
        }

        private static List<Var> BuildVars(DesignInput b, Limits l)
        {
            double tipMax = l.MaxTip.HasValue ? Math.Min(1.3 * b.TipDiameterMm, l.MaxTip.Value) : 1.3 * b.TipDiameterMm;
            double tipMin = Math.Min(0.7 * b.TipDiameterMm, tipMax);
            double rpmMax = b.MaxSpeedRpm.HasValue ? b.MaxSpeedRpm.Value : Math.Round(1.5 * b.SpeedRpm);
            double rpmMin = Math.Min(Math.Max(300, 0.6 * b.SpeedRpm), rpmMax);

            return new List<Var>
            {
                new Var { Get = d => d.TipDiameterMm, Set = (d, x) => d.TipDiameterMm = x,
                          Step = Math.Max(5, 0.05 * b.TipDiameterMm), MinStep = 1, Min = tipMin, Max = tipMax },
                new Var { Get = d => d.HubRatio, Set = (d, x) => d.HubRatio = x,
                          Step = 0.04, MinStep = 0.005, Min = 0.30, Max = 0.75 },
                new Var { Get = d => d.BladeAngleDeg, Set = (d, x) => d.BladeAngleDeg = x,
                          Step = 2.0, MinStep = 0.25, Min = 8, Max = 45 },
                new Var { Get = d => d.BladeCount, Set = (d, x) => d.BladeCount = (int)x,
                          Step = 1, MinStep = 1, Min = 3, Max = 12, IsInt = true },
                new Var { Get = d => d.SpeedRpm, Set = (d, x) => d.SpeedRpm = (int)x,
                          Step = 50, MinStep = 10, Min = rpmMin, Max = rpmMax, IsInt = true }
            };
        }

        private static double Violation(SizingSnapshot s, Limits l)
        {
            double v = 0;
            double eff = s.Aero.OverallEfficiencyPct;

            if (eff <= 1.0) v += 1.0;
            if (!string.IsNullOrEmpty(s.Aero.Status) && s.Aero.Status.StartsWith("error")) v += 1.0;
            if (s.Struct.SafetyFactor < MinSafetyFactor) v += (MinSafetyFactor - s.Struct.SafetyFactor) / MinSafetyFactor;
            if (l.MinEff.HasValue && l.MinEff.Value > 0 && eff < l.MinEff.Value) v += (l.MinEff.Value - eff) / l.MinEff.Value;
            if (l.MaxNoise.HasValue && l.MaxNoise.Value > 0 && s.Sound.LpOverallDba > l.MaxNoise.Value)
                v += (s.Sound.LpOverallDba - l.MaxNoise.Value) / l.MaxNoise.Value;
            if (l.MaxMotor.HasValue && l.MaxMotor.Value > 0 && s.Input.MotorPowerKw > l.MaxMotor.Value)
                v += (s.Input.MotorPowerKw - l.MaxMotor.Value) / l.MaxMotor.Value;
            if (l.MaxTip.HasValue && l.MaxTip.Value > 0 && s.Input.TipDiameterMm > l.MaxTip.Value)
                v += (s.Input.TipDiameterMm - l.MaxTip.Value) / l.MaxTip.Value;

            return v;
        }

        private static double Score(SizingSnapshot s, Limits l)
        {
            return s.Aero.OverallEfficiencyPct - 300.0 * Violation(s, l) - 1e-5 * s.Cost;
        }

        public static DesignResult ToDesignResult(SizingSnapshot s)
        {
            var aero = s.Aero;
            var st = s.Struct;
            var sound = s.Sound;

            return new DesignResult
            {
                SpecificSpeed = Math.Round(aero.SpecificSpeed, 4),
                TipSpeedMs = Math.Round(aero.TipSpeedMs, 2),
                HubDiameterMm = Math.Round(aero.HubDiameterMm, 1),
                ChordLengthMm = Math.Round(aero.ChordLengthMm, 1),
                BladeSpanMm = Math.Round(aero.BladeSpanMm, 1),
                ShaftPowerKw = Math.Round(aero.ShaftPowerKw, 3),
                OverallEfficiencyPct = Math.Round(aero.OverallEfficiencyPct, 2),
                FlowCoefficient = Math.Round(aero.FlowCoefficient, 4),
                PressureCoefficient = Math.Round(aero.PressureCoefficient, 4),
                TipClearanceMm = aero.TipClearanceMm,

                BladeStressMpa = Math.Round(st.TotalStressMpa, 2),
                SafetyFactor = st.SafetyFactor,
                MaterialUsed = st.MaterialUsed,
                YieldStrengthMpa = Math.Round(st.YieldStrengthMpa, 1),

                OverallNoiseDbA = (float)sound.LpOverallDba,
                SoundPowerLevelDb = (float)sound.LwOverallDb,
                BladePassingFrequencyHz = (float)sound.BpfHz,
                TipMachNumber = (float)sound.TipMachNumber,
                NoiseRatingValue = sound.NrValue,
                NoiseRating = sound.NoiseRating,
                OctaveBandLwJson = JsonSerializer.Serialize(sound.OctaveBandLwDb),

                Status = s.Warnings.Count == 0 ? "ok" : "warning",
                WarningMessages = JsonSerializer.Serialize(s.Warnings),
                CalculatedAt = DateTime.UtcNow
            };
        }
    }
}