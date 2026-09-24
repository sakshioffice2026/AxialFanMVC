namespace AxialFanMVC.Services.AeroAi
{
    public sealed class DesignSizing
    {
        public bool IsCustom { get; init; }
        public string DutyClass { get; init; } = "Medium";
        public string Material { get; init; } = CustomOptionsParser.MatAluminum6061;
        public string DriveType { get; init; } = CustomOptionsParser.DriveDirect;
        public double MaxTipSpeedMs { get; init; }
        public double TipSpeedMs { get; init; }
        public double TotalPressurePa { get; init; }
        public double StaticPressurePa { get; init; }
        public double UnconstrainedDiameterMm { get; init; }
        public double TipDiameterMm { get; init; }
        public bool DiameterConstrained { get; init; }
        public double HubRatio { get; init; }
        public int BladeCount { get; init; }
        public double BladeAngleDeg { get; init; }
        public int SpeedRpm { get; init; }
        public double TargetEfficiencyPct { get; init; }
        public double MotorPowerKw { get; init; }
        public double DensityKgM3 { get; init; } = 1.2;
        public double AltitudeM { get; init; }
        public double TemperatureC { get; init; } = 25.0;
        public double AtmosphericPressureKPa { get; init; } = 101.325;
        public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
    }

    public static class DesignSizingEngine
    {
        private const double StaticToTotalRatio = 0.9;
        private const double MotorMargin = 1.15;
        private const double HardTipSpeedCapMs = 100.0;
        private const double ConstrainedTipSpeedFactor = 1.35;
        private const double DirectDriveOvershoot = 1.15;

        private static readonly double[] StandardDiametersMm =
        {
            300, 350, 400, 450, 500, 560, 630, 710, 800, 900,
            1000, 1120, 1250, 1400, 1600, 1800, 2000, 2240, 2500, 2800, 3150
        };

        private static readonly int[] StandardSpeedsRpm =
        {
            480, 580, 720, 960, 1000, 1450, 1500, 1750, 2900, 2950, 3000, 3500
        };

        private sealed class Duty
        {
            public string Name = "Medium";
            public double FaceVelocityMs;
            public double MaxTipSpeedMs;
            public double PressureCoefficient;
            public double HubRatio;
            public int BladeCount;
            public double BladeAngleDeg;
            public double TargetEfficiencyPct;
        }

        // Default path: pass no options. Custom path: pass the collected options.
        public static DesignSizing Size(double flowM3s, double totalPressurePa, CustomOptions? options = null)
        {
            var opt = options ?? new CustomOptions();
            var isCustom = options is not null && options.HasAny;
            var notes = new List<string>();

            // Air state
            var temperatureC = opt.TemperatureC ?? 25.0;
            var altitudeM = opt.AltitudeM ?? 0.0;
            var atmosphericKPa = 101.325 * Math.Pow(1.0 - 2.25577e-5 * altitudeM, 5.25588);
            var density = atmosphericKPa * 1000.0 / (287.05 * (temperatureC + 273.15));

            if (opt.AltitudeM.HasValue || opt.TemperatureC.HasValue)
            {
                notes.Add(
                    "Site air density is " + density.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) +
                    " kg/m³ (altitude " + altitudeM.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) +
                    " m, " + temperatureC.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " °C).");
            }

            var duty = Classify(totalPressurePa);

            // Diameter
            var freeDiameterMm = EstimateTipDiameterMm(flowM3s, duty.FaceVelocityMs);
            var diameterMm = freeDiameterMm;
            var constrained = false;

            if (opt.MaxTipDiameterMm.HasValue && diameterMm > opt.MaxTipDiameterMm.Value)
            {
                diameterMm = LargestStandardAtOrBelow(opt.MaxTipDiameterMm.Value);
                constrained = true;
            }

            var diameterRatio = freeDiameterMm / diameterMm;
            var circumference = Math.PI * diameterMm / 1000.0;

            // Speed needed to develop the pressure at this diameter and density
            var requiredTipSpeed = Math.Sqrt(2.0 * totalPressurePa / (density * duty.PressureCoefficient));
            var requiredRpm = requiredTipSpeed * 60.0 / circumference;

            var capTipSpeed = constrained
                ? Math.Min(duty.MaxTipSpeedMs * ConstrainedTipSpeedFactor, HardTipSpeedCapMs)
                : duty.MaxTipSpeedMs;

            var capRpm = capTipSpeed * 60.0 / circumference;

            // Speed and drive
            int speedRpm;
            string drive;
            var speedLimited = false;

            if (opt.SpeedRpm.HasValue)
            {
                speedRpm = opt.SpeedRpm.Value;
                drive = opt.DriveType
                        ?? (Array.IndexOf(StandardSpeedsRpm, speedRpm) >= 0
                            ? CustomOptionsParser.DriveDirect
                            : CustomOptionsParser.DriveVfd);

                if (speedRpm > capRpm * 1.0001)
                    notes.Add("Requested speed exceeds the tip-speed limit for this diameter; review noise and blade stress.");
            }
            else
            {
                var target = requiredRpm;
                if (target > capRpm)
                {
                    target = capRpm;
                    speedLimited = true;
                }

                drive = opt.DriveType ?? string.Empty;

                if (drive == CustomOptionsParser.DriveDirect)
                {
                    var std = SmallestStandardAtOrAbove(target, capRpm);
                    speedRpm = std > 0 ? std : HighestStandardAtOrBelow(capRpm, target);
                }
                else if (drive == CustomOptionsParser.DriveVfd || drive == CustomOptionsParser.DriveVBelt)
                {
                    speedRpm = VariableSpeed(target, capRpm);
                }
                else
                {
                    var std = SmallestStandardAtOrAbove(target, capRpm);

                    if (std > 0 && std <= target * DirectDriveOvershoot)
                    {
                        speedRpm = std;
                        drive = CustomOptionsParser.DriveDirect;
                    }
                    else
                    {
                        speedRpm = VariableSpeed(target, capRpm);
                        drive = CustomOptionsParser.DriveVfd;
                    }
                }
            }

            speedRpm = Math.Max(100, speedRpm);

            var tipSpeed = circumference * speedRpm / 60.0;

            if (constrained)
            {
                notes.Add(
                    "Diameter limited to " + diameterMm.ToString("0", System.Globalization.CultureInfo.InvariantCulture) +
                    " mm (unconstrained size " + freeDiameterMm.ToString("0", System.Globalization.CultureInfo.InvariantCulture) +
                    " mm): speed raised to " + speedRpm.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    " RPM and blade angle increased to hold the pressure.");
            }

            if (speedLimited)
                notes.Add("The speed needed for this pressure exceeds the tip-speed limit; the delivered pressure may fall short.");

            if (tipSpeed > duty.MaxTipSpeedMs * 1.0001)
            {
                notes.Add(
                    "Tip speed " + tipSpeed.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
                    " m/s is above the " + duty.MaxTipSpeedMs.ToString("0", System.Globalization.CultureInfo.InvariantCulture) +
                    " m/s design target; consider noise attenuation.");
            }

            // Geometry
            var bladeCount = opt.BladeCount ?? duty.BladeCount;

            var bladeAngle = Math.Clamp(
                duty.BladeAngleDeg * (1.0 + 0.5 * (diameterRatio - 1.0)),
                duty.BladeAngleDeg,
                duty.BladeAngleDeg + 8.0);

            // Efficiency and motor
            var penaltyPct = Math.Clamp((tipSpeed - duty.MaxTipSpeedMs) / duty.MaxTipSpeedMs * 20.0, 0.0, 8.0);
            var targetEfficiencyPct = Math.Round(duty.TargetEfficiencyPct - penaltyPct, 1);
            var efficiency = Math.Clamp(targetEfficiencyPct / 100.0, 0.35, 0.95);
            var motorKw = flowM3s * totalPressurePa / efficiency * MotorMargin / 1000.0;

            return new DesignSizing
            {
                IsCustom = isCustom,
                DutyClass = duty.Name,
                Material = string.IsNullOrEmpty(opt.Material) ? CustomOptionsParser.MatAluminum6061 : opt.Material,
                DriveType = drive,
                MaxTipSpeedMs = duty.MaxTipSpeedMs,
                TipSpeedMs = Math.Round(tipSpeed, 1),
                TotalPressurePa = Math.Round(totalPressurePa, 1),
                StaticPressurePa = Math.Round(totalPressurePa * StaticToTotalRatio, 1),
                UnconstrainedDiameterMm = freeDiameterMm,
                TipDiameterMm = diameterMm,
                DiameterConstrained = constrained,
                HubRatio = duty.HubRatio,
                BladeCount = bladeCount,
                BladeAngleDeg = Math.Round(bladeAngle, 1),
                SpeedRpm = speedRpm,
                TargetEfficiencyPct = targetEfficiencyPct,
                MotorPowerKw = Math.Round(motorKw, 2),
                DensityKgM3 = Math.Round(density, 4),
                AltitudeM = altitudeM,
                TemperatureC = temperatureC,
                AtmosphericPressureKPa = Math.Round(atmosphericKPa, 3),
                Notes = notes
            };
        }

        private static Duty Classify(double totalPressurePa)
        {
            if (totalPressurePa > 800)
            {
                return new Duty
                {
                    Name = "High",
                    FaceVelocityMs = 7.5,
                    MaxTipSpeedMs = 65,
                    PressureCoefficient = 0.35,
                    HubRatio = 0.58,
                    BladeCount = 11,
                    BladeAngleDeg = 34,
                    TargetEfficiencyPct = 72
                };
            }

            if (totalPressurePa < 300)
            {
                return new Duty
                {
                    Name = "Low",
                    FaceVelocityMs = 11.5,
                    MaxTipSpeedMs = 80,
                    PressureCoefficient = 0.18,
                    HubRatio = 0.35,
                    BladeCount = 5,
                    BladeAngleDeg = 16,
                    TargetEfficiencyPct = 78
                };
            }

            return new Duty
            {
                Name = "Medium",
                FaceVelocityMs = 9.5,
                MaxTipSpeedMs = 75,
                PressureCoefficient = 0.25,
                HubRatio = 0.45,
                BladeCount = 7,
                BladeAngleDeg = 22.5,
                TargetEfficiencyPct = 82
            };
        }

        private static double EstimateTipDiameterMm(double flowM3s, double faceVelocityMs)
        {
            var area = flowM3s / faceVelocityMs;
            var diameterMm = Math.Sqrt(4.0 * area / Math.PI) * 1000.0;

            foreach (var size in StandardDiametersMm)
            {
                if (size >= diameterMm)
                    return size;
            }

            return Math.Round(diameterMm / 50.0) * 50.0;
        }

        private static double LargestStandardAtOrBelow(double maxMm)
        {
            var best = 0.0;

            foreach (var size in StandardDiametersMm)
            {
                if (size <= maxMm && size > best)
                    best = size;
            }

            return best > 0 ? best : Math.Floor(maxMm / 10.0) * 10.0;
        }

        private static int SmallestStandardAtOrAbove(double targetRpm, double capRpm)
        {
            var best = 0;

            foreach (var speed in StandardSpeedsRpm)
            {
                if (speed >= targetRpm && speed <= capRpm && (best == 0 || speed < best))
                    best = speed;
            }

            return best;
        }

        private static int HighestStandardAtOrBelow(double capRpm, double fallbackRpm)
        {
            var best = 0;

            foreach (var speed in StandardSpeedsRpm)
            {
                if (speed <= capRpm && speed > best)
                    best = speed;
            }

            return best > 0 ? best : (int)(Math.Floor(fallbackRpm / 10.0) * 10.0);
        }

        private static int VariableSpeed(double targetRpm, double capRpm)
        {
            var rounded = Math.Ceiling(targetRpm / 10.0) * 10.0;
            var ceiling = Math.Floor(capRpm / 10.0) * 10.0;

            return (int)Math.Min(rounded, Math.Max(ceiling, 100.0));
        }
    }
}