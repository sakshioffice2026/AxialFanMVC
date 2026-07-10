using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AxialFanMVC.Database
{
    [Table("design_inputs")]
    public class DesignInput
    {
        [Key, Column("id")]
        public int Id { get; set; }

        [Column("project_id")]
        public int ProjectId { get; set; }

        [Column("blade_profile_id")]
        public int? BladeProfileId { get; set; }

        [MaxLength(50), Column("media_type")]
        public string MediaType { get; set; } = "Air (standard)";

        [Column("temperature_celsius")]
        public double TemperatureCelsius { get; set; } = 25;

        [Column("inlet_pressure_pa")]
        public double InletPressurePa { get; set; } = 101325;

        [Column("density_kg_m3")]
        public double DensityKgM3 { get; set; } = 1.204;

        [Column("altitude_m")]
        public double? AltitudeM { get; set; }

        [Column("atmospheric_pressure_kpa")]
        public double? AtmosphericPressureKPa { get; set; }

        [Column("relative_humidity_pct")]
        public double? RelativeHumidityPct { get; set; }

        [MaxLength(20), Column("direction")]
        public string? Direction { get; set; }

        [MaxLength(50), Column("installation_type")]
        public string? InstallationType { get; set; }

        [MaxLength(20), Column("duty")]
        public string? Duty { get; set; }

        [Column("frequency_hz")]
        public int? FrequencyHz { get; set; }

        [Column("max_tip_diameter_mm")]
        public double? MaxTipDiameterMm { get; set; }

        [Column("min_efficiency_pct")]
        public double? MinEfficiencyPct { get; set; }

        [Column("max_noise_dba")]
        public double? MaxNoiseDbA { get; set; }

        [Column("max_motor_power_kw")]
        public double? MaxMotorPowerKw { get; set; }

        [Column("preferred_blade_count")]
        public int? PreferredBladeCount { get; set; }

        [Column("max_speed_rpm")]
        public int? MaxSpeedRpm { get; set; }

        [Column("flow_rate_m3s")]
        public double FlowRateM3s { get; set; }

        [Column("static_pressure_pa")]
        public double StaticPressurePa { get; set; }

        [Column("total_pressure_pa")]
        public double TotalPressurePa { get; set; }

        [Column("speed_rpm")]
        public int SpeedRpm { get; set; } = 1450;

        [MaxLength(30), Column("motor_poles")]
        public string MotorPoles { get; set; } = "4-pole / 50 Hz";

        [MaxLength(50), Column("motor_type")]
        public string? MotorType { get; set; }

        [MaxLength(30), Column("voltage_spec")]
        public string? VoltageSpec { get; set; }

        [MaxLength(5), Column("insulation_class")]
        public string? InsulationClass { get; set; }

        [MaxLength(30), Column("starting_method")]
        public string? StartingMethod { get; set; }

        [Column("acc_inlet_guard")]
        public bool AccInletGuard { get; set; }

        [Column("acc_outlet_guard")]
        public bool AccOutletGuard { get; set; }

        [Column("acc_vibration_isolators")]
        public bool AccVibrationIsolators { get; set; }

        [Column("acc_flexible_connector")]
        public bool AccFlexibleConnector { get; set; }

        [Column("acc_silencer")]
        public bool AccSilencer { get; set; }

        [Column("acc_backdraft_damper")]
        public bool AccBackdraftDamper { get; set; }

        [MaxLength(1000), Column("accessory_notes")]
        public string? AccessoryNotes { get; set; }

        [Column("blade_count")]
        public int BladeCount { get; set; } = 6;

        [Column("tip_diameter_mm")]
        public double TipDiameterMm { get; set; } = 1000;

        [Column("hub_ratio")]
        public double HubRatio { get; set; } = 0.45;

        [Column("blade_angle_deg")]
        public double BladeAngleDeg { get; set; } = 22;

        [Column("target_efficiency_pct")]
        public double TargetEfficiencyPct { get; set; } = 82;

        [Column("motor_power_kw")]
        public double MotorPowerKw { get; set; } = 2.2;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("hub_diameter_mm")]
        public double? HubDiameterMm { get; set; }

        [Column("range_min_flow_m3h")]
        public double? RangeMinFlowM3h { get; set; }

        [Column("range_max_flow_m3h")]
        public double? RangeMaxFlowM3h { get; set; }

        [Column("range_min_pressure_pa")]
        public double? RangeMinPressurePa { get; set; }

        [Column("range_max_pressure_pa")]
        public double? RangeMaxPressurePa { get; set; }

        [Column("range_min_speed_rpm")]
        public int? RangeMinSpeedRpm { get; set; }

        [Column("range_max_speed_rpm")]
        public int? RangeMaxSpeedRpm { get; set; }

        [MaxLength(50), Column("drive_type")]
        public string? DriveType { get; set; }

        [Column("motor_rpm")]
        public int? MotorRpm { get; set; }

        [Column("fan_rpm")]
        public int? FanRpm { get; set; }

        [MaxLength(50), Column("belt_type")]
        public string? BeltType { get; set; }

        [Column("pulley_ratio")]
        public double? PulleyRatio { get; set; }

        [Column("number_of_belts")]
        public int? NumberOfBelts { get; set; }

        [Column("centre_distance_mm")]
        public double? CentreDistanceMm { get; set; }

        [Column("vfd_min_rpm")]
        public int? VfdMinRpm { get; set; }

        [Column("vfd_max_rpm")]
        public int? VfdMaxRpm { get; set; }

        [Column("vfd_speed_pct")]
        public double? VfdSpeedPct { get; set; }

        [Column("capacity_flow_m3h")]
        public double? CapacityFlowM3h { get; set; }

        [Column("capacity_static_pa")]
        public double? CapacityStaticPa { get; set; }

        [Column("capacity_speed_rpm")]
        public int? CapacitySpeedRpm { get; set; }

        [Column("capacity_motor_kw")]
        public double? CapacityMotorKw { get; set; }

        [Column("capacity_efficiency_pct")]
        public double? CapacityEfficiencyPct { get; set; }

        [MaxLength(100), Column("drawing_tag_no")]
        public string? DrawingTagNo { get; set; }

        [Column("nameplate_text", TypeName = "text")]


        public string? NameplateText { get; set; }

        public double? ReceiverDistanceM { get; set; }

        public string? AcousticEnvironment { get; set; }

        public double? DirectivityIndexDb { get; set; }

        public double? InletAttenuationDb { get; set; }

        public double? OutletAttenuationDb { get; set; }

        public double? CasingTransmissionLossDb { get; set; }

        public double? SilencerAttenuationDb { get; set; }

        public double? RoomCorrectionDb { get; set; }

        public double? BackgroundNoiseDbA { get; set; }

        public double ?SafetyMarginDb { get; set; }

        //design series
        [Column("design_series_id")]
        public int? DesignSeriesId { get; set; }

        public DesignSeries? DesignSeries { get; set; }
        public Project Project { get; set; } = null!;
        public BladeProfile? BladeProfile { get; set; }
        public DesignResult? DesignResult { get; set; }
    }
}