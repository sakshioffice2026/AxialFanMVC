using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AxialFanMVC.Database
{
    public class BomLineItem
    {
        [Key, Column("id")]
        public int Id { get; set; }

        [Column("design_result_id")]
        public int DesignResultId { get; set; }

        [Column("source")]
        public string Source { get; set; } = "Auto";  // Auto | Manual

        [Column("category")]
        public string Category { get; set; } = "";

        [Column("description")]
        public string Description { get; set; } = "";

        [Column("quantity")]
        public double Quantity { get; set; }

        [Column("unit")]
        public string? Unit { get; set; }

        [Column("unit_cost")]
        public double UnitCost { get; set; }

        [Column("line_total")]
        public double LineTotal { get; set; }

        [Column("sort_order")]
        public int SortOrder { get; set; }

        [Column("created_by_user_id")]
        public int? CreatedByUserId { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DesignResult DesignResult { get; set; } = null!;
        public User? CreatedByUser { get; set; }
    }
}