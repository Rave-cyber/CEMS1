using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using CEMS.Models;

namespace CEMS.Controllers
{
    [Authorize(Roles = "Finance")]
    public class FinanceController : Controller
    {
        private readonly Data.ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;

        public FinanceController(Data.ApplicationDbContext db, UserManager<IdentityUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        // Dashboard: list reports ready for reimbursement
        public async Task<IActionResult> Dashboard()
        {
            var reports = await _db.ExpenseReports
                .Include(r => r.Items)
                // Only show reports approved by manager and cleared for finance:
                // - within budget and manager approved OR
                // - CEO approved after over-budget
                .Where(r => r.Status == ReportStatus.Approved && !r.Reimbursed)
                .Where(r => (r.BudgetCheck == BudgetCheckStatus.WithinBudget) || (r.CEOApproved == true))
                .OrderByDescending(r => r.SubmissionDate)
                .ToListAsync();

            var userIds = reports.Select(r => r.UserId).Where(id => id != null).Distinct().ToList();
            var users = await _userManager.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName);

            var dto = reports.Select(r => new PendingExpenseReportDto { Report = r, UserName = r.UserId != null && users.ContainsKey(r.UserId) ? users[r.UserId] : "Unknown" }).ToList();
            ViewBag.Reports = dto;
            ViewBag.PendingCount = dto.Count;
            ViewBag.TotalToProcess = dto.Sum(x => x.Report.TotalAmount);

            // processed today: count finance approvals today
            var today = DateTime.UtcNow.Date;
            var processedTodayCount = await _db.Approvals
                .Where(a => a.Stage == "Finance" && a.Status == ApprovalStatus.Approved && a.DecisionDate.HasValue && a.DecisionDate.Value.Date == today)
                .CountAsync();

            var processedTodayTotal = await _db.Approvals
                .Include(a => a.Report)
                .Where(a => a.Stage == "Finance" && a.Status == ApprovalStatus.Approved && a.DecisionDate.HasValue && a.DecisionDate.Value.Date == today)
                .SumAsync(a => (decimal?)(a.Report != null ? a.Report.TotalAmount : 0m)) ?? 0m;

            // monthly total reimbursed
            var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
            var monthlyTotal = await _db.ExpenseReports
                .Where(r => r.Reimbursed && r.SubmissionDate >= monthStart)
                .SumAsync(r => (decimal?)r.TotalAmount) ?? 0m;

            ViewBag.ProcessedTodayTotal = processedTodayTotal;
            ViewBag.ProcessedTodayCount = processedTodayCount;
            ViewBag.MonthlyTotal = monthlyTotal;

            return View("Dashboard/Index");
        }

        // List approved reports (visible to finance) — either within budget or CEO approved
        public async Task<IActionResult> ApprovedReports()
        {
            var reports = await _db.ExpenseReports
                .Where(r => r.Status == ReportStatus.Approved && (r.BudgetCheck == BudgetCheckStatus.WithinBudget || r.CEOApproved == true))
                .OrderByDescending(r => r.SubmissionDate)
                .ToListAsync();

            var userIds = reports.Select(r => r.UserId).Where(id => id != null).Distinct().ToList();
            var users = await _userManager.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName);

            var dto = reports.Select(r => new PendingExpenseReportDto { Report = r, UserName = r.UserId != null && users.ContainsKey(r.UserId) ? users[r.UserId] : "Unknown" }).ToList();
            ViewBag.ApprovedReports = dto;
            return View("ApprovedReports/Index");
        }

        // Reimbursements list (same as dashboard but explicit view)
        public async Task<IActionResult> Reimbursements()
        {
            var reports = await _db.ExpenseReports
                .Where(r => r.Status == ReportStatus.Approved && !r.Reimbursed && (r.BudgetCheck == BudgetCheckStatus.WithinBudget || r.CEOApproved == true))
                .OrderByDescending(r => r.SubmissionDate)
                .ToListAsync();

            var userIds = reports.Select(r => r.UserId).Where(id => id != null).Distinct().ToList();
            var users = await _userManager.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName);

            var dto = reports.Select(r => new PendingExpenseReportDto { Report = r, UserName = r.UserId != null && users.ContainsKey(r.UserId) ? users[r.UserId] : "Unknown" }).ToList();
            ViewBag.Reports = dto;
            return View("Reimbursements/Index");
        }

        public async Task<IActionResult> ReportDetails(int id)
        {
            var report = await _db.ExpenseReports
                .Include(r => r.Items)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (report == null) return NotFound();

            var user = report.UserId != null ? await _userManager.FindByIdAsync(report.UserId) : null;
            ViewBag.ReportUserName = user?.UserName ?? "Unknown";

            return View("ReportDetails", report);
        }

        // Payment history: show reports that have been reimbursed (recorded via Finance approval)
        public async Task<IActionResult> Payments()
        {
            var approvals = await _db.Approvals
                .Include(a => a.Report)
                .Where(a => a.Stage == "Finance" && a.Status == ApprovalStatus.Approved)
                .OrderByDescending(a => a.DecisionDate)
                .ToListAsync();

            // convert to simple DTO for view
            var list = approvals.Select(a => new {
                Report = a.Report,
                DecisionDate = a.DecisionDate,
                ApprovedBy = a.ApprovedByUserId
            }).ToList();

            ViewBag.Payments = list;
            return View("Payments/Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reimburse(int id)
        {
            var report = await _db.ExpenseReports.FindAsync(id);
            if (report == null) return NotFound();

            // Mark reimbursed
            report.Reimbursed = true;
            // record finance approval entry
            var approval = new Models.Approval
            {
                ReportId = report.Id,
                ApprovedByUserId = _userManager.GetUserId(User),
                Status = ApprovalStatus.Approved,
                DecisionDate = DateTime.UtcNow,
                Stage = "Finance",
                Remarks = "Reimbursed"
            };
            _db.Approvals.Add(approval);

            await _db.SaveChangesAsync();

            TempData["Success"] = "Report marked as reimbursed.";
            return RedirectToAction("Dashboard");
        }

        

        public IActionResult Index()
        {
            return RedirectToAction("Dashboard");
        }

        // Budget summary for finance (read-only)
        public async Task<IActionResult> Budget()
        {
            var budgets = await _db.Budgets.OrderBy(b => b.Category).ToListAsync();
            ViewBag.Budgets = budgets;
            return View("Budget/Index");
        }
    }
}
