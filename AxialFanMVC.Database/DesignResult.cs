using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AxialFanMVC.Database
{
    public class DesignResult
    {
        [Key, Column("id")]
        public int Id { get; set; }

        [Column("design_input_id")]
        public int DesignInputId { get; set; }

        // Aerodynamic outputs
        [Column("specific_speed")] public double SpecificSpeed { get; set; }
        [Column("tip_speed_ms")] public double TipSpeedMs { get; set; }
        [Column("hub_diameter_mm")] public double HubDiameterMm { get; set; }
        [Column("chord_length_mm")] public double ChordLengthMm { get; set; }
        [Column("blade_span_mm")] public double BladeSpanMm { get; set; }
        [Column("shaft_power_kw")] public double ShaftPowerKw { get; set; }
        [Column("overall_efficiency_pct")] public double OverallEfficiencyPct { get; set; }
        [Column("flow_coefficient")] public double FlowCoefficient { get; set; }
        [Column("pressure_coefficient")] public double PressureCoefficient { get; set; }

        // Structural outputs
        [Column("tip_clearance_mm")] public double TipClearanceMm { get; set; } = 3;
        [Column("blade_stress_mpa")] public double BladeStressMpa { get; set; }
        [Column("safety_factor")] public double SafetyFactor { get; set; }

        [MaxLength(20), Column("status")]
        public string Status { get; set; } = "ok"; // ok | warning | error

        [Column("warning_messages")]
        public string? WarningMessages { get; set; } // JSON array

        [Column("calculated_at")]
        public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;

        // ===== Acoustic Results =====
        public double? OverallNoiseDbA { get; set; }

        public double? SoundPowerLevelDb { get; set; }

        public double? SpecificSoundLevelKs { get; set; }

        public double? BladePassingFrequencyHz { get; set; }

        public double? TipMachNumber { get; set; }

        public double? NoiseRatingValue { get; set; }

        public string? NoiseRating { get; set; }

        public string? OctaveBandLwJson { get; set; }


        public DesignInput DesignInput { get; set; } = null!;
        public ICollection<PerformanceCurve> PerformanceCurves { get; set; } = new List<PerformanceCurve>();
        public ICollection<Drawing> Drawings { get; set; } = new List<Drawing>();
    }
}
