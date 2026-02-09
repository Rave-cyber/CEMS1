namespace CEMS.Models
{
    public class OverBudgetReportDto
    {
        public ExpenseReport Report { get; set; }
        public string? UserName { get; set; }
        public decimal ExceedAmount { get; set; }
    }
}
