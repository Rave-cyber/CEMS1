using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using CEMS.Models;

namespace CEMS.Controllers
{
    [Authorize(Roles = "Manager")]
    public class ManagerController : Controller
    {
        private readonly Data.ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;

        public ManagerController(Data.ApplicationDbContext db, UserManager<IdentityUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }
        
        public async Task<IActionResult> Dashboard()
        {
            var pendingReports = await _db.ExpenseReports
                .Where(r => r.Status == ReportStatus.Submitted)
                .OrderByDescending(r => r.SubmissionDate)
                .Take(20)
                .ToListAsync();

            var userIds = pendingReports.Select(r => r.UserId).Where(id => id != null).Distinct().ToList();
            var users = await _userManager.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName);

            var pendingWithUsers = pendingReports.Select(r => new PendingExpenseReportDto
            {
                Report = r,
                UserName = (r.UserId != null && users.ContainsKey(r.UserId)) ? users[r.UserId] : "Unknown"
            }).ToList();

            ViewBag.PendingReports = pendingWithUsers;

            // Budget totals (set by CEO)
            var budgets = await _db.Budgets.ToListAsync();
            ViewBag.TotalBudget = budgets.Sum(b => b.Allocated);
            ViewBag.TotalSpent = budgets.Sum(b => b.Spent);
            ViewBag.TotalRemaining = budgets.Sum(b => b.Allocated - b.Spent);

            // Approved today
            var today = DateTime.UtcNow.Date;
            var approvedToday = await _db.Approvals
                .Include(a => a.Report)
                .Where(a => a.Stage == "Manager" && a.Status == ApprovalStatus.Approved && a.DecisionDate.HasValue && a.DecisionDate.Value.Date == today)
                .ToListAsync();
            ViewBag.ApprovedTodayCount = approvedToday.Count;
            ViewBag.ApprovedTodayTotal = approvedToday.Sum(a => a.Report != null ? a.Report.TotalAmount : 0m);

            // Active drivers
            var driverUsers = await _userManager.GetUsersInRoleAsync("Driver");
            ViewBag.ActiveDrivers = driverUsers.Count;

            return View("Dashboard/Index");
        }

        public async Task<IActionResult> Reports(string driver, DateTime? start, DateTime? end, string status)
        {
            var q = _db.ExpenseReports.Include(r => r.User).AsQueryable();
            if (!string.IsNullOrEmpty(driver))
            {
                var users = await _userManager.Users.Where(u => u.UserName.Contains(driver)).Select(u => u.Id).ToListAsync();
                q = q.Where(r => r.UserId != null && users.Contains(r.UserId));
            }
            if (start.HasValue) q = q.Where(r => r.SubmissionDate >= start.Value);
            if (end.HasValue) q = q.Where(r => r.SubmissionDate <= end.Value);
            if (!string.IsNullOrEmpty(status) && Enum.TryParse<ReportStatus>(status, out var st)) q = q.Where(r => r.Status == st);

            var reports = await q.OrderByDescending(r => r.SubmissionDate).Take(200).ToListAsync();

            var userIds = reports.Select(r => r.UserId).Where(id => id != null).Distinct().ToList();
            var usersDict = await _userManager.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName);

            var dto = reports.Select(r => new PendingExpenseReportDto { Report = r, UserName = r.UserId != null && usersDict.ContainsKey(r.UserId) ? usersDict[r.UserId] : "Unknown" }).ToList();
            ViewBag.Reports = dto;
            return View("Reports/Index");
        }

        [HttpGet]
        public async Task<IActionResult> Metrics()
        {
            var totalPending = await _db.ExpenseReports.Where(r => r.Status == ReportStatus.Submitted).CountAsync();
            var approved = await _db.ExpenseReports.Where(r => r.Status == ReportStatus.Approved).SumAsync(r => (decimal?)r.TotalAmount) ?? 0m;
            var rejected = await _db.ExpenseReports.Where(r => r.Status == ReportStatus.Rejected).SumAsync(r => (decimal?)r.TotalAmount) ?? 0m;
            var overBudgetCount = await _db.ExpenseReports.Where(r => r.BudgetCheck == BudgetCheckStatus.OverBudget).CountAsync();

            // budget per category
            var budgets = await _db.Budgets.Select(b => new { b.Category, b.Allocated, b.Spent }).ToListAsync();

            return Json(new { totalPending, approved, rejected, overBudgetCount, budgets });
        }

        public async Task<IActionResult> OverBudget()
        {
            var reports = await _db.ExpenseReports
                .Where(r => r.BudgetCheck == BudgetCheckStatus.OverBudget)
                .OrderByDescending(r => r.SubmissionDate)
                .ToListAsync();

            var userIds = reports.Select(r => r.UserId).Where(id => id != null).Distinct().ToList();
            var users = await _userManager.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName);

            var list = new List<OverBudgetReportDto>();
            foreach(var r in reports)
            {
                // compute exceed amount roughly: sum of items - budget allocated
                var items = await _db.ExpenseItems.Where(i => i.ReportId == r.Id).ToListAsync();
                decimal exceed = 0m;
                foreach(var group in items.GroupBy(i => i.Category))
                {
                    var cat = group.Key;
                    var total = group.Sum(i => i.Amount);
                    var budget = await _db.Budgets.FirstOrDefaultAsync(b => b.Category == cat);
                    if (budget != null)
                    {
                        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
                        var currentMonthSpent = await _db.ExpenseItems.Where(ii => ii.Category == cat && ii.Date >= monthStart).SumAsync(ii => (decimal?)ii.Amount) ?? 0m;
                        if (currentMonthSpent + total > budget.Allocated)
                        {
                            exceed += (currentMonthSpent + total) - budget.Allocated;
                        }
                    }
                }

                list.Add(new OverBudgetReportDto { Report = r, UserName = r.UserId != null && users.ContainsKey(r.UserId) ? users[r.UserId] : "Unknown", ExceedAmount = exceed });
            }

            ViewBag.OverBudget = list;
            return View("OverBudget/Index");
        }

        [HttpGet]
        public async Task<IActionResult> Diagnostics()
        {
            var total = await _db.ExpenseReports.CountAsync();
            var pendingCount = await _db.ExpenseReports.Where(r => r.Status == ReportStatus.Submitted).CountAsync();
            var approvedCount = await _db.ExpenseReports.Where(r => r.Status == ReportStatus.Approved).CountAsync();
            var rejectedCount = await _db.ExpenseReports.Where(r => r.Status == ReportStatus.Rejected).CountAsync();

            var recent = await _db.ExpenseReports.OrderByDescending(r => r.SubmissionDate).Take(20)
                .Select(r => new { r.Id, r.UserId, r.TotalAmount, r.SubmissionDate, r.Status, r.BudgetCheck })
                .ToListAsync();

            return Json(new { total, pendingCount, approvedCount, rejectedCount, recent });
        }

        public IActionResult Expenses()
        {
            return View("Expenses/Index");
        }

        public IActionResult Team()
        {
            return View("Team/Index");
        }

        public async Task<IActionResult> Budget()
        {
            var budgets = await _db.Budgets.OrderBy(b => b.Category).ToListAsync();
            ViewBag.Budgets = budgets;
            return View("Budget/Index");
        }

        public IActionResult Index()
        {
            return RedirectToAction("Dashboard");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveReport(int id)
        {
            var report = await _db.ExpenseReports
                .Include(r => r.Items)
                .FirstOrDefaultAsync(r => r.Id == id);
            if (report == null) return NotFound();

            report.Status = ReportStatus.Approved;
            // record manager approval
            var managerApproval = new Models.Approval
            {
                ReportId = report.Id,
                ApprovedByUserId = _userManager.GetUserId(User),
                Status = Models.ApprovalStatus.Approved,
                DecisionDate = DateTime.UtcNow,
                Stage = "Manager",
                Remarks = "Approved by manager"
            };
            _db.Approvals.Add(managerApproval);
            // If within budget, forward to finance by leaving ForwardedToCEO = false
            // If over budget, flag and forward to CEO
            if (report.BudgetCheck == BudgetCheckStatus.OverBudget)
            {
                report.ForwardedToCEO = true;
                report.Status = ReportStatus.PendingCEOApproval;
            }

            foreach (var item in report.Items)
            {
                var budget = _db.Budgets.FirstOrDefault(b => b.Category == item.Category);
                if (budget != null)
                {
                    budget.Spent += item.Amount;
                }
            }

            await _db.SaveChangesAsync();

            // If within budget, create a task/flag for finance to reimburse (simple approach: set Reimbursed=false and leave ForwardedToCEO false)
            if (report.BudgetCheck == BudgetCheckStatus.WithinBudget)
            {
                // In a real system: create finance work item / notification. Here we just ensure flags are set appropriately.
                report.Reimbursed = false;
                report.ForwardedToCEO = false;
                await _db.SaveChangesAsync();
            }
            return RedirectToAction("Dashboard");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectReport(int id, string? returnUrl)
        {
            var report = await _db.ExpenseReports.FindAsync(id);
            if (report == null) return NotFound();

            report.Status = ReportStatus.Rejected;
            await _db.SaveChangesAsync();

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            var referer = Request.Headers["Referer"].ToString();
            if (!string.IsNullOrEmpty(referer) && Url.IsLocalUrl(referer))
                return Redirect(referer);

            return RedirectToAction("Dashboard");
        }

        [HttpGet]
        public async Task<IActionResult> ReportDetails(int id)
        {
            var report = await _db.ExpenseReports
                .Include(r => r.Items)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (report == null)
                return NotFound();

            var user = report.UserId != null ? await _userManager.FindByIdAsync(report.UserId) : null;
            ViewBag.ReportUserName = user?.UserName ?? "Unknown";

            // Load budgets so the details page can show per-category budget vs spent
            var budgets = await _db.Budgets.ToListAsync();
            ViewBag.Budgets = budgets;

            // Load approval trail
            var approvals = await _db.Approvals
                .Where(a => a.ReportId == id)
                .OrderBy(a => a.DecisionDate)
                .ToListAsync();
            ViewBag.Approvals = approvals;

            return View("Reports/Details", report);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForwardToCEO(int id)
        {
            var report = await _db.ExpenseReports.FindAsync(id);
            if (report == null) return NotFound();

            report.ForwardedToCEO = true;
            report.Status = ReportStatus.PendingCEOApproval;
            await _db.SaveChangesAsync();

            TempData["Success"] = "Report forwarded to CEO for approval.";
            return RedirectToAction("Dashboard");
        }
    }
}