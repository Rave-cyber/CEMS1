using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace CEMS.Models
{
    public class ExpenseReport
    {
        [Key]
        public int Id { get; set; }

        public string? UserId { get; set; }

        public IdentityUser? User { get; set; }

        public DateTime SubmissionDate { get; set; } = DateTime.UtcNow;

        public ReportStatus Status { get; set; } = ReportStatus.Submitted;

        public BudgetCheckStatus BudgetCheck { get; set; } = BudgetCheckStatus.WithinBudget;

        public decimal TotalAmount { get; set; }

        public ICollection<ExpenseItem> Items { get; set; } = new List<ExpenseItem>();

        // Flag to indicate report was forwarded to CEO for final approval
        public bool ForwardedToCEO { get; set; } = false;
        // Mark when finance completed reimbursement
        public bool Reimbursed { get; set; } = false;
        // Mark whether CEO has approved an over-budget report
        public bool CEOApproved { get; set; } = false;
    }

    public enum ReportStatus
    {
        Submitted,
        Approved,
        Rejected
    }

    public enum BudgetCheckStatus
    {
        WithinBudget,
        OverBudget
    }
}
