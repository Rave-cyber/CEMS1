using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using CEMS.Models;
using CEMS.Services;

namespace CEMS.Controllers
{
    [Authorize(Roles = "CEO")]
    public class CEOController : Controller
    {
        private readonly Data.ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly FuelPriceService _fuelPriceService;
        private readonly NotificationService _notificationService;

        public CEOController(Data.ApplicationDbContext db, UserManager<IdentityUser> userManager, RoleManager<IdentityRole> roleManager, FuelPriceService fuelPriceService, NotificationService notificationService)
        {
            _db = db;
            _userManager = userManager;
            _roleManager = roleManager;
            _fuelPriceService = fuelPriceService;
            _notificationService = notificationService;
        }

        public async Task<IActionResult> Dashboard(DateTime? start = null, DateTime? end = null)
        {
            // Set default date range to current month
            var startDate = start ?? new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var endDate = end ?? DateTime.Now;

            // Get user's full name from CEOProfile
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                var ceoProfile = await _db.Set<CEOProfile>().FirstOrDefaultAsync(c => c.UserId == userId);
                ViewBag.UserFullName = ceoProfile?.FullName ?? User.Identity?.Name ?? "CEO";
            }
            else
            {
                ViewBag.UserFullName = User.Identity?.Name ?? "CEO";
            }

            var budgets = await _db.Budgets.ToListAsync();
            ViewBag.TotalBudget = budgets.Sum(b => b.Allocated);

            // Calculate spent dynamically from reimbursed expense items only (Finance paid, three-level date fallback for NULL dates)
            // Calculate spent dynamically from reimbursed expense items only (match Finance behavior)
            var spentByCategory = await _db.ExpenseItems
                .Where(ei => ei.Report != null && ei.Report.Reimbursed == true
                             && (ei.Date >= startDate && ei.Date <= endDate.AddDays(1)))
                .GroupBy(ei => ei.Category)
                .Select(g => new { Category = g.Key, Total = g.Sum(ei => ei.Amount) })
                .ToListAsync();

            var totalSpent = spentByCategory.Sum(s => s.Total);
            ViewBag.TotalSpent = totalSpent;
            ViewBag.TotalRemaining = budgets.Sum(b => b.Allocated) - totalSpent;

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
                    if (budget != null)
                    {
                        // Calculate actual spent for this category (dynamic, not static Budget.Spent)
                        var categorySpentAmount = spentByCategory.FirstOrDefault(s => s.Category == group.Key)?.Total ?? 0m;
                        if (categorySpentAmount > budget.Allocated)
                        {
                            exceed += categorySpentAmount - budget.Allocated;
                        }
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

            // ── Adaptive Spending Trend (auto granularity from date range) ──
            var trendLabels = new List<string>();
            var trendData = new List<decimal>();
            var totalDays = (endDate - startDate).TotalDays;

            if (totalDays <= 1)
            {
                for (int h = 0; h < 24; h += 4)
                {
                    var blockStart = startDate.Date.AddHours(h);
                    var blockEnd = blockStart.AddHours(4);
                    trendLabels.Add(blockStart.ToString("h tt"));
                    var t = await _db.ExpenseReports
                        .Where(r => r.Reimbursed == true && r.SubmissionDate >= blockStart && r.SubmissionDate < blockEnd)
                        .SumAsync(r => (decimal?)r.TotalAmount) ?? 0m;
                    trendData.Add(t);
                }
            }
            else if (totalDays <= 7)
            {
                for (var d = startDate.Date; d <= endDate.Date; d = d.AddDays(1))
                {
                    trendLabels.Add(d.ToString("ddd d"));
                    var t = await _db.ExpenseReports
                        .Where(r => r.Reimbursed == true && r.SubmissionDate >= d && r.SubmissionDate < d.AddDays(1))
                        .SumAsync(r => (decimal?)r.TotalAmount) ?? 0m;
                    trendData.Add(t);
                }
            }
            else if (totalDays <= 40)
            {
                var wStart = startDate.Date;
                int wNum = 1;
                while (wStart <= endDate.Date)
                {
                    var wEnd = wStart.AddDays(7) > endDate ? endDate.AddDays(1) : wStart.AddDays(7);
                    trendLabels.Add($"Week {wNum}");
                    var t = await _db.ExpenseReports
                        .Where(r => r.Reimbursed == true && r.SubmissionDate >= wStart && r.SubmissionDate < wEnd)
                        .SumAsync(r => (decimal?)r.TotalAmount) ?? 0m;
                    trendData.Add(t);
                    wStart = wEnd;
                    wNum++;
                }
            }
            else
            {
                var cur = new DateTime(startDate.Year, startDate.Month, 1);
                var endMonth = new DateTime(endDate.Year, endDate.Month, 1);
                while (cur <= endMonth)
                {
                    var mEnd = cur.AddMonths(1);
                    trendLabels.Add(cur.ToString("MMM"));
                    var t = await _db.ExpenseReports
                        .Where(r => r.Reimbursed == true && r.SubmissionDate >= cur && r.SubmissionDate < mEnd)
                        .SumAsync(r => (decimal?)r.TotalAmount) ?? 0m;
                    trendData.Add(t);
                    cur = mEnd;
                }
            }

            ViewBag.TrendLabels = System.Text.Json.JsonSerializer.Serialize(trendLabels);
            ViewBag.TrendData = System.Text.Json.JsonSerializer.Serialize(trendData);

            // ── Report Status Distribution Data ──
            var submitted = await _db.ExpenseReports.CountAsync(r => r.Status == ReportStatus.Submitted && r.SubmissionDate >= startDate && r.SubmissionDate <= endDate);
            var approved = await _db.ExpenseReports.CountAsync(r => r.Status == ReportStatus.Approved && r.SubmissionDate >= startDate && r.SubmissionDate <= endDate);
            var rejected = await _db.ExpenseReports.CountAsync(r => r.Status == ReportStatus.Rejected && r.SubmissionDate >= startDate && r.SubmissionDate <= endDate);
            var pendingApproval = await _db.ExpenseReports.CountAsync(r => r.Status == ReportStatus.PendingCEOApproval && r.SubmissionDate >= startDate && r.SubmissionDate <= endDate);

            ViewBag.SubmittedCount = submitted;
            ViewBag.ApprovedCount = approved;
            ViewBag.RejectedCount = rejected;
            ViewBag.PendingApprovalCount = pendingApproval;

            // ── Category Budget Breakdown ──
            var categoryLabels = budgets.Select(b => b.Category).ToList();
            var categoryAllocated = budgets.Select(b => b.Allocated).ToList();
            var categorySpent = budgets.Select(b =>
                spentByCategory.FirstOrDefault(s => s.Category == b.Category)?.Total ?? 0m
            ).ToList();
            ViewBag.CategoryLabels = System.Text.Json.JsonSerializer.Serialize(categoryLabels);
            ViewBag.CategoryAllocated = System.Text.Json.JsonSerializer.Serialize(categoryAllocated);
            ViewBag.CategorySpent = System.Text.Json.JsonSerializer.Serialize(categorySpent);

            // Store filter dates for view
            ViewBag.FilterStart = startDate.ToString("yyyy-MM-dd");
            ViewBag.FilterEnd = endDate.ToString("yyyy-MM-dd");

            // Get current fuel prices
            var fuelPrices = await _fuelPriceService.GetFuelPricesAsync();
            ViewBag.FuelPrices = System.Text.Json.JsonSerializer.Serialize(fuelPrices);

            return View("Dashboard/Index");
        }

