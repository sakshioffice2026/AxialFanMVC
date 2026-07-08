using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AxialFanMVC.Database
{
    public class Drawing
    {
        [Key, Column("id")]
        public int Id { get; set; }

        [Column("design_result_id")]
        public int DesignResultId { get; set; }

        [MaxLength(30), Column("drawing_type")]
        public string DrawingType { get; set; } = string.Empty; // front_elevation | cross_section | blade_profile

        [Column("svg_data")] public string? SvgData { get; set; }
        [Column("dxf_path")] public string? DxfPath { get; set; }
        [Column("pdf_path")] public string? PdfPath { get; set; }

        [Column("generated_at")]
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

        public DesignResult DesignResult { get; set; } = null!;
    }
}
