using System.Globalization;
using System.Text.RegularExpressions;
using AxialFanMVC.Database;

namespace AxialFanMVC.Services.AeroAi
{
    public sealed class OptimizationChange
    {
        public string Parameter { get; init; } = string.Empty;

        public string Before { get; init; } = string.Empty;

        public string After { get; init; } = string.Empty;

        public string Reason { get; init; } = string.Empty;
    }

    public sealed class OptimizationOutcome
    {
        public bool Success { get; init; }

        public string Error { get; init; } = string.Empty;

        public DesignRunParameters BaseParameters { get; init; } = new();

        public DesignPreviewResult BasePreview { get; init; } = new();

        public DesignRunParameters Parameters { get; init; } = new();

        public DesignPreviewResult Preview { get; init; } = new();

        public List<OptimizationChange> Changes { get; init; } = new();

        // Warnings present before and gone after.
        public List<string> Resolved { get; init; } = new();

        // Warnings still present after.
        public List<string> Remaining { get; init; } = new();

        // Warnings that only appear after the changes.
        public List<string> Introduced { get; init; } = new();
    }

    // Deterministic optimizer: every change is verified against the same physics engines
    // (in memory, nothing written to MySQL). Rules run in a fixed order:
    // 1) even blade count -> odd, 2) direct-drive speed snap, 3) stall, 4) runout, 5) motor sizing.
    public sealed class DesignOptimizerEngine
    {
        private const double StallLimit = 0.15;
        private const double StallTarget = 0.16;
        private const double MotorMargin = 1.15;
        private const int MaxStallTrials = 4;
        private const int MaxRunoutSteps = 12;
        private const double MaxBladeAngleStepDeg = 0.5;
        private const int MaxSpeedTrials = 6;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private static readonly double[] StandardDiametersMm =
        {
            300, 350, 400, 450, 500, 560, 630, 710, 800, 850, 900, 950,
            1000, 1060, 1120, 1250, 1400, 1600, 1800, 2000, 2240, 2500, 2800, 3150
        };

        // 50 Hz synchronous-less-slip speeds: 2, 4, 6, 8 pole.
        private static readonly int[] StandardDirectDriveRpm = { 2900, 1450 };

        // Engine warns when a direct-drive fan runs >10% below the 2/4-pole synchronous speed.
        private static readonly int[] SynchronousRpm = { 3000, 1500 };

        private static readonly double[] StandardMotorsKw =
        {
            0.75, 1.1, 1.5, 2.2, 3.0, 4.0, 5.5, 7.5, 11, 15, 18.5, 22, 30, 37, 45, 55, 75, 90, 110, 132, 160, 200, 250
        };

        private readonly DesignPreviewService _preview;

        public DesignOptimizerEngine(DesignPreviewService preview)
        {
            _preview = preview;
        }

        public static DesignRunParameters FromDesignInput(DesignInput d)
        {
            return new DesignRunParameters
            {
                ProjectId = d.ProjectId,
                FlowRateM3s = d.FlowRateM3s,
                TotalPressurePa = d.TotalPressurePa,
                StaticPressurePa = d.StaticPressurePa,
                SpeedRpm = d.SpeedRpm,
                BladeCount = d.BladeCount,
                TipDiameterMm = d.TipDiameterMm,
                TemperatureCelsius = d.TemperatureCelsius,
                HubRatio = d.HubRatio,
                BladeAngleDeg = d.BladeAngleDeg,
                TargetEfficiencyPct = d.TargetEfficiencyPct,
                MotorPowerKw = d.MotorPowerKw,
                BladeMaterial = d.BladeMaterial,
                DensityKgM3 = d.DensityKgM3,
                AltitudeM = d.AltitudeM,
                AtmosphericPressureKPa = d.AtmosphericPressureKPa,
                MaxTipDiameterMm = d.MaxTipDiameterMm,
                PreferredBladeCount = d.PreferredBladeCount,
                DriveType = d.DriveType
            };
        }

