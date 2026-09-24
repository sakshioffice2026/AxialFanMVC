namespace AxialFanMVC.Services.AeroAi
{
    public sealed class DesignSizing
    {
        public string DutyClass { get; init; } = "Medium";
        public double MaxTipSpeedMs { get; init; }
        public double TotalPressurePa { get; init; }
        public double StaticPressurePa { get; init; }
        public double TipDiameterMm { get; init; }
        public double HubRatio { get; init; }
        public int BladeCount { get; init; }
        public double BladeAngleDeg { get; init; }
        public int SpeedRpm { get; init; }
        public double TargetEfficiencyPct { get; init; }
        public double MotorPowerKw { get; init; }
    }

    public static class DesignSizingEngine
    {
        private const double StaticToTotalRatio = 0.9;
        private const double MotorMargin = 1.15;

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
            public double HubRatio;
            public int BladeCount;
            public double BladeAngleDeg;
            public double TargetEfficiencyPct;
        }

        public static DesignSizing Size(double flowM3s, double totalPressurePa)
        {
            var duty = Classify(totalPressurePa);

            var tipDiameterMm = EstimateTipDiameterMm(flowM3s, duty.FaceVelocityMs);
            var speedRpm = EstimateSpeedRpm(tipDiameterMm, duty.MaxTipSpeedMs);

            var efficiency = Math.Clamp(duty.TargetEfficiencyPct / 100.0, 0.35, 0.95);
            var motorKw = flowM3s * totalPressurePa / efficiency * MotorMargin / 1000.0;

            return new DesignSizing
            {
                DutyClass = duty.Name,
                MaxTipSpeedMs = duty.MaxTipSpeedMs,
                TotalPressurePa = Math.Round(totalPressurePa, 1),
                StaticPressurePa = Math.Round(totalPressurePa * StaticToTotalRatio, 1),
                TipDiameterMm = tipDiameterMm,
                HubRatio = duty.HubRatio,
                BladeCount = duty.BladeCount,
                BladeAngleDeg = duty.BladeAngleDeg,
                SpeedRpm = speedRpm,
                TargetEfficiencyPct = duty.TargetEfficiencyPct,
                MotorPowerKw = Math.Round(motorKw, 2)
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
                    HubRatio = 0.58,
                    BladeCount = 12,
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
                    HubRatio = 0.35,
                    BladeCount = 4,
                    BladeAngleDeg = 16,
                    TargetEfficiencyPct = 78
                };
            }

            return new Duty
            {
                Name = "Medium",
                FaceVelocityMs = 9.5,
                MaxTipSpeedMs = 75,
                HubRatio = 0.45,
                BladeCount = 8,
                BladeAngleDeg = 24,
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

        private static int EstimateSpeedRpm(double tipDiameterMm, double maxTipSpeedMs)
        {
            var maxRpm = maxTipSpeedMs * 60.0 / (Math.PI * (tipDiameterMm / 1000.0));

            var best = 0;
            foreach (var speed in StandardSpeedsRpm)
            {
                if (speed <= maxRpm && speed > best)
                    best = speed;
            }

            if (best > 0)
                return best;

            return Math.Max(100, (int)(Math.Floor(maxRpm / 10.0) * 10.0));
        }
    }
}