using System.ComponentModel.DataAnnotations;

namespace CEMS.Models
{
    public class Notification
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(200)]
        public string Title { get; set; } = null!;

        [Required]
        [MaxLength(500)]
        public string Message { get; set; } = null!;

        /// <summary>The specific user who should receive this notification.</summary>
        [Required]
        public string UserId { get; set; } = null!;

        /// <summary>The role of the receiver (for informational/filtering purposes).</summary>
        [MaxLength(50)]
        public string? Role { get; set; }

        public bool IsRead { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Optional link to the related expense report.</summary>
        public int? RelatedReportId { get; set; }

        /// <summary>Event type that triggered this notification.</summary>
        [MaxLength(100)]
        public string? Type { get; set; }
    }
}
