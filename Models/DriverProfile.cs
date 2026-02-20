using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace CEMS.Models
{
    public class DriverProfile
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = null!;

        public IdentityUser User { get; set; } = null!;

        [Required]
        [MaxLength(100)]
        public string FullName { get; set; } = null!;

        [MaxLength(50)]
        public string? LicenseNumber { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public string? CreatedByUserId { get; set; }
    }
}
