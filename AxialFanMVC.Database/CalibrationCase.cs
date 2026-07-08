using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AxialFanMVC.Database
{
   
        [Table("calibration_cases")]
        public class CalibrationCase
        {
            [Key, Column("id")]
            public int Id { get; set; }

            // ── Provenance — always know how trustworthy a case is ──
            [Required, MaxLength(30), Column("source_type")]
            public string SourceType { get; set; } = "Manufacturer Curve"; // Manufacturer Curve | CFD | Test Bench

            [MaxLength(300), Column("source_description")]
            public string? SourceDescription { get; set; } // e.g. "Acme AXF-500 catalog, 2023 edition, p.12"

            // ── Geometry snapshot (raw) ──
            [Column("tip_diameter_mm")]
            public double TipDiameterMm { get; set; }

            [Column("hub_ratio")]
            public double HubRatio { get; set; }

            [Column("blade_angle_deg")]
            public double BladeAngleDeg { get; set; }

            [Column("blade_count")]
            public int BladeCount { get; set; }

            [Column("speed_rpm")]
            public int SpeedRpm { get; set; }

            [Column("density_kg_m3")]
            public double DensityKgM3 { get; set; }

            [Column("temperature_celsius")]
            public double TemperatureCelsius { get; set; }

            // ── Blade profile snapshot ──
            [MaxLength(50), Column("blade_profile_designation")]
            public string? BladeProfileDesignation { get; set; } // null if unknown/not assigned

            [Column("max_camber_pct")]
            public double? MaxCamberPct { get; set; }

            [Column("max_thickness_pct")]
            public double? MaxThicknessPct { get; set; }

            // ── Derived features (computed once at capture time via
            //    PinnFeatureEngine, stored so training doesn't depend on
            //    re-deriving them consistently later) ──
            [Column("chord_length_mm")]
            public double ChordLengthMm { get; set; }

            [Column("tip_speed_ms")]
            public double TipSpeedMs { get; set; }

            [Column("flow_coefficient")]
            public double FlowCoefficient { get; set; }

            [Column("pressure_coefficient")]
            public double PressureCoefficient { get; set; }
            [Column("tip_mach_number")]
            public double TipMachNumber { get; set; }

            [Column("solidity")]
            public double Solidity { get; set; }

            [Column("reynolds_number")]
            public double ReynoldsNumber { get; set; }

            [Column("created_at")]
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

            public ICollection<CalibrationCasePoint> Points { get; set; } = new List<CalibrationCasePoint>();
        }

        
    [Table("calibration_case_points")]
    public class CalibrationCasePoint
    {
        [Key, Column("id")]
        public int Id { get; set; }

        [Column("calibration_case_id")]
        public int CalibrationCaseId { get; set; }

        [Column("flow_rate_m3s")]
        public double FlowRateM3s { get; set; }

        [Column("pressure_rise_pa")]
        public double PressureRisePa { get; set; }

        [Column("efficiency_pct")]
        public double EfficiencyPct { get; set; }

        [Column("power_kw")]
        public double? PowerKw { get; set; }

        [Column("flow_coefficient")]
        public double FlowCoefficient { get; set; }

        [Column("pressure_coefficient")]
        public double PressureCoefficient { get; set; }

        public CalibrationCase CalibrationCase { get; set; } = null!;
    }
}

