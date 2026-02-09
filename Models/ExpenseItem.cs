using System;
using System.ComponentModel.DataAnnotations;

namespace CEMS.Models
{
    public class ExpenseItem
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int ReportId { get; set; }

        public ExpenseReport? Report { get; set; }

        [Required]
        public string Category { get; set; }

        [Required]
        public decimal Amount { get; set; }

        [Required]
        public DateTime Date { get; set; }

        public string? Description { get; set; }

        public string? ReceiptPath { get; set; }

        public byte[]? ReceiptData { get; set; }

        public string? ReceiptContentType { get; set; }
    }
}
