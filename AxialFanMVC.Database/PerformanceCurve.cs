using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AxialFanMVC.Database
{
    public class PerformanceCurve
    {
        [Key, Column("id")]
        public int Id { get; set; }

        [Column("design_result_id")]
        public int DesignResultId { get; set; }
        public string Source { get; set; } = "";

        public string Label { get; set; } = "";
        [Column("blade_angle_deg")] public double BladeAngleDeg { get; set; }
        [Column("speed_rpm")] public int SpeedRpm { get; set; }

        // Comma-separated double arrays (stored as mediumtext)
        [Column("q_values")] public string QValues { get; set; } = string.Empty;
        [Column("dp_values")] public string DpValues { get; set; } = string.Empty;
        [Column("eta_values")] public string EtaValues { get; set; } = string.Empty;
        [Column("kw_values")] public string KwValues { get; set; } = string.Empty;

        [Column("generated_at")]
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

        public DesignResult DesignResult { get; set; } = null!;
    }
}
