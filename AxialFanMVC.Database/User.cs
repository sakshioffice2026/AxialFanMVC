using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AxialFanMVC.Database
{
    [Table("users")]
    public class User
    {
        [Key, Column("id")]
        public int Id { get; set; }

        [Required, MaxLength(100), Column("name")]
        public string Name { get; set; } = string.Empty;

        [Required, MaxLength(200), Column("email")]
        public string Email { get; set; } = string.Empty;

        [Required, Column("password_hash")]
        public string PasswordHash { get; set; } = string.Empty;

        [MaxLength(20), Column("role")]
        public string Role { get; set; } = "user"; // user | admin

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<Project> Projects { get; set; } = new List<Project>();
        public ICollection<ExportLog> ExportLogs { get; set; } = new List<ExportLog>();
    }
}
