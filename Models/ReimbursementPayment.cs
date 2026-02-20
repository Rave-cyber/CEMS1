using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CEMS.Models
{
    public class ReimbursementPayment
    {
        [Key]
        public int Id { get; set; }

        public int ReportId { get; set; }

        [ForeignKey("ReportId")]
        public ExpenseReport? Report { get; set; }

        /// <summary>PayMongo link ID (e.g. "link_...")</summary>
        public string PayMongoLinkId { get; set; } = string.Empty;

        /// <summary>PayMongo checkout URL for the driver</summary>
        public string CheckoutUrl { get; set; } = string.Empty;

        /// <summary>unpaid, paid, expired</summary>
        public string Status { get; set; } = "unpaid";

        public decimal Amount { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? PaidAt { get; set; }

        public string? ProcessedByUserId { get; set; }
    }
}
