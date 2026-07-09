using AxialFanMVC.Database;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class PerformanceCurve
{
    [Key, Column("id")]
    public int Id { get; set; }

    [Column("design_result_id")]
    public int DesignResultId { get; set; }

    [Column("source")]
    public string? Source { get; set; }              // Baseline | PINN | null for Manual

    [Column("label")]
    public string Label { get; set; } = "";

    [Column("blade_angle_deg")] public double BladeAngleDeg { get; set; }
    [Column("speed_rpm")] public int SpeedRpm { get; set; }

    [Column("q_values")] public string QValues { get; set; } = string.Empty;
    [Column("dp_values")] public string DpValues { get; set; } = string.Empty;
    [Column("eta_values")] public string EtaValues { get; set; } = string.Empty;
    [Column("kw_values")] public string KwValues { get; set; } = string.Empty;

    [Column("generated_at")]
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    // ── NEW: Feature 2 — origin tracking ──────────────────────
    [Column("origin_type")]
    [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
    public string? OriginType { get; set; }

    [Column("created_by_user_id")]
    public int? CreatedByUserId { get; set; }

    // ── NEW: Feature 1 — validation audit trail ───────────────
    [Column("validation_status")]
    public string ValidationStatus { get; set; } = "not_applicable"; // ok | corrected | flagged | not_applicable

    [Column("validation_flags_json")]
    public string? ValidationFlagsJson { get; set; }

    public DesignResult DesignResult { get; set; } = null!;
    public User? CreatedByUser { get; set; }
}