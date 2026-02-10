using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using CEMS.Models;

namespace CEMS.Controllers
{
    [Authorize(Roles = "CEO")]
    public class CEOController : Controller
    {
        private readonly Data.ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;

        public CEOController(Data.ApplicationDbContext db, UserManager<IdentityUser> userManager, RoleManager<IdentityRole> roleManager)
        {
            _db = db;
            _userManager = userManager;
            _roleManager = roleManager;
        }

        // ───────────── Dashboard ─────────────
        public async Task<IActionResult> Dashboard()
        {
            var budgets = await _db.Budgets.ToListAsync();
            ViewBag.TotalBudget = budgets.Sum(b => b.Allocated);
            ViewBag.TotalSpent = budgets.Sum(b => b.Spent);
            ViewBag.TotalRemaining = budgets.Sum(b => b.Allocated - b.Spent);

            var pendingCEO = await _db.ExpenseReports
                .Where(r => r.ForwardedToCEO && !r.CEOApproved && (r.Status == ReportStatus.Submitted || r.Status == ReportStatus.PendingCEOApproval || r.Status == ReportStatus.Approved))
                .ToListAsync();
            ViewBag.PendingCEOCount = pendingCEO.Count;
            ViewBag.PendingCEOTotal = pendingCEO.Sum(r => r.TotalAmount);

            var driverRole = await _roleManager.FindByNameAsync("Driver");
            if (driverRole != null)
            {
                var driverUsers = await _userManager.GetUsersInRoleAsync("Driver");
                ViewBag.ActiveDrivers = driverUsers.Count;
            }
            else
            {
                ViewBag.ActiveDrivers = 0;
            }

            // recent over-budget reports for table
            var recentOB = await _db.ExpenseReports
                .Where(r => r.ForwardedToCEO && !r.CEOApproved)
                .OrderByDescending(r => r.SubmissionDate)
                .Take(10)
                .ToListAsync();

            var userIds = recentOB.Select(r => r.UserId).Where(id => id != null).Distinct().ToList();
            var users = await _userManager.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName);

            var recentDto = new List<OverBudgetReportDto>();
            foreach (var r in recentOB)
            {
                var items = await _db.ExpenseItems.Where(i => i.ReportId == r.Id).ToListAsync();
                decimal exceed = 0m;
                foreach (var group in items.GroupBy(i => i.Category))
                {
                    var budget = budgets.FirstOrDefault(b => b.Category == group.Key);
                    if (budget != null && budget.Spent > budget.Allocated)
                    {
                        exceed += budget.Spent - budget.Allocated;
                    }
                }
                recentDto.Add(new OverBudgetReportDto
                {
                    Report = r,
                    UserName = r.UserId != null && users.ContainsKey(r.UserId) ? users[r.UserId] : "Unknown",
                    ExceedAmount = exceed
                });
            }
            ViewBag.RecentOverBudget = recentDto;

            return View("Dashboard/Index");
        }

        // ───────────── Over-Budget Expense Reports (Approvals) ─────────────
        public async Task<IActionResult> Approvals()
        {
            var reports = await _db.ExpenseReports
                .Where(r => r.ForwardedToCEO && !r.CEOApproved && (r.Status == ReportStatus.Submitted || r.Status == ReportStatus.PendingCEOApproval || r.Status == ReportStatus.Approved))
                .OrderByDescending(r => r.SubmissionDate)
                .ToListAsync();

            var userIds = reports.Select(r => r.UserId).Where(id => id != null).Distinct().ToList();
            var users = await _userManager.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName);
            var budgets = await _db.Budgets.ToListAsync();

            var dto = new List<OverBudgetReportDto>();
            foreach (var r in reports)
            {
                var items = await _db.ExpenseItems.Where(i => i.ReportId == r.Id).ToListAsync();
                decimal exceed = 0m;
                foreach (var group in items.GroupBy(i => i.Category))
                {
                    var budget = budgets.FirstOrDefault(b => b.Category == group.Key);
                    if (budget != null && budget.Spent > budget.Allocated)
                    {
                        exceed += budget.Spent - budget.Allocated;
                    }
                }
                dto.Add(new OverBudgetReportDto
                {
                    Report = r,
                    UserName = r.UserId != null && users.ContainsKey(r.UserId) ? users[r.UserId] : "Unknown",
                    ExceedAmount = exceed
                });
            }

            ViewBag.Forwarded = dto;
            return View("Approvals/Index");
        }

        [HttpGet]
        public async Task<IActionResult> ReportDetails(int id)
        {
            var report = await _db.ExpenseReports
                .Include(r => r.Items)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (report == null) return NotFound();

            var user = report.UserId != null ? await _userManager.FindByIdAsync(report.UserId) : null;
            ViewBag.ReportUserName = user?.UserName ?? "Unknown";

            var budgets = await _db.Budgets.ToListAsync();
            ViewBag.Budgets = budgets;

            return View("ReportDetails", report);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CEOApprove(int id, string? remarks)
        {
            var report = await _db.ExpenseReports.FindAsync(id);
            if (report == null) return NotFound();
            report.CEOApproved = true;
            report.ForwardedToCEO = false;
            report.Status = ReportStatus.Approved;

            var approval = new Approval
            {
                ReportId = id,
                ApprovedByUserId = _userManager.GetUserId(User),
                Status = ApprovalStatus.Approved,
                DecisionDate = DateTime.UtcNow,
                Stage = "CEO",
                Remarks = remarks
            };
            _db.Approvals.Add(approval);

            // Optionally adjust budgets if CEO changed values
            foreach (var key in Request.Form.Keys)
            {
                if (key.StartsWith("budget_"))
                {
                    var category = key.Substring("budget_".Length);
                    if (decimal.TryParse(Request.Form[key], out var newAlloc))
                    {
                        var budget = await _db.Budgets.FirstOrDefaultAsync(b => b.Category == category);
                        if (budget != null)
                        {
                            budget.Allocated = newAlloc;
                        }
                    }
                }
            }

            await _db.SaveChangesAsync();

            TempData["Success"] = "Report approved by CEO and forwarded to Finance for reimbursement.";
            return RedirectToAction("Approvals");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CEOReject(int id, string? remarks)
        {
            var report = await _db.ExpenseReports.FindAsync(id);
            if (report == null) return NotFound();
            report.CEOApproved = false;
            report.ForwardedToCEO = false;
            report.Status = ReportStatus.Rejected;

            var approval = new Approval
            {
                ReportId = id,
                ApprovedByUserId = _userManager.GetUserId(User),
                Status = ApprovalStatus.Rejected,
                DecisionDate = DateTime.UtcNow,
                Stage = "CEO",
                Remarks = remarks
            };
            _db.Approvals.Add(approval);
            await _db.SaveChangesAsync();

            TempData["Success"] = "Report rejected by CEO.";
            return RedirectToAction("Approvals");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveForReimbursement(int id)
        {
            var report = await _db.ExpenseReports.FindAsync(id);
            if (report == null) return NotFound();

            report.CEOApproved = true;
            report.ForwardedToCEO = false;
            report.Status = ReportStatus.Approved;

            var approval = new Approval
            {
                ReportId = id,
                ApprovedByUserId = _userManager.GetUserId(User),
                Status = ApprovalStatus.Approved,
                DecisionDate = DateTime.UtcNow,
                Stage = "CEO",
                Remarks = "Quick-approved for reimbursement"
            };
            _db.Approvals.Add(approval);
            await _db.SaveChangesAsync();

            TempData["Success"] = "Report approved for reimbursement.";
            return RedirectToAction("Approvals");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectForReimbursement(int id)
        {
            var report = await _db.ExpenseReports.FindAsync(id);
            if (report == null) return NotFound();

            report.CEOApproved = false;
            report.ForwardedToCEO = false;
            report.Status = ReportStatus.Rejected;

            var approval = new Approval
            {
                ReportId = id,
                ApprovedByUserId = _userManager.GetUserId(User),
                Status = ApprovalStatus.Rejected,
                DecisionDate = DateTime.UtcNow,
                Stage = "CEO",
                Remarks = "Quick-rejected"
            };
            _db.Approvals.Add(approval);
            await _db.SaveChangesAsync();

            TempData["Success"] = "Report rejected by CEO.";
            return RedirectToAction("Approvals");
        }

        // ───────────── Budget Management ─────────────
        public async Task<IActionResult> Budget()
        {
            var budgets = await _db.Budgets.OrderBy(b => b.Category).ToListAsync();
            ViewBag.Budgets = budgets;
            return View("Budget/Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateBudget(string category, decimal allocated)
        {
            if (string.IsNullOrWhiteSpace(category))
            {
                TempData["Error"] = "Category is required.";
                return RedirectToAction("Budget");
            }

            var existing = await _db.Budgets.FirstOrDefaultAsync(b => b.Category == category);
            if (existing != null)
            {
                TempData["Error"] = $"Budget for '{category}' already exists. Use edit instead.";
                return RedirectToAction("Budget");
            }

            _db.Budgets.Add(new Budget { Category = category, Allocated = allocated, Spent = 0 });
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Budget for '{category}' created successfully.";
            return RedirectToAction("Budget");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateBudget(int id, decimal allocated)
        {
            var budget = await _db.Budgets.FindAsync(id);
            if (budget == null) return NotFound();

            budget.Allocated = allocated;
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Budget for '{budget.Category}' updated to ₱{allocated:N2}.";
            return RedirectToAction("Budget");
        }

        // ───────────── Expense Overview ─────────────
        public async Task<IActionResult> ExpenseOverview(string? category, string? status, DateTime? start, DateTime? end)
        {
            var q = _db.ExpenseReports.Include(r => r.Items).AsQueryable();

            if (!string.IsNullOrEmpty(status) && Enum.TryParse<ReportStatus>(status, out var st))
                q = q.Where(r => r.Status == st);
            if (start.HasValue)
                q = q.Where(r => r.SubmissionDate >= start.Value);
            if (end.HasValue)
                q = q.Where(r => r.SubmissionDate <= end.Value);

            var reports = await q.OrderByDescending(r => r.SubmissionDate).Take(200).ToListAsync();

            // filter by category at item level if needed
            if (!string.IsNullOrEmpty(category))
            {
                reports = reports.Where(r => r.Items.Any(i => i.Category == category)).ToList();
            }

            var userIds = reports.Select(r => r.UserId).Where(id2 => id2 != null).Distinct().ToList();
            var users = await _userManager.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName);

            var dto = reports.Select(r => new PendingExpenseReportDto
            {
                Report = r,
                UserName = r.UserId != null && users.ContainsKey(r.UserId) ? users[r.UserId] : "Unknown"
            }).ToList();
            ViewBag.Reports = dto;

            // get categories for filter dropdown
            var categories = await _db.Budgets.Select(b => b.Category).Distinct().ToListAsync();
            ViewBag.Categories = categories;
            ViewBag.FilterCategory = category;
            ViewBag.FilterStatus = status;
            ViewBag.FilterStart = start;
            ViewBag.FilterEnd = end;

            return View("ExpenseOverview/Index");
        }

        // ───────────── Financial Reports & Analytics ─────────────
        public async Task<IActionResult> Analytics()
        {
            var budgets = await _db.Budgets.ToListAsync();
            ViewBag.Budgets = budgets;

            var totalReports = await _db.ExpenseReports.CountAsync();
            var approvedCount = await _db.ExpenseReports.Where(r => r.Status == ReportStatus.Approved).CountAsync();
            var rejectedCount = await _db.ExpenseReports.Where(r => r.Status == ReportStatus.Rejected).CountAsync();
            var pendingCount = await _db.ExpenseReports.Where(r => r.Status == ReportStatus.Submitted || r.Status == ReportStatus.PendingCEOApproval).CountAsync();

            ViewBag.TotalReports = totalReports;
            ViewBag.ApprovedCount = approvedCount;
            ViewBag.RejectedCount = rejectedCount;
            ViewBag.PendingCount = pendingCount;

            var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
            var monthlyReimbursed = await _db.ExpenseReports
                .Where(r => r.Reimbursed && r.SubmissionDate >= monthStart)
                .SumAsync(r => (decimal?)r.TotalAmount) ?? 0m;
            ViewBag.MonthlyReimbursed = monthlyReimbursed;

            return View("Analytics/Index");
        }

        [HttpGet]
        public async Task<IActionResult> Metrics()
        {
            var budgets = await _db.Budgets.Select(b => new { b.Category, b.Allocated, b.Spent }).ToListAsync();
            var totalPending = await _db.ExpenseReports.Where(r => r.Status == ReportStatus.Submitted || r.Status == ReportStatus.PendingCEOApproval).CountAsync();
            var approved = await _db.ExpenseReports.Where(r => r.Status == ReportStatus.Approved).SumAsync(r => (decimal?)r.TotalAmount) ?? 0m;
            var rejected = await _db.ExpenseReports.Where(r => r.Status == ReportStatus.Rejected).SumAsync(r => (decimal?)r.TotalAmount) ?? 0m;
            var overBudgetCount = await _db.ExpenseReports.Where(r => r.BudgetCheck == BudgetCheckStatus.OverBudget).CountAsync();

            // monthly expense trend (last 6 months)
            var months = Enumerable.Range(0, 6).Select(i => DateTime.UtcNow.AddMonths(-i)).Select(d => new { d.Year, d.Month }).Reverse().ToList();
            var monthlyData = new List<object>();
            foreach (var m in months)
            {
                var mStart = new DateTime(m.Year, m.Month, 1);
                var mEnd = mStart.AddMonths(1);
                var total = await _db.ExpenseReports.Where(r => r.SubmissionDate >= mStart && r.SubmissionDate < mEnd).SumAsync(r => (decimal?)r.TotalAmount) ?? 0m;
                monthlyData.Add(new { label = mStart.ToString("MMM yyyy"), total });
            }

            return Json(new { totalPending, approved, rejected, overBudgetCount, budgets, monthlyData });
        }

        // ───────────── Reports (legacy) ─────────────
        public IActionResult Reports()
        {
            return RedirectToAction("Analytics");
        }

        // ───────────── User Management ─────────────
        public async Task<IActionResult> Users()
        {
            var allUsers = await _userManager.Users.ToListAsync();
            var userList = new List<dynamic>();
            foreach (var u in allUsers)
            {
                var roles = await _userManager.GetRolesAsync(u);
                userList.Add(new { User = u, Roles = string.Join(", ", roles) });
            }
            ViewBag.Users = userList;

            var roles2 = await _roleManager.Roles.Select(r => r.Name).ToListAsync();
            ViewBag.AvailableRoles = roles2;

            return View("Users/Index");
        }

        public IActionResult Index()
        {
            return RedirectToAction("Dashboard");
        }
    }
}
