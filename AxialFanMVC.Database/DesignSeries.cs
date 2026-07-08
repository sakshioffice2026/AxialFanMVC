using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AxialFanMVC.Database
{
    [Table("design_series")]
    public class DesignSeries
    {
        [Key, Column("id")]
        public int Id { get; set; }

        [Column("project_id")]
        public int ProjectId { get; set; }

        [Column("base_design_input_id")]
        public int BaseDesignInputId { get; set; }

        [Required, MaxLength(200), Column("name")]
        public string Name { get; set; } = string.Empty;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Project Project { get; set; } = null!;
        public DesignInput BaseDesignInput { get; set; } = null!;
        public ICollection<DesignInput> Variants { get; set; } = new List<DesignInput>();
    }
}