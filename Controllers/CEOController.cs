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
                .Where(r => r.ForwardedToCEO && !r.CEOApproved && r.BudgetCheck == BudgetCheckStatus.OverBudget && (r.Status == ReportStatus.PendingCEOApproval))
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
                .Where(r => r.ForwardedToCEO && !r.CEOApproved && r.BudgetCheck == BudgetCheckStatus.OverBudget)
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
                .Where(r => r.ForwardedToCEO && !r.CEOApproved && r.BudgetCheck == BudgetCheckStatus.OverBudget && (r.Status == ReportStatus.PendingCEOApproval))
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
                    var category = key.Substring("budget_".Length).Trim();
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

            category = category.Trim();

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
        public async Task<IActionResult> EditBudget(int id, string category, decimal allocated)
        {
            var budget = await _db.Budgets.FindAsync(id);
            if (budget == null) return NotFound();

            if (string.IsNullOrWhiteSpace(category))
            {
                TempData["Error"] = "Category is required.";
                return RedirectToAction("Budget");
            }

            category = category.Trim();

            // ensure no duplicate category name for different budget
            var existing = await _db.Budgets.FirstOrDefaultAsync(b => b.Category == category && b.Id != id);
            if (existing != null)
            {
                TempData["Error"] = $"Another budget with category '{category}' already exists.";
                return RedirectToAction("Budget");
            }

            budget.Category = category;
            budget.Allocated = allocated;
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Budget '{budget.Category}' updated to ₱{allocated:N2}.";
            return RedirectToAction("Budget");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteBudget(int id)
        {
            var budget = await _db.Budgets.FindAsync(id);
            if (budget == null) return NotFound();

            // Optionally prevent deleting if there are expense items referencing this category
            var hasItems = await _db.ExpenseItems.AnyAsync(i => i.Category == budget.Category);
            if (hasItems)
            {
                TempData["Error"] = "Cannot delete budget: there are existing expense items using this category.";
                return RedirectToAction("Budget");
            }

            _db.Budgets.Remove(budget);
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Budget '{budget.Category}' deleted.";
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

        // ───────────── Account Management ─────────────
        public async Task<IActionResult> Users(string? tab)
        {
            // CEO accounts
            var ceoProfiles = await _db.CEOProfiles.Include(p => p.User).OrderByDescending(p => p.CreatedAt).ToListAsync();
            ViewBag.CEOProfiles = ceoProfiles;

            // Manager accounts
            var managerProfiles = await _db.ManagerProfiles.Include(p => p.User).OrderByDescending(p => p.CreatedAt).ToListAsync();
            ViewBag.ManagerProfiles = managerProfiles;

            // Finance accounts
            var financeProfiles = await _db.FinanceProfiles.Include(p => p.User).OrderByDescending(p => p.CreatedAt).ToListAsync();
            ViewBag.FinanceProfiles = financeProfiles;

            // Driver accounts
            var driverProfiles = await _db.DriverProfiles.Include(p => p.User).OrderByDescending(p => p.CreatedAt).ToListAsync();
            ViewBag.DriverProfiles = driverProfiles;

            ViewBag.ActiveTab = tab ?? "manager";

            return View("Users/Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateAccount(string email, string password, string fullName, string role, string? department, string? licenseNumber)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(fullName))
            {
                TempData["Error"] = "Email, password, and full name are required.";
                return RedirectToAction("Users", new { tab = role?.ToLower() });
            }

            // Only allow creating Manager, Finance, and Driver accounts
            if (role != "Manager" && role != "Finance" && role != "Driver")
            {
                TempData["Error"] = "You can only create Manager, Finance, or Driver accounts.";
                return RedirectToAction("Users");
            }

            var existingUser = await _userManager.FindByEmailAsync(email);
            if (existingUser != null)
            {
                TempData["Error"] = $"A user with email '{email}' already exists.";
                return RedirectToAction("Users", new { tab = role.ToLower() });
            }

            var user = new IdentityUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                LockoutEnabled = false
            };

            var result = await _userManager.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                TempData["Error"] = "Failed to create account: " + string.Join(", ", result.Errors.Select(e => e.Description));
                return RedirectToAction("Users", new { tab = role.ToLower() });
            }

            await _userManager.AddToRoleAsync(user, role);
            var ceoUserId = _userManager.GetUserId(User);

            switch (role)
            {
                case "Manager":
                    _db.ManagerProfiles.Add(new ManagerProfile
                    {
                        UserId = user.Id,
                        FullName = fullName,
                        Department = department,
                        IsActive = true,
                        CreatedByUserId = ceoUserId
                    });
                    break;
                case "Finance":
                    _db.FinanceProfiles.Add(new FinanceProfile
                    {
                        UserId = user.Id,
                        FullName = fullName,
                        Department = department,
                        IsActive = true,
                        CreatedByUserId = ceoUserId
                    });
                    break;
                case "Driver":
                    _db.DriverProfiles.Add(new DriverProfile
                    {
                        UserId = user.Id,
                        FullName = fullName,
                        LicenseNumber = licenseNumber,
                        IsActive = true,
                        CreatedByUserId = ceoUserId
                    });
                    break;
            }

            await _db.SaveChangesAsync();
            TempData["Success"] = $"{role} account for '{fullName}' created successfully.";
            return RedirectToAction("Users", new { tab = role.ToLower() });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleAccountStatus(int profileId, string role)
        {
            switch (role)
            {
                case "Manager":
                    var mp = await _db.ManagerProfiles.Include(p => p.User).FirstOrDefaultAsync(p => p.Id == profileId);
                    if (mp == null) return NotFound();
                    mp.IsActive = !mp.IsActive;
                    if (mp.User != null)
                    {
                        mp.User.LockoutEnd = mp.IsActive ? null : DateTimeOffset.MaxValue;
                        mp.User.LockoutEnabled = !mp.IsActive;
                    }
                    break;
                case "Finance":
                    var fp = await _db.FinanceProfiles.Include(p => p.User).FirstOrDefaultAsync(p => p.Id == profileId);
                    if (fp == null) return NotFound();
                    fp.IsActive = !fp.IsActive;
                    if (fp.User != null)
                    {
                        fp.User.LockoutEnd = fp.IsActive ? null : DateTimeOffset.MaxValue;
                        fp.User.LockoutEnabled = !fp.IsActive;
                    }
                    break;
                case "Driver":
                    var dp = await _db.DriverProfiles.Include(p => p.User).FirstOrDefaultAsync(p => p.Id == profileId);
                    if (dp == null) return NotFound();
                    dp.IsActive = !dp.IsActive;
                    if (dp.User != null)
                    {
                        dp.User.LockoutEnd = dp.IsActive ? null : DateTimeOffset.MaxValue;
                        dp.User.LockoutEnabled = !dp.IsActive;
                    }
                    break;
                default:
                    TempData["Error"] = "Invalid role.";
                    return RedirectToAction("Users");
            }

            await _db.SaveChangesAsync();
            TempData["Success"] = $"Account status updated successfully.";
            return RedirectToAction("Users", new { tab = role.ToLower() });
        }

        public IActionResult Index()
        {
            return RedirectToAction("Dashboard");
        }
    }
}
