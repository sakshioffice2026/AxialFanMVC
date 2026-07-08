using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AxialFanMVC.Database
{
    [Table("projects")]
    public class Project
    {
        [Key, Column("id")]
        public int Id { get; set; }

        [Column("user_id")]
        public int UserId { get; set; }

        [Required, MaxLength(200), Column("name")]
        public string Name { get; set; } = string.Empty;

        //New
        [MaxLength(200), Column("client")]
        public string? Client { get; set; }

        [MaxLength(100), Column("application")]
        public string? Application { get; set; }

        [MaxLength(100), Column("engineer")]
        public string? Engineer { get; set; }

        [Column("job_date")]
        public DateOnly? JobDate { get; set; }

        [Column("description")]
        public string? Description { get; set; }

        [MaxLength(20), Column("status")]
        public string Status { get; set; } = "draft"; // draft | active | archived

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public User User { get; set; } = null!;
        public ICollection<DesignInput> DesignInputs { get; set; } = new List<DesignInput>();
        public ICollection<ExportLog> ExportLogs { get; set; } = new List<ExportLog>();
    }
}
