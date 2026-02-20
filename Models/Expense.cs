using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace CEMS.Models
{
    public class Expense
    {
        [Key]
        public int Id { get; set; }

        [BindNever]
        public string? UserId { get; set; }

        [BindNever]
        public IdentityUser? User { get; set; }

        [Required]
        public string Category { get; set; }

        [Required]
        public decimal Amount { get; set; }

        [Required]
        public DateTime Date { get; set; }

        public string? Description { get; set; }

        [BindNever]
        public string? ReceiptPath { get; set; }

        // Store receipt binary and content type in the database if desired
        [BindNever]
        public byte[]? ReceiptData { get; set; }

        [BindNever]
        public string? ReceiptContentType { get; set; }

        public ExpenseStatus Status { get; set; } = ExpenseStatus.Pending;
    }

    public enum ExpenseStatus
    {
        Pending,
        Approved,
        Rejected
    }
}
