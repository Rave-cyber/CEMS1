using System;

namespace CEMS.Models
{
    // Simple DTO to pass pending expense with user display name to views
    public class PendingExpenseDto
    {
        public Expense Expense { get; set; }
        public string UserName { get; set; }
    }
}