        // ───────────── Dashboard AJAX Data ─────────────
        [HttpGet]
        public async Task<IActionResult> GetDashboardData(DateTime? start = null, DateTime? end = null)
        {
            var startDate = start ?? new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var endDate = end ?? DateTime.Now;
            var totalDays = (endDate - startDate).TotalDays;

            var budgets = await _db.Budgets.ToListAsync();
            var totalBudget = budgets.Sum(b => b.Allocated);

            var spentByCategory = await _db.ExpenseItems
                .Where(ei => ei.Report != null && ei.Report.Reimbursed == true
                             && (ei.Date >= startDate && ei.Date <= endDate.AddDays(1)))
                .GroupBy(ei => ei.Category)
                .Select(g => new { Category = g.Key, Total = g.Sum(ei => ei.Amount) })
                .ToListAsync();

            var totalSpent = spentByCategory.Sum(s => s.Total);
            var totalRemaining = totalBudget - totalSpent;

            var submitted = await _db.ExpenseReports.CountAsync(r => r.Status == ReportStatus.Submitted && r.SubmissionDate >= startDate && r.SubmissionDate <= endDate);
            var approved = await _db.ExpenseReports.CountAsync(r => r.Status == ReportStatus.Approved && r.SubmissionDate >= startDate && r.SubmissionDate <= endDate);
            var rejected = await _db.ExpenseReports.CountAsync(r => r.Status == ReportStatus.Rejected && r.SubmissionDate >= startDate && r.SubmissionDate <= endDate);
            var pendingApproval = await _db.ExpenseReports.CountAsync(r => r.Status == ReportStatus.PendingCEOApproval && r.SubmissionDate >= startDate && r.SubmissionDate <= endDate);

            var categoryLabels = budgets.Select(b => b.Category).ToList();
            var categoryAllocated = budgets.Select(b => b.Allocated).ToList();
            var categorySpentList = budgets.Select(b => spentByCategory.FirstOrDefault(s => s.Category == b.Category)?.Total ?? 0m).ToList();

            var trendLabels = new List<string>();
            var trendData = new List<decimal>();

            if (totalDays <= 1)
            {
                for (int h = 0; h < 24; h += 4)
                {
                    var blockStart = startDate.Date.AddHours(h);
                    var blockEnd = blockStart.AddHours(4);
                    trendLabels.Add(blockStart.ToString("h tt"));
                    var t = await _db.ExpenseReports
                        .Where(r => r.Reimbursed == true && r.SubmissionDate >= blockStart && r.SubmissionDate < blockEnd)
                        .SumAsync(r => (decimal?)r.TotalAmount) ?? 0m;
                    trendData.Add(t);
                }
            }
            else if (totalDays <= 7)
            {
                for (var d = startDate.Date; d <= endDate.Date; d = d.AddDays(1))
                {
                    trendLabels.Add(d.ToString("ddd d"));
                    var t = await _db.ExpenseReports
                        .Where(r => r.Reimbursed == true && r.SubmissionDate >= d && r.SubmissionDate < d.AddDays(1))
                        .SumAsync(r => (decimal?)r.TotalAmount) ?? 0m;
                    trendData.Add(t);
                }
            }
            else if (totalDays <= 40)
            {
                var wStart = startDate.Date;
                int wNum = 1;
                while (wStart <= endDate.Date)
                {
                    var wEnd = wStart.AddDays(7) > endDate ? endDate.AddDays(1) : wStart.AddDays(7);
                    trendLabels.Add($"Week {wNum}");
                    var t = await _db.ExpenseReports
                        .Where(r => r.Reimbursed == true && r.SubmissionDate >= wStart && r.SubmissionDate < wEnd)
                        .SumAsync(r => (decimal?)r.TotalAmount) ?? 0m;
                    trendData.Add(t);
                    wStart = wEnd;
                    wNum++;
                }
            }
            else
            {
                var cur = new DateTime(startDate.Year, startDate.Month, 1);
                var endMonth = new DateTime(endDate.Year, endDate.Month, 1);
                while (cur <= endMonth)
                {
                    var mEnd = cur.AddMonths(1);
                    trendLabels.Add(cur.ToString("MMM"));
                    var t = await _db.ExpenseReports
                        .Where(r => r.Reimbursed == true && r.SubmissionDate >= cur && r.SubmissionDate < mEnd)
                        .SumAsync(r => (decimal?)r.TotalAmount) ?? 0m;
                    trendData.Add(t);
                    cur = mEnd;
                }
            }

            return Json(new
            {
                totalBudget,
                totalSpent,
                totalRemaining,
                submittedCount = submitted,
                approvedCount = approved,
                rejectedCount = rejected,
                pendingApprovalCount = pendingApproval,
                categoryLabels,
                categoryAllocated,
                categorySpent = categorySpentList,
                trendLabels,
                trendData
            });
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

            // Calculate spent dynamically for all categories (three-level date fallback)
            var spentByCategory = await _db.ExpenseItems
                .Where(ei => ei.Report != null && ei.Report.Status == ReportStatus.Approved)
                .GroupBy(ei => ei.Category)
                .Select(g => new { Category = g.Key, Total = g.Sum(ei => ei.Amount) })
                .ToListAsync();

            var dto = new List<OverBudgetReportDto>();
            foreach (var r in reports)
            {
                var items = await _db.ExpenseItems.Where(i => i.ReportId == r.Id).ToListAsync();
                decimal exceed = 0m;
                foreach (var group in items.GroupBy(i => i.Category))
                {
                    var budget = budgets.FirstOrDefault(b => b.Category == group.Key);
                    if (budget != null)
                    {
                        // Calculate actual spent for this category (dynamic, not static Budget.Spent)
                        var categorySpentAmount = spentByCategory.FirstOrDefault(s => s.Category == group.Key)?.Total ?? 0m;
                        if (categorySpentAmount > budget.Allocated)
                        {
                            exceed += categorySpentAmount - budget.Allocated;
                        }
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

            _db.AuditLogs.Add(new AuditLog
            {
                Action = "CEOApprove",
                Module = "Expense Reports",
                Role = "CEO",
                PerformedByUserId = _userManager.GetUserId(User),
                RelatedRecordId = id,
                Details = $"CEO approved report #{id}"
            });

            await _db.SaveChangesAsync();

            // Notify driver, managers, and finance about CEO approval
            await _notificationService.NotifyCEOApproved(id, report.UserId);

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
            report.BudgetCheck = BudgetCheckStatus.WithinBudget;

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

            _db.AuditLogs.Add(new AuditLog
            {
                Action = "CEOReject",
                Module = "Expense Reports",
                Role = "CEO",
                PerformedByUserId = _userManager.GetUserId(User),
                RelatedRecordId = id,
                Details = $"CEO rejected report #{id}"
            });

            await _db.SaveChangesAsync();

            // Notify driver and managers about CEO rejection (include reason)
            await _notificationService.NotifyCEORejected(id, report.UserId, remarks);

            TempData["Success"] = "Report rejected by CEO.";
            return RedirectToAction("Approvals");
        }
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

            _db.AuditLogs.Add(new AuditLog
            {
                Action = "ApproveForReimbursement",
                Module = "Expense Reports",
                Role = "CEO",
                PerformedByUserId = _userManager.GetUserId(User),
                RelatedRecordId = id,
                Details = $"CEO quick-approved report #{id} for reimbursement"
            });

            await _db.SaveChangesAsync();

            // Notify driver, managers, and finance about CEO approval
            await _notificationService.NotifyCEOApproved(id, report.UserId);

            TempData["Success"] = "Report approved for reimbursement.";
            return RedirectToAction("Approvals");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectForReimbursement(int id, string? remarks)
        {
            var report = await _db.ExpenseReports.FindAsync(id);
            if (report == null) return NotFound();

            report.CEOApproved = false;
            report.ForwardedToCEO = false;
            report.Status = ReportStatus.Rejected;

            var rejectionRemarks = string.IsNullOrWhiteSpace(remarks) ? "Rejected by CEO" : remarks.Trim();
            var approval = new Approval
            {
                ReportId = id,
                ApprovedByUserId = _userManager.GetUserId(User),
                Status = ApprovalStatus.Rejected,
                DecisionDate = DateTime.UtcNow,
                Stage = "CEO",
                Remarks = rejectionRemarks
            };
            _db.Approvals.Add(approval);

            _db.AuditLogs.Add(new AuditLog
            {
                Action = "RejectForReimbursement",
                Module = "Expense Reports",
                Role = "CEO",
                PerformedByUserId = _userManager.GetUserId(User),
                RelatedRecordId = id,
                Details = $"CEO rejected report #{id} — {rejectionRemarks}"
            });

            await _db.SaveChangesAsync();

            // Notify driver and managers about CEO rejection (include reason)
            await _notificationService.NotifyCEORejected(id, report.UserId, rejectionRemarks);

            TempData["Success"] = "Report rejected by CEO.";
            return RedirectToAction("Approvals");
        }

        // ───────────── Budget Management ─────────────
        public async Task<IActionResult> Budget()
        {
            var budgets = await _db.Budgets.OrderBy(b => b.Category).ToListAsync();

            // Calculate spent dynamically from approved expense items (all-time)
            var spentByCategory = await _db.ExpenseItems
                .Where(ei => ei.Report != null && ei.Report.Status == ReportStatus.Approved)
                .GroupBy(ei => ei.Category)
                .Select(g => new { Category = g.Key, Total = g.Sum(ei => ei.Amount) })
                .ToListAsync();

            // Store the dynamic spent amounts in ViewBag for the view to use
            ViewBag.Budgets = budgets;
            ViewBag.SpentByCategory = spentByCategory.ToDictionary(s => s.Category, s => s.Total);

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

            _db.AuditLogs.Add(new AuditLog
            {
                Action = "CreateBudget",
                Module = "Budget Management",
                Role = "CEO",
                PerformedByUserId = _userManager.GetUserId(User),
                Details = $"Created budget '{category}' with ₱{allocated:N2}"
            });

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

            _db.AuditLogs.Add(new AuditLog
            {
                Action = "EditBudget",
                Module = "Budget Management",
                Role = "CEO",
                PerformedByUserId = _userManager.GetUserId(User),
                RelatedRecordId = budget.Id,
                Details = $"Updated budget '{category}' to ₱{allocated:N2}"
            });

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

            _db.AuditLogs.Add(new AuditLog
            {
                Action = "DeleteBudget",
                Module = "Budget Management",
                Role = "CEO",
                PerformedByUserId = _userManager.GetUserId(User),
                RelatedRecordId = budget.Id,
                Details = $"Deleted budget '{budget.Category}'"
            });

            await _db.SaveChangesAsync();
            TempData["Success"] = $"Budget '{budget.Category}' deleted.";
            return RedirectToAction("Budget");
        }

        [HttpPost]
        [Route("CEO/ToggleBudgetStatus")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleBudgetStatus(int id)
        {
            var budget = await _db.Budgets.FindAsync(id);
            if (budget == null) return NotFound();

            // Check if trying to deactivate a budget with existing expenses
            if (budget.IsActive && !budget.IsActive) // This is always false, but the intent is checking if currently active and trying to deactivate
            {
                // Actually, we need to check: if budget.IsActive is true AND we're trying to toggle it off
                // Since toggle flips the value, we need to check if HasItems BEFORE toggling
                var hasItems = await _db.ExpenseItems.AnyAsync(i => i.Category == budget.Category);
                if (hasItems)
                {
                    TempData["Error"] = $"Cannot deactivate budget '{budget.Category}': there are existing expense items using this category.";
                    return RedirectToAction("Budget");
                }
            }

            // Check if trying to deactivate and there are approved expense items
            if (budget.IsActive) // If currently active
            {
                var hasApprovedItems = await _db.ExpenseItems
                    .Where(i => i.Category == budget.Category && i.Report != null && i.Report.Status == ReportStatus.Approved)
                    .AnyAsync();

                if (hasApprovedItems)
                {
                    TempData["Error"] = $"Cannot deactivate budget '{budget.Category}': there are approved expense items using this category.";
                    return RedirectToAction("Budget");
                }
            }

            budget.IsActive = !budget.IsActive;

            _db.AuditLogs.Add(new AuditLog
            {
                Action = "ToggleBudgetStatus",
                Module = "Budget Management",
                Role = "CEO",
                PerformedByUserId = _userManager.GetUserId(User),
                RelatedRecordId = budget.Id,
                Details = $"Set budget '{budget.Category}' active={budget.IsActive}"
            });

            await _db.SaveChangesAsync();
            TempData["Success"] = $"Budget '{budget.Category}' is now {(budget.IsActive ? "active" : "inactive")}.";
            return RedirectToAction("Budget");
        }

        [HttpPost]
        [Route("CEO/ToggleBudgetStatusAjax")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleBudgetStatusAjax(int id)
        {
            var budget = await _db.Budgets.FindAsync(id);
            if (budget == null) return Json(new { success = false, message = "Budget not found" });

            // Check if trying to deactivate and there are approved expense items
            if (budget.IsActive) // If currently active, trying to deactivate
            {
                var hasApprovedItems = await _db.ExpenseItems
                    .Where(i => i.Category == budget.Category && i.Report != null && i.Report.Status == ReportStatus.Approved)
                    .AnyAsync();

                if (hasApprovedItems)
                {
                    return Json(new { 
                        success = false, 
                        message = $"Cannot deactivate budget '{budget.Category}': there are approved expense items using this category." 
                    });
                }
            }

            budget.IsActive = !budget.IsActive;

            _db.AuditLogs.Add(new AuditLog
            {
                Action = "ToggleBudgetStatusAjax",
                Module = "Budget Management",
                Role = "CEO",
                PerformedByUserId = _userManager.GetUserId(User),
                RelatedRecordId = budget.Id,
                Details = $"Set budget '{budget.Category}' active={budget.IsActive} via AJAX"
            });

            await _db.SaveChangesAsync();

            return Json(new { success = true, isActive = budget.IsActive, message = $"Budget '{budget.Category}' is now {(budget.IsActive ? "active" : "inactive")}." });
        }

        // ───────────── Expense Overview ─────────────
        public async Task<IActionResult> ExpenseOverview(string? category, string? status, DateTime? start, DateTime? end)
        {
            // Only show reimbursed reports by default to match Finance semantics
            var q = _db.ExpenseReports.Include(r => r.Items).Where(r => r.Reimbursed == true).AsQueryable();

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
        public async Task<IActionResult> Analytics(DateTime? start = null, DateTime? end = null)
        {
            var budgets = await _db.Budgets.ToListAsync();

            // Store filter dates for view (if provided)
            ViewBag.FilterStart = start?.ToString("yyyy-MM-dd");
            ViewBag.FilterEnd = end?.ToString("yyyy-MM-dd");

            // Calculate spent dynamically from approved expense items (optionally filtered by submission date range)
            var itemsQuery = _db.ExpenseItems.Where(ei => ei.Report != null && ei.Report.Status == ReportStatus.Approved).AsQueryable();
            if (start.HasValue && end.HasValue)
            {
                var startDt = start.Value.Date;
                var endDt = end.Value.Date.AddDays(1).AddTicks(-1);
                itemsQuery = itemsQuery.Where(ei => ei.Report.SubmissionDate >= startDt && ei.Report.SubmissionDate <= endDt);
            }
            var spentByCategory = await itemsQuery
                .GroupBy(ei => ei.Category)
                .Select(g => new { Category = g.Key, Total = g.Sum(ei => ei.Amount) })
                .ToListAsync();

            // Project budgets with dynamic spent amounts
            var dynamicBudgets = budgets.Select(b => new
            {
                b.Id,
                b.Category,
                b.Allocated,
                Spent = spentByCategory.FirstOrDefault(s => s.Category == b.Category)?.Total ?? 0m,
                b.IsActive
            }).ToList();

            ViewBag.Budgets = dynamicBudgets;

            // Use optional date filter when counting reports so charts reflect the active range
            var reportQueryForCounts = _db.ExpenseReports.AsQueryable();
            if (start.HasValue)
                reportQueryForCounts = reportQueryForCounts.Where(r => r.SubmissionDate >= start.Value);
            if (end.HasValue)
            {
                var endDt = end.Value.Date.AddDays(1).AddTicks(-1);
                reportQueryForCounts = reportQueryForCounts.Where(r => r.SubmissionDate <= endDt);
            }

            var totalReports = await reportQueryForCounts.CountAsync();
            var approvedCount = await reportQueryForCounts.Where(r => r.Status == ReportStatus.Approved).CountAsync();
            var rejectedCount = await reportQueryForCounts.Where(r => r.Status == ReportStatus.Rejected).CountAsync();
            var pendingCount = await reportQueryForCounts.Where(r => r.Status == ReportStatus.Submitted || r.Status == ReportStatus.PendingCEOApproval).CountAsync();

            ViewBag.TotalReports = totalReports;
            ViewBag.ApprovedCount = approvedCount;
            ViewBag.RejectedCount = rejectedCount;
            ViewBag.PendingCount = pendingCount;

            // Monthly reimbursed within filter range or current month if not provided
            DateTime monthRangeStart;
            DateTime monthRangeEnd;
            if (start.HasValue && end.HasValue)
            {
                monthRangeStart = start.Value.Date;
                monthRangeEnd = end.Value.Date.AddDays(1).AddTicks(-1);
            }
            else
            {
                monthRangeStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
                monthRangeEnd = DateTime.UtcNow;
            }
            var monthlyReimbursed = await _db.ExpenseReports
                .Where(r => r.Reimbursed && r.SubmissionDate >= monthRangeStart && r.SubmissionDate <= monthRangeEnd)
                .SumAsync(r => (decimal?)r.TotalAmount) ?? 0m;
            ViewBag.MonthlyReimbursed = monthlyReimbursed;

            return View("Analytics/Index");
        }

        [HttpGet]
        public async Task<IActionResult> Metrics(DateTime? start = null, DateTime? end = null)
        {
            var budgets = await _db.Budgets.ToListAsync();

            // Build base queries filtered by optional date range (submission date)
            var reportQuery = _db.ExpenseReports.AsQueryable();
            if (start.HasValue)
            {
                reportQuery = reportQuery.Where(r => r.SubmissionDate >= start.Value);
            }
            if (end.HasValue)
            {
                var endDt = end.Value.Date.AddDays(1).AddTicks(-1);
                reportQuery = reportQuery.Where(r => r.SubmissionDate <= endDt);
            }

            // Calculate spent dynamically from approved expense items within the filter (if provided)
            var itemsQuery = _db.ExpenseItems.Where(ei => ei.Report != null && ei.Report.Status == ReportStatus.Approved).AsQueryable();
            if (start.HasValue)
                itemsQuery = itemsQuery.Where(ei => ei.Report.SubmissionDate >= start.Value);
            if (end.HasValue)
            {
                var endDt = end.Value.Date.AddDays(1).AddTicks(-1);
                itemsQuery = itemsQuery.Where(ei => ei.Report.SubmissionDate <= endDt);
            }

            var spentByCategory = await itemsQuery
                .GroupBy(ei => ei.Category)
                .Select(g => new { Category = g.Key, Total = g.Sum(ei => ei.Amount) })
                .ToListAsync();

            // Project budgets with dynamic spent amounts
            var budgetData = budgets.Select(b => new
            {
                b.Category,
                b.Allocated,
                Spent = spentByCategory.FirstOrDefault(s => s.Category == b.Category)?.Total ?? 0m
            }).ToList();

            var totalPending = await reportQuery.Where(r => r.Status == ReportStatus.Submitted || r.Status == ReportStatus.PendingCEOApproval).CountAsync();
            var approved = await reportQuery.Where(r => r.Status == ReportStatus.Approved).SumAsync(r => (decimal?)r.TotalAmount) ?? 0m;
            var rejected = await reportQuery.Where(r => r.Status == ReportStatus.Rejected).SumAsync(r => (decimal?)r.TotalAmount) ?? 0m;
            var overBudgetCount = await reportQuery.Where(r => r.BudgetCheck == BudgetCheckStatus.OverBudget).CountAsync();

            // monthly expense trend (last 6 months) - each month is computed from reports intersecting the optional filter
            var months = Enumerable.Range(0, 6).Select(i => DateTime.UtcNow.AddMonths(-i)).Select(d => new { d.Year, d.Month }).Reverse().ToList();
            var monthlyData = new List<object>();
            foreach (var m in months)
            {
                var mStart = new DateTime(m.Year, m.Month, 1);
                var mEnd = mStart.AddMonths(1);
                // Only include reimbursed reports for monthly expense trend (match Finance behavior)
                var q = _db.ExpenseReports.Where(r => r.Reimbursed == true && r.SubmissionDate >= mStart && r.SubmissionDate < mEnd);
                if (start.HasValue)
                    q = q.Where(r => r.SubmissionDate >= start.Value);
                if (end.HasValue)
                {
                    var endDt = end.Value.Date.AddDays(1).AddTicks(-1);
                    q = q.Where(r => r.SubmissionDate <= endDt);
                }
                var total = await q.SumAsync(r => (decimal?)r.TotalAmount) ?? 0m;
                monthlyData.Add(new { label = mStart.ToString("MMM yyyy"), total });
            }

            return Json(new { totalPending, approved, rejected, overBudgetCount, budgets = budgetData, monthlyData });
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
        public async Task<IActionResult> CreateAccount(string email, string password, string fullName, string role,
            string? department, string? licenseNumber,
            string? street, string? barangay, string? city, string? province, string? zipCode, string? country, string? contactNumber)
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
                        Street = street,
                        Barangay = barangay,
                        City = city,
                        Province = province,
                        ZipCode = zipCode,
                        Country = country,
                        ContactNumber = contactNumber,
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
                        Street = street,
                        Barangay = barangay,
                        City = city,
                        Province = province,
                        ZipCode = zipCode,
                        Country = country,
                        ContactNumber = contactNumber,
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
                        Street = street,
                        Barangay = barangay,
                        City = city,
                        Province = province,
                        ZipCode = zipCode,
                        Country = country,
                        ContactNumber = contactNumber,
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
