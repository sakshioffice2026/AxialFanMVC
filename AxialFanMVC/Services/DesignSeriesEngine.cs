using AxialFanMVC.Database;

namespace AxialFanMVC.Services
{
    // Generates scaled DesignInput variants from a base design, using
    // standard axial-fan affinity laws for geometrically similar fans:
    //   Flow    ∝ D³        (at constant RPM)
    //   Pressure ∝ D²·N²
    //   Power   ∝ D⁵·N³
    // RPM is held constant across the series (N ratio = 1), so pressure
    // scales with D² and power scales with D⁵.
    // Blade count, hub ratio, blade angle, target efficiency, and blade
    // profile are kept identical — that's what "geometrically similar"
    // means. Only size-dependent quantities are scaled.
    public static class DesignSeriesEngine
    {
        public static DesignInput GenerateVariant(DesignInput baseInput, int targetDiameterMm)
        {
            double ratio = (double)targetDiameterMm / baseInput.TipDiameterMm;

            var variant = new DesignInput
            {
                ProjectId = baseInput.ProjectId,
                BladeProfileId = baseInput.BladeProfileId,

                // Unchanged — operating environment stays the same across a series
                MediaType = baseInput.MediaType,
                TemperatureCelsius = baseInput.TemperatureCelsius,
                InletPressurePa = baseInput.InletPressurePa,
                DensityKgM3 = baseInput.DensityKgM3,
                AltitudeM = baseInput.AltitudeM,
                AtmosphericPressureKPa = baseInput.AtmosphericPressureKPa,
                RelativeHumidityPct = baseInput.RelativeHumidityPct,
                Direction = baseInput.Direction,
                InstallationType = baseInput.InstallationType,
                Duty = baseInput.Duty,
                FrequencyHz = baseInput.FrequencyHz,

                // Unchanged — geometrically similar means same proportions/profile
                BladeCount = baseInput.BladeCount,
                HubRatio = baseInput.HubRatio,
                BladeAngleDeg = baseInput.BladeAngleDeg,
                TargetEfficiencyPct = baseInput.TargetEfficiencyPct,

                // Scaled — size-dependent geometry
                TipDiameterMm = targetDiameterMm,
                HubDiameterMm = baseInput.HubDiameterMm.HasValue
                    ? Math.Round(baseInput.HubDiameterMm.Value * ratio, 1)
                    : Math.Round(targetDiameterMm * baseInput.HubRatio, 1),

                // Unchanged — RPM held constant across the series
                SpeedRpm = baseInput.SpeedRpm,
                MotorPoles = baseInput.MotorPoles,
                MotorType = baseInput.MotorType,
                VoltageSpec = baseInput.VoltageSpec,
                InsulationClass = baseInput.InsulationClass,
                StartingMethod = baseInput.StartingMethod,
                DriveType = baseInput.DriveType,
                // Carried along with DriveType: without these, a V-Belt/VFD
                // series variant would have DriveType set but none of the
                // fields the new drive-type resolution logic needs, and
                // would spuriously warn "not fully specified" on every
                // variant even though the base design's ratio/band is
                // known and unchanged across a geometrically-similar series.
                MotorRpm = baseInput.MotorRpm,
                FanRpm = baseInput.FanRpm,
                BeltType = baseInput.BeltType,
                PulleyRatio = baseInput.PulleyRatio,
                NumberOfBelts = baseInput.NumberOfBelts,
                CentreDistanceMm = baseInput.CentreDistanceMm,
                VfdMinRpm = baseInput.VfdMinRpm,
                VfdMaxRpm = baseInput.VfdMaxRpm,
                VfdSpeedPct = baseInput.VfdSpeedPct,

                // Scaled — affinity laws (RPM ratio = 1, so N-terms drop out)
                FlowRateM3s = Math.Round(baseInput.FlowRateM3s * Math.Pow(ratio, 3), 4),
                StaticPressurePa = Math.Round(baseInput.StaticPressurePa * Math.Pow(ratio, 2), 1),
                TotalPressurePa = Math.Round(baseInput.TotalPressurePa * Math.Pow(ratio, 2), 1),
                MotorPowerKw = Math.Round(baseInput.MotorPowerKw * Math.Pow(ratio, 5), 3),

                // Unchanged — accessories/constraints carry over as defaults;
                // user can edit the variant later before calculating it
                AccInletGuard = baseInput.AccInletGuard,
                AccOutletGuard = baseInput.AccOutletGuard,
                AccVibrationIsolators = baseInput.AccVibrationIsolators,
                AccFlexibleConnector = baseInput.AccFlexibleConnector,
                AccSilencer = baseInput.AccSilencer,
                AccBackdraftDamper = baseInput.AccBackdraftDamper,

                MaxTipDiameterMm = baseInput.MaxTipDiameterMm,
                MinEfficiencyPct = baseInput.MinEfficiencyPct,
                MaxNoiseDbA = baseInput.MaxNoiseDbA,
                MaxMotorPowerKw = baseInput.MaxMotorPowerKw,
                PreferredBladeCount = baseInput.PreferredBladeCount,
                MaxSpeedRpm = baseInput.MaxSpeedRpm,

                CreatedAt = DateTime.UtcNow

                // NOTE: DesignSeriesId is intentionally NOT set here —
                // the calling controller sets it after the DesignSeries
                // row itself has been saved and has a real Id.
            };

            return variant;
        }
    }
}