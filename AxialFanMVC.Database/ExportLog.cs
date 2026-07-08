using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AxialFanMVC.Database
{
    public class ExportLog
    {
        [Key, Column("id")]
        public int Id { get; set; }

        [Column("project_id")] public int ProjectId { get; set; }
        [Column("user_id")] public int UserId { get; set; }

        [MaxLength(20), Column("format")]
        public string Format { get; set; } = string.Empty; // pdf | dxf | xlsx

        [Column("file_path")] public string? FilePath { get; set; }
        [Column("exported_at")] public DateTime ExportedAt { get; set; } = DateTime.UtcNow;

        public Project Project { get; set; } = null!;
        public User User { get; set; } = null!;
    }
}
