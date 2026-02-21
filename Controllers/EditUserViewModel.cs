using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;

namespace CEMS.Controllers
{
    public class EditUserViewModel
    {
        [Required]
        public string UserId { get; set; } = null!;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = null!;

        public List<string> Roles { get; set; } = new List<string>();

        // Profile fields
        [MaxLength(100)]
        public string? FullName { get; set; }

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
    }
}