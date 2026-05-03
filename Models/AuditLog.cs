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

        [MaxLength(1000)]
        public string? Details { get; set; }

        public int? RelatedRecordId { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>IP address of the request — used for intrusion detection.</summary>
        [MaxLength(45)] // supports IPv6
        public string? IpAddress { get; set; }

        /// <summary>Browser/client user-agent — used for session anomaly detection.</summary>
        [MaxLength(500)]
        public string? UserAgent { get; set; }
    }
}
