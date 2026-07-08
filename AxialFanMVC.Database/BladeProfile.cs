using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AxialFanMVC.Database
{
    
    public class BladeProfile
    {
        [Key, Column("id")]
        public int Id { get; set; }

        [Required, MaxLength(50), Column("name")]
        public string Name { get; set; } = string.Empty;

        [MaxLength(20), Column("type")]
        public string Type { get; set; } = "NACA"; // NACA | custom

        [Column("coordinate_data")]
        public string? CoordinateData { get; set; } // JSON x/y coords

        [Column("description")]
        public string? Description { get; set; }

        public ICollection<DesignInput> DesignInputs { get; set; } = new List<DesignInput>();
    }
}
