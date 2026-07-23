using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AxialFanMVC.Database
{
    // Backs the async "Optimize for me" feature. DB-backed (not an in-memory
    // queue) deliberately — an in-memory Channel<T> job queue loses every
    // in-flight job on app restart/deploy, which is unacceptable for a
    // request the user is actively waiting on. This survives restarts and
    // is the same repository+EF pattern already used for every other
    // entity in this codebase, so no new persistence approach is introduced.
    [Table("optimization_jobs")]
    public class OptimizationJob
    {
        [Key, Column("id")]
        public int Id { get; set; }

        [Column("project_id")]
        public int ProjectId { get; set; }

        [Column("user_id")]
        public int UserId { get; set; }

        // Queued -> Running -> Completed | Failed
        [Required, MaxLength(20), Column("status")]
        public string Status { get; set; } = "Queued";

        // The constraint request, serialized — MinEfficiencyPct, MaxNoiseDbA,
        // MaxMotorPowerKw, MaxTipDiameterMm, and the fixed duty point
        // (FlowRateM3s, TotalPressurePa, TemperatureCelsius) it was solved for.
        [Column("request_json", TypeName = "text")]
        public string RequestJson { get; set; } = "";

        // Three candidates (Budget / Silent / Premium), serialized. Null until Completed.
        [Column("result_json", TypeName = "text")]
        public string? ResultJson { get; set; }

        [Column("error_message")]
        public string? ErrorMessage { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("started_at")]
        public DateTime? StartedAt { get; set; }

        [Column("completed_at")]
        public DateTime? CompletedAt { get; set; }
    }
}