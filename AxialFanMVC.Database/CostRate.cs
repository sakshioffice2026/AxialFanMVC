using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AxialFanMVC.Database
{
    public class CostRate
    {
        [Key, Column("id")]
        public int Id { get; set; }

        [Column("category")]
        public string Category { get; set; } = "";   // Material | Motor | Drive | Misc

        [Column("rate_key")]
        public string RateKey { get; set; } = "";     // e.g. "Al 6061-T6", "MotorPerKw"

        [Column("unit_label")]
        public string UnitLabel { get; set; } = "";   // per_kg | per_kw | flat | pct_of_subtotal

        [Column("rate_value")]
        public double RateValue { get; set; }

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}