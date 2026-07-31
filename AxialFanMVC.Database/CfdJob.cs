using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AxialFanMVC.Database
{
    // Backs the async "Generate CFD" feature. DB-backed like OptimizationJob —
    // survives an app restart mid-run instead of silently losing a job the
    // user is waiting on.
    [Table("cfd_jobs")]
    public class CfdJob
    {
        [Key, Column("id")]
        public int Id { get; set; }

        [Column("result_id")]
        public int ResultId { get; set; }   // FK -> DesignResult, the run this CFD job visualizes

        [Column("user_id")]
        public int UserId { get; set; }

        // Queued -> Running -> Completed | Failed
        [Required, MaxLength(20), Column("status")]
        public string Status { get; set; } = "Queued";

        [Column("error_message")]
        public string? ErrorMessage { get; set; }

        // Relative path under wwwroot, e.g. "cfd-results/7/pressure_slice.png"
        [Column("png_path")]
        public string? PngPath { get; set; }

        [Column("vtp_path")]
        public string? VtpPath { get; set; }

        // Relative path under wwwroot, e.g. "cfd-results/7/streamlines.vtp".
        // Null when this run's seed produced no streamlines - an expected
        // outcome for some geometries, not a failure (see render_result.py).
        [Column("streamlines_vtp_path")]
        public string? StreamlinesVtpPath { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("started_at")]
        public DateTime? StartedAt { get; set; }

        [Column("completed_at")]
        public DateTime? CompletedAt { get; set; }
    }
}
