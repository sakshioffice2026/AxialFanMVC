using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AxialFanMVC.Database
{
    public class ExceptionLogEntry
    {
        [Key, Column("id")]
        public int Id { get; set; }

        [MaxLength(200), Column("class_name")]
        public string ClassName { get; set; } = string.Empty;

        [MaxLength(200), Column("method_name")]
        public string MethodName { get; set; } = string.Empty;

        [Column("error")]
        public string Error { get; set; } = string.Empty;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("user_id")]
        public int? UserId { get; set; }
    }
}