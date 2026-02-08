using System.ComponentModel.DataAnnotations;

namespace CEMS.Models
{
    public class Budget
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Category { get; set; }

        [Required]
        public decimal Allocated { get; set; }

        public decimal Spent { get; set; }
    }
}