        public async Task<OptimizationOutcome> OptimizeAsync(DesignRunParameters baseParameters, CancellationToken ct)
        {
            var baseline = await _preview.PreviewAsync(baseParameters, ct);
            if (!baseline.Success)
                return new OptimizationOutcome { Success = false, Error = baseline.Error };

            var current = Copy(baseParameters);
            var currentPreview = baseline;
            var changes = new List<OptimizationChange>();

            // 1) Blade count: even -> odd (fixes blade-passing-frequency noise).
            if (current.BladeCount % 2 == 0)
            {
                var candidate = Copy(current);
                candidate.BladeCount = current.BladeCount > 5 ? current.BladeCount - 1 : current.BladeCount + 1;

                var result = await _preview.PreviewAsync(candidate, ct);
                if (result.Success)
                {
                    changes.Add(new OptimizationChange
                    {
                        Parameter = "Blades",
                        Before = current.BladeCount.ToString(Inv) + " (even)",
                        After = candidate.BladeCount.ToString(Inv) + " (odd)",
                        Reason = "Odd blade count avoids coincident blade-passing harmonics and lowers tonal noise."
                    });

                    current = candidate;
                    currentPreview = result;
                }
            }

            // 2) Speed standardisation: direct-drive fans snap to a standard synchronous speed,
            //    with the tip diameter rescaled (D ~ 1/N at constant pressure).
            if (IsDirectDrive(current) && !IsStandardSpeed(current.SpeedRpm))
            {
                var baseWarnings = CountWarnings(currentPreview);
                DesignRunParameters? bestSpeed = null;
                DesignPreviewResult? bestSpeedPreview = null;
                var trials = 0;

                foreach (var rpm in StandardDirectDriveRpm.OrderBy(r => Math.Abs(r - current.SpeedRpm)))
                {
                    var scaled = current.TipDiameterMm * current.SpeedRpm / (double)rpm;
                    var diameters = StandardDiametersMm
                        .Where(d => !(current.MaxTipDiameterMm is > 0) || d <= current.MaxTipDiameterMm.Value)
                        .OrderBy(d => Math.Abs(d - scaled))
                        .Take(2);

                    foreach (var diameter in diameters)
                    {
                        if (trials++ >= MaxSpeedTrials)
                            break;

                        var candidate = Copy(current);
                        candidate.SpeedRpm = rpm;
                        candidate.TipDiameterMm = diameter;

                        var result = await _preview.PreviewAsync(candidate, ct);
                        if (!result.Success)
                            continue;

                        var better = bestSpeedPreview is null ||
                                     CountWarnings(result) < CountWarnings(bestSpeedPreview) ||
                                     (CountWarnings(result) == CountWarnings(bestSpeedPreview) &&
                                      (result.OverallEfficiencyPct ?? 0) > (bestSpeedPreview.OverallEfficiencyPct ?? 0));

                        if (better)
                        {
                            bestSpeed = candidate;
                            bestSpeedPreview = result;
                        }
                    }

                    if (trials >= MaxSpeedTrials)
                        break;
                }

                if (bestSpeed is not null && bestSpeedPreview is not null &&
                    CountWarnings(bestSpeedPreview) <= baseWarnings)
                {
                    changes.Add(new OptimizationChange
                    {
                        Parameter = "Fan speed",
                        Before = current.SpeedRpm.ToString(Inv) + " RPM",
                        After = bestSpeed.SpeedRpm.ToString(Inv) + " RPM",
                        Reason = "Snapped to a standard synchronous direct-drive speed."
                    });

                    if (Math.Abs(bestSpeed.TipDiameterMm - current.TipDiameterMm) > 0.5)
                    {
                        changes.Add(new OptimizationChange
                        {
                            Parameter = "Tip diameter",
                            Before = current.TipDiameterMm.ToString("0", Inv) + " mm",
                            After = bestSpeed.TipDiameterMm.ToString("0", Inv) + " mm",
                            Reason = "Resized for the standard-speed velocity triangle."
                        });
                    }

                    current = bestSpeed;
                    currentPreview = bestSpeedPreview;
                }
            }

            // 3) Stall: lift the flow coefficient above the limit by trying neighbouring standard diameters.
            if (NeedsStallFix(currentPreview))
            {
                var startPhi = currentPreview.FlowCoefficient ?? 0.0;
                DesignRunParameters? best = null;
                DesignPreviewResult? bestPreview = null;
                var bestPhi = startPhi;
                var trials = 0;

                foreach (var diameter in NeighbourDiameters(current))
                {
                    if (trials++ >= MaxStallTrials)
                        break;

                    var candidate = Copy(current);
                    candidate.TipDiameterMm = diameter;

                    var result = await _preview.PreviewAsync(candidate, ct);
                    if (!result.Success || !result.FlowCoefficient.HasValue)
                        continue;

                    var phi = result.FlowCoefficient.Value;

                    if (phi > bestPhi && !HasRunout(result))
                    {
                        best = candidate;
                        bestPreview = result;
                        bestPhi = phi;
                    }

                    if (phi >= StallTarget && !HasRunout(result))
                        break;
                }

                if (best is not null && bestPreview is not null && bestPhi > startPhi * 1.02)
                {
                    changes.Add(new OptimizationChange
                    {
                        Parameter = "Tip diameter",
                        Before = current.TipDiameterMm.ToString("0", Inv) + " mm",
                        After = best.TipDiameterMm.ToString("0", Inv) + " mm",
                        Reason = "Flow coefficient " + startPhi.ToString("0.000", Inv) + " to " +
                                 bestPhi.ToString("0.000", Inv) +
                                 (bestPhi >= StallLimit ? ", clear of the stall limit." : ", still below the stall limit.")
                    });

                    current = best;
                    currentPreview = bestPreview;
                }
            }

            // 4) Runout: raise blade angle in 0.5 degree steps until the shortfall warning clears.
            if (HasRunout(currentPreview))
            {
                var startAngle = current.BladeAngleDeg ?? 0.0;
                var candidate = Copy(current);
                var candidatePreview = currentPreview;

                for (var step = 0; step < MaxRunoutSteps && HasRunout(candidatePreview); step++)
                {
                    candidate.BladeAngleDeg = Math.Round((candidate.BladeAngleDeg ?? startAngle) + MaxBladeAngleStepDeg, 1);

                    var result = await _preview.PreviewAsync(candidate, ct);
                    if (!result.Success)
                        break;

                    candidatePreview = result;
                }

                if ((candidate.BladeAngleDeg ?? startAngle) > startAngle && !HasRunout(candidatePreview))
                {
                    changes.Add(new OptimizationChange
                    {
                        Parameter = "Blade angle",
                        Before = startAngle.ToString("0.#", Inv) + "°",
                        After = (candidate.BladeAngleDeg ?? startAngle).ToString("0.#", Inv) + "°",
                        Reason = "Higher blade angle restores the requested pressure at this flow (runout cleared)."
                    });

                    current = candidate;
                    currentPreview = candidatePreview;
                }
            }

            // 5) Motor: size to shaft power plus margin, next standard IEC frame.
            var shaftKw = currentPreview.ShaftPowerKw ?? 0.0;
            var requiredKw = shaftKw * MotorMargin;
            var motorKw = current.MotorPowerKw ?? 0.0;

            if (shaftKw > 0 && motorKw < requiredKw)
            {
                var candidate = Copy(current);
                candidate.MotorPowerKw = NextStandardMotor(requiredKw);

                var result = await _preview.PreviewAsync(candidate, ct);
                if (result.Success)
                {
                    changes.Add(new OptimizationChange
                    {
                        Parameter = "Motor",
                        Before = motorKw.ToString("0.##", Inv) + " kW",
                        After = (candidate.MotorPowerKw ?? 0).ToString("0.##", Inv) + " kW",
                        Reason = "Sized for " + shaftKw.ToString("0.00", Inv) + " kW shaft power plus 15% margin."
                    });

                    current = candidate;
                    currentPreview = result;
                }
            }

            var before = WarningMap(baseline.Warnings);
            var after = WarningMap(currentPreview.Warnings);

            return new OptimizationOutcome
            {
                Success = true,
                BaseParameters = baseParameters,
                BasePreview = baseline,
                Parameters = current,
                Preview = currentPreview,
                Changes = changes,
                Resolved = before.Where(kv => !after.ContainsKey(kv.Key)).Select(kv => kv.Value).ToList(),
                Remaining = after.Where(kv => before.ContainsKey(kv.Key)).Select(kv => kv.Value).ToList(),
                Introduced = after.Where(kv => !before.ContainsKey(kv.Key)).Select(kv => kv.Value).ToList()
            };
        }

        private static bool IsDirectDrive(DesignRunParameters p)
        {
            return string.IsNullOrWhiteSpace(p.DriveType) ||
                   p.DriveType.Contains("Direct", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsStandardSpeed(int rpm)
        {
            return SynchronousRpm.Any(s => rpm >= s * 0.9 && rpm <= s * 1.05);
        }

        private static int CountWarnings(DesignPreviewResult r)
        {
            return WarningMap(r.Warnings).Count;
        }

        private static bool NeedsStallFix(DesignPreviewResult r)
        {
            return (r.FlowCoefficient.HasValue && r.FlowCoefficient.Value < StallLimit) ||
                   r.Warnings.Any(w => w.Contains("stall", StringComparison.OrdinalIgnoreCase));
        }

        private static bool HasRunout(DesignPreviewResult r)
        {
            return r.Warnings.Any(w => w.Contains("runout", StringComparison.OrdinalIgnoreCase));
        }

        // Standard diameters near the current size, closest first, honouring the casing limit.
        private static IEnumerable<double> NeighbourDiameters(DesignRunParameters p)
        {
            var current = p.TipDiameterMm;
            var limit = p.MaxTipDiameterMm is > 0 ? p.MaxTipDiameterMm.Value : double.MaxValue;

            return StandardDiametersMm
                .Where(d => Math.Abs(d - current) > 0.5 && d >= current * 0.7 && d <= current * 1.3 && d <= limit)
                .OrderBy(d => Math.Abs(d - current))
                .ThenBy(d => d);
        }

        private static double NextStandardMotor(double requiredKw)
        {
            foreach (var size in StandardMotorsKw)
            {
                if (size >= requiredKw)
                    return size;
            }

            return Math.Ceiling(requiredKw / 10.0) * 10.0;
        }

        // Keyed without digits so the same warning with different numbers still matches.
        private static Dictionary<string, string> WarningMap(IEnumerable<string> warnings)
        {
            var map = new Dictionary<string, string>();

            foreach (var w in warnings)
            {
                if (string.IsNullOrWhiteSpace(w) || w.StartsWith("Info", StringComparison.OrdinalIgnoreCase))
                    continue;

                var key = Regex.Replace(w.ToLowerInvariant(), @"[\d.]+", "#");
                if (key.Length > 70)
                    key = key.Substring(0, 70);

                map.TryAdd(key, w);
            }

            return map;
        }

        private static DesignRunParameters Copy(DesignRunParameters p)
        {
            return new DesignRunParameters
            {
                ProjectId = p.ProjectId,
                FlowRateM3s = p.FlowRateM3s,
                TotalPressurePa = p.TotalPressurePa,
                StaticPressurePa = p.StaticPressurePa,
                SpeedRpm = p.SpeedRpm,
                BladeCount = p.BladeCount,
                TipDiameterMm = p.TipDiameterMm,
                TemperatureCelsius = p.TemperatureCelsius,
                HubRatio = p.HubRatio,
                BladeAngleDeg = p.BladeAngleDeg,
                TargetEfficiencyPct = p.TargetEfficiencyPct,
                MotorPowerKw = p.MotorPowerKw,
                ApplicationDescription = p.ApplicationDescription,
                PressureClass = p.PressureClass,
                BladeMaterial = p.BladeMaterial,
                DensityKgM3 = p.DensityKgM3,
                AltitudeM = p.AltitudeM,
                AtmosphericPressureKPa = p.AtmosphericPressureKPa,
                MaxTipDiameterMm = p.MaxTipDiameterMm,
                PreferredBladeCount = p.PreferredBladeCount,
                DriveType = p.DriveType
            };
        }
    }
}