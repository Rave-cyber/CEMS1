using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace CEMS.Models
{
    public enum ApprovalStatus
    {
        Pending,
        Approved,
        Rejected
    }

    public class Approval
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int ReportId { get; set; }

        public ExpenseReport? Report { get; set; }

        // Identity user id (string)
        public string? ApprovedByUserId { get; set; }
        public IdentityUser? ApprovedByUser { get; set; }

        public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;

        public string? Remarks { get; set; }

        public DateTime? DecisionDate { get; set; }

        // Stage: e.g. Manager, CEO, Finance
        public string? Stage { get; set; }
    }
}
