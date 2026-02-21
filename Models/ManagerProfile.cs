using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace CEMS.Models
{
    public class ManagerProfile
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = null!;

        public IdentityUser User { get; set; } = null!;

        [Required]
        [MaxLength(100)]
        public string FullName { get; set; } = null!;

        [MaxLength(100)]
        public string? Department { get; set; }

        [MaxLength(200)]
        public string? Street { get; set; }

        [MaxLength(100)]
        public string? Barangay { get; set; }

        [MaxLength(100)]
        public string? City { get; set; }

        [MaxLength(100)]
        public string? Province { get; set; }

        [MaxLength(10)]
        public string? ZipCode { get; set; }

        [MaxLength(100)]
        public string? Country { get; set; }

        [MaxLength(20)]
        public string? ContactNumber { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public string? CreatedByUserId { get; set; }
    }
}
