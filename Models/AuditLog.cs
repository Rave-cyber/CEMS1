using System.ComponentModel.DataAnnotations;

namespace CEMS.Models
{
    public class AuditLog
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(200)]
        public string Action { get; set; } = null!;

        [MaxLength(100)]
        public string? Module { get; set; }

        [MaxLength(50)]
        public string? Role { get; set; }

        public string? PerformedByUserId { get; set; }

        public string? TargetUserId { get; set; }

        [MaxLength(500)]
        public string? Details { get; set; }

        public int? RelatedRecordId { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
