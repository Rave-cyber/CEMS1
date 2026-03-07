using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using CEMS.Models;
using CEMS.Services;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;

namespace CEMS.Controllers
{
    [Authorize(Roles = "Manager")]
    public class ManagerController : Controller
    {
        private readonly Data.ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly FuelPriceService _fuelPriceService;
        private readonly NotificationService _notificationService;

        public ManagerController(Data.ApplicationDbContext db, UserManager<IdentityUser> userManager, FuelPriceService fuelPriceService, NotificationService notificationService)
        {
            _db = db;
            _userManager = userManager;
            _fuelPriceService = fuelPriceService;
            _notificationService = notificationService;
        }
        
        public async Task<IActionResult> Dashboard(DateTime? start, DateTime? end)
        {
            // Set default date range if not provided (first of current month to today)
            if (!start.HasValue) start = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
            if (!end.HasValue) end = DateTime.UtcNow.Date;

            ViewBag.FilterStart = start?.ToString("yyyy-MM-dd");
            ViewBag.FilterEnd = end?.ToString("yyyy-MM-dd");

            // Get user's full name from ManagerProfile
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                var managerProfile = await _db.Set<ManagerProfile>().FirstOrDefaultAsync(m => m.UserId == userId);
                ViewBag.UserFullName = managerProfile?.FullName ?? User.Identity?.Name ?? "Manager";
            }
            else
            {
                ViewBag.UserFullName = User.Identity?.Name ?? "Manager";
            }

            var pendingReports = await _db.ExpenseReports
                .Where(r => r.Status == ReportStatus.Submitted && r.SubmissionDate >= start && r.SubmissionDate <= end.Value.AddDays(1))
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

            // Budget totals (calculate spent from approved expense items, not static Budget.Spent)
            var budgets = await _db.Budgets.ToListAsync();

            // Calculate actual spent for each category from reimbursed items only (Finance paid)
            // Use item date if available, fall back to report submission date, then to trip start date for old backfilled data
            var spentByCategory = await _db.ExpenseItems
                .Where(ei => ei.Report != null && ei.Report.Reimbursed == true
                             && ((ei.Date >= start && ei.Date <= end.Value.AddDays(1)) 
                                 || (ei.Date == null && ei.Report.SubmissionDate >= start && ei.Report.SubmissionDate <= end.Value.AddDays(1))
                                 || (ei.Date == null && ei.Report.SubmissionDate < start && ei.Report.TripStart >= start && ei.Report.TripStart <= end.Value.AddDays(1))))
                .GroupBy(ei => ei.Category)
                .Select(g => new { Category = g.Key, Total = g.Sum(ei => ei.Amount) })
                .ToListAsync();

            decimal totalSpent = 0m;
            foreach (var budget in budgets)
            {
                var spent = spentByCategory.FirstOrDefault(s => s.Category == budget.Category)?.Total ?? 0m;
                totalSpent += spent;
            }

            ViewBag.TotalBudget = budgets.Sum(b => b.Allocated);
            ViewBag.TotalSpent = totalSpent;
            ViewBag.TotalRemaining = budgets.Sum(b => b.Allocated) - totalSpent;

            // Approved in date range (dynamically calculated from Approvals table)
            var approvedInRange = await _db.Approvals
                .Include(a => a.Report)
                .Where(a => a.Stage == "Manager" && a.Status == ApprovalStatus.Approved && a.DecisionDate.HasValue && a.DecisionDate.Value >= start && a.DecisionDate.Value <= end.Value.AddDays(1))
                .Where(a => a.Report != null) // Ensure report is not null
                .ToListAsync();
            ViewBag.ApprovedTodayCount = approvedInRange.Count;
            ViewBag.ApprovedTodayTotal = approvedInRange.Sum(a => a.Report.TotalAmount);

            // Active drivers
            var driverUsers = await _userManager.GetUsersInRoleAsync("Driver");
            ViewBag.ActiveDrivers = driverUsers.Count;

            // Get current fuel prices
            var fuelPrices = await _fuelPriceService.GetFuelPricesAsync();
            ViewBag.FuelPrices = System.Text.Json.JsonSerializer.Serialize(fuelPrices);

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
            if (end.HasValue) q = q.Where(r => r.SubmissionDate <= end.Value.AddDays(1));

            // Default to "Submitted" status if no status filter is provided
            if (!string.IsNullOrEmpty(status))
            {
                if (Enum.TryParse<ReportStatus>(status, out var st))
                {
                    q = q.Where(r => r.Status == st);
                }
            }
            else
            {
                // Default: show only Submitted (pending review) reports
                q = q.Where(r => r.Status == ReportStatus.Submitted);
            }

            var reports = await q.OrderByDescending(r => r.SubmissionDate).Take(200).ToListAsync();

            var userIds = reports.Select(r => r.UserId).Where(id => id != null).Distinct().ToList();
            var usersDict = await _userManager.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName);

            var dto = reports.Select(r => new PendingExpenseReportDto { Report = r, UserName = r.UserId != null && usersDict.ContainsKey(r.UserId) ? usersDict[r.UserId] : "Unknown" }).ToList();
            ViewBag.Reports = dto;
            return View("Reports/Index");
        }

        // ───────────── Export Reports as PDF ─────────────
        public async Task<IActionResult> ExportReportsPdf(string driver, DateTime? start, DateTime? end, string status)
        {
            var q = _db.ExpenseReports.Include(r => r.User).AsQueryable();

            if (!string.IsNullOrEmpty(driver))
            {
                var users = await _userManager.Users.Where(u => u.UserName.Contains(driver)).Select(u => u.Id).ToListAsync();
                q = q.Where(r => r.UserId != null && users.Contains(r.UserId));
            }
            if (start.HasValue) q = q.Where(r => r.SubmissionDate >= start.Value);
            if (end.HasValue) q = q.Where(r => r.SubmissionDate <= end.Value.AddDays(1));

            if (!string.IsNullOrEmpty(status))
            {
                if (Enum.TryParse<ReportStatus>(status, out var st))
                {
                    q = q.Where(r => r.Status == st);
                }
            }
            else
            {
                q = q.Where(r => r.Status == ReportStatus.Submitted);
            }

            var reports = await q.OrderByDescending(r => r.SubmissionDate).ToListAsync();

            var userIds = reports.Select(r => r.UserId).Where(id => id != null).Distinct().ToList();
            var usersDict = await _userManager.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName);

            // Create PDF
            using (var memoryStream = new MemoryStream())
            {
                var writer = new PdfWriter(memoryStream);
                var pdf = new PdfDocument(writer);
                var document = new Document(pdf);

                // Title
                var title = new Paragraph("Expense Reports")
                    .SetFontSize(18)
                    .SetBold()
                    .SetMarginBottom(10);
                document.Add(title);

                // Report info
                var reportInfo = new Paragraph()
                    .Add($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n")
                    .Add($"Total Records: {reports.Count}\n")
                    .SetFontSize(10)
                    .SetMarginBottom(15);
                if (!string.IsNullOrEmpty(driver))
                    reportInfo.Add($"Driver Filter: {driver}\n");
                if (start.HasValue)
                    reportInfo.Add($"Date From: {start:yyyy-MM-dd}\n");
                if (end.HasValue)
                    reportInfo.Add($"Date To: {end:yyyy-MM-dd}\n");
                if (!string.IsNullOrEmpty(status))
                    reportInfo.Add($"Status: {status}\n");
                document.Add(reportInfo);

                // Table
                var table = new Table(UnitValue.CreatePercentArray(new[] { 12f, 15f, 12f, 12f, 12f, 12f, 12f, 15f }));
                table.SetWidth(UnitValue.CreatePercentValue(100));

                // Table headers
                var headers = new[] { "Report #", "Driver", "Amount", "Status", "Budget", "Submitted", "Approved", "Reimbursed" };
                foreach (var header in headers)
                {
                    var cell = new Cell()
                        .Add(new Paragraph(header).SetBold())
                        .SetBackgroundColor(new iText.Kernel.Colors.DeviceRgb(240, 243, 247))
                        .SetPadding(8);
                    table.AddHeaderCell(cell);
                }

                // Table rows
                foreach (var report in reports)
                {
                    var driverName = report.UserId != null && usersDict.ContainsKey(report.UserId)
                        ? usersDict[report.UserId] ?? "Unknown"
                        : "Unknown";

                    var approvalDate = "—";
                    var reimbursedText = report.Reimbursed ? "Yes" : "No";

                    table.AddCell(new Cell().Add(new Paragraph($"#{report.Id}").SetFontSize(9)));
                    table.AddCell(new Cell().Add(new Paragraph(driverName).SetFontSize(9)));
                    table.AddCell(new Cell().Add(new Paragraph($"₱{report.TotalAmount:N2}").SetFontSize(9)));
                    table.AddCell(new Cell().Add(new Paragraph(report.Status.ToString()).SetFontSize(9)));
                    table.AddCell(new Cell().Add(new Paragraph(report.BudgetCheck.ToString()).SetFontSize(9)));
                    table.AddCell(new Cell().Add(new Paragraph(report.SubmissionDate.ToString("yyyy-MM-dd")).SetFontSize(9)));
                    table.AddCell(new Cell().Add(new Paragraph(approvalDate).SetFontSize(9)));
                    table.AddCell(new Cell().Add(new Paragraph(reimbursedText).SetFontSize(9)));
                }

                document.Add(table);
                document.Close();

                var bytes = memoryStream.ToArray();
                return File(bytes, "application/pdf", $"ExpenseReports_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
            }
        }

        [HttpGet]
        public async Task<IActionResult> Metrics(DateTime? start, DateTime? end)
        {
            // Set default date range if not provided (first of current month to today)
            if (!start.HasValue) start = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
            if (!end.HasValue) end = DateTime.UtcNow.Date;

            var totalPending = await _db.ExpenseReports
                .Where(r => r.Status == ReportStatus.Submitted && r.SubmissionDate >= start && r.SubmissionDate <= end.Value.AddDays(1))
                .CountAsync();

            var approved = await _db.ExpenseReports
                .Where(r => r.Status == ReportStatus.Approved && r.SubmissionDate >= start && r.SubmissionDate <= end.Value.AddDays(1))
                .SumAsync(r => (decimal?)r.TotalAmount) ?? 0m;

            var rejected = await _db.ExpenseReports
                .Where(r => r.Status == ReportStatus.Rejected && r.SubmissionDate >= start && r.SubmissionDate <= end.Value.AddDays(1))
                .SumAsync(r => (decimal?)r.TotalAmount) ?? 0m;

            var overBudgetCount = await _db.ExpenseReports
                .Where(r => r.BudgetCheck == BudgetCheckStatus.OverBudget && r.Reimbursed == true && r.SubmissionDate >= start && r.SubmissionDate <= end.Value.AddDays(1))
                .CountAsync();

            // Approved by this manager in the date range (dynamically calculated)
            var approvedByManagerRaw = await _db.Approvals
                .Include(a => a.Report)
                .Where(a => a.Stage == "Manager" && a.Status == ApprovalStatus.Approved && 
                       a.DecisionDate.HasValue && a.DecisionDate.Value.Date >= start.Value.Date && a.DecisionDate.Value.Date <= end.Value.Date)
                .ToListAsync();
            // Filter out null reports to ensure accurate calculations
            var approvedTodayCount = approvedByManagerRaw.Where(a => a.Report != null).Count();
            var approvedTodayTotal = approvedByManagerRaw.Where(a => a.Report != null).Sum(a => a.Report.TotalAmount);

            // Budget per category with spent amounts for the date range (only approved reports)
            // Filter by expense item date (not submission date) so old backfilled data is included
            var allBudgets = await _db.Budgets.ToListAsync();

            // Sum expense items per category from reimbursed reports only (Finance paid)
            // Use item date if available, fall back to report submission date, then to trip start date for old backfilled data
            var spentByCategory = await _db.ExpenseItems
                .Where(ei => ei.Report != null && ei.Report.Reimbursed == true
                             && ((ei.Date >= start && ei.Date <= end.Value.AddDays(1)) 
                                 || (ei.Date == null && ei.Report.SubmissionDate >= start && ei.Report.SubmissionDate <= end.Value.AddDays(1))
                                 || (ei.Date == null && ei.Report.SubmissionDate < start && ei.Report.TripStart >= start && ei.Report.TripStart <= end.Value.AddDays(1))))
                .GroupBy(ei => ei.Category)
                .Select(g => new { Category = g.Key, Total = g.Sum(ei => ei.Amount) })
                .ToListAsync();

            var budgets = allBudgets.Select(b => new {
                category = b.Category,
                allocated = b.Allocated,
                spent = spentByCategory.FirstOrDefault(s => s.Category == b.Category)?.Total ?? 0m
            }).ToList();

            // Top submitters (by number of reports)
            var topSubmittersRaw = await _db.ExpenseReports
                .Where(r => r.SubmissionDate >= start && r.SubmissionDate <= end.Value.AddDays(1))
                .GroupBy(r => r.UserId)
                .Select(g => new { UserId = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(5)
                .ToListAsync();

            var topSubmitterIds = topSubmittersRaw.Select(t => t.UserId).Where(id => id != null).Distinct().ToList();
            var topSubmitterUsers = await _userManager.Users.Where(u => topSubmitterIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName);
            var topSubmitters = topSubmittersRaw.Select(t => new { username = t.UserId != null && topSubmitterUsers.ContainsKey(t.UserId) ? topSubmitterUsers[t.UserId] : "Unknown", count = t.Count }).ToList();

            // Top reimbursed drivers (by total reimbursed amount)
            var topReimbursedRaw = await _db.ExpenseReports
                .Where(r => r.Reimbursed && r.SubmissionDate >= start && r.SubmissionDate <= end.Value.AddDays(1))
                .GroupBy(r => r.UserId)
                .Select(g => new { UserId = g.Key, Total = g.Sum(x => x.TotalAmount) })
                .OrderByDescending(x => x.Total)
                .Take(5)
                .ToListAsync();

            var topReimbursedIds = topReimbursedRaw.Select(t => t.UserId).Where(id => id != null).Distinct().ToList();
            var topReimbursedUsers = await _userManager.Users.Where(u => topReimbursedIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName);
            var topReimbursed = topReimbursedRaw.Select(t => new { username = t.UserId != null && topReimbursedUsers.ContainsKey(t.UserId) ? topReimbursedUsers[t.UserId] : "Unknown", total = t.Total }).ToList();

            // Report status counts
            var submittedCount = await _db.ExpenseReports
                .Where(r => r.Status == ReportStatus.Submitted && r.SubmissionDate >= start && r.SubmissionDate <= end.Value.AddDays(1))
                .CountAsync();
            var approvedCount = await _db.ExpenseReports
                .Where(r => r.Status == ReportStatus.Approved && r.SubmissionDate >= start && r.SubmissionDate <= end.Value.AddDays(1))
                .CountAsync();
            var rejectedCount = await _db.ExpenseReports
                .Where(r => r.Status == ReportStatus.Rejected && r.SubmissionDate >= start && r.SubmissionDate <= end.Value.AddDays(1))
                .CountAsync();

            // Spending trend with dynamic labels based on date range
            var monthLabels = new List<string>();
            var monthTotals = new List<decimal>();

            var dateSpan = (end.Value - start.Value).Days;

            if (dateSpan <= 1)
            {
                // Daily view: show hourly labels (00:00, 01:00, 02:00, ..., 23:00)
                for (int hour = 0; hour < 24; hour++)
                {
                    monthLabels.Add($"{hour:D2}:00");

                    var hourStart = start.Value.Date.AddHours(hour);
                    var hourEnd = hourStart.AddHours(1).AddSeconds(-1);

                    var mt = await _db.ExpenseReports
                        .Where(r => r.Reimbursed == true && r.SubmissionDate >= hourStart && r.SubmissionDate <= hourEnd)
                        .SumAsync(r => (decimal?)r.TotalAmount) ?? 0m;
                    monthTotals.Add(mt);
                }
            }
            else if (dateSpan <= 7)
            {
                // Weekly view: show 7 days with day names and dates
                var currentDate = start.Value.Date;
                string[] dayNames = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };

                while (currentDate <= end.Value.Date)
                {
                    var dayOfWeek = dayNames[(int)currentDate.DayOfWeek];
                    var dayLabel = $"{dayOfWeek} {currentDate:M/d}";

                    monthLabels.Add(dayLabel);
                    var dayEnd = currentDate.AddDays(1).AddSeconds(-1);

                    var mt = await _db.ExpenseReports
                        .Where(r => r.Reimbursed == true && r.SubmissionDate >= currentDate && r.SubmissionDate <= dayEnd)
                        .SumAsync(r => (decimal?)r.TotalAmount) ?? 0m;
                    monthTotals.Add(mt);

                    currentDate = currentDate.AddDays(1);
                }
            }
            else if (dateSpan <= 31)
            {
                // Monthly view: show weeks within the selected month
                var currentDate = start.Value.Date;
                int weekNum = 1;

                while (currentDate <= end.Value.Date)
                {
                    var weekEnd = currentDate.AddDays(7).AddDays(-1);
                    if (weekEnd > end.Value.Date) weekEnd = end.Value.Date;

                    monthLabels.Add($"Week {weekNum}");
                    var mt = await _db.ExpenseReports
                        .Where(r => r.Reimbursed == true && r.SubmissionDate >= currentDate && r.SubmissionDate <= weekEnd.AddDays(1))
                        .SumAsync(r => (decimal?)r.TotalAmount) ?? 0m;
                    monthTotals.Add(mt);

                    currentDate = weekEnd.AddDays(1);
                    weekNum++;
                }
            }
            else
            {
                // Yearly/large range view: show last 6 months (original behavior)
                for (int i = 5; i >= 0; i--)
                {
                    var ms = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1).AddMonths(-i);
                    var me = ms.AddMonths(1).AddDays(-1);
                    monthLabels.Add(ms.ToString("MMM"));
                    var mt = await _db.ExpenseReports
                        .Where(r => r.Reimbursed == true && r.SubmissionDate >= ms && r.SubmissionDate <= me)
                        .SumAsync(r => (decimal?)r.TotalAmount) ?? 0m;
                    monthTotals.Add(mt);
                }
            }

            return Json(new { totalPending, approved, rejected, overBudgetCount, approvedTodayCount, approvedTodayTotal, budgets, topSubmitters, topReimbursed, submittedCount, approvedCount, rejectedCount, monthLabels, monthTotals });
        }

        public async Task<IActionResult> OverBudget(DateTime? start, DateTime? end)
        {
            // Set default date range if not provided
            if (!start.HasValue) start = DateTime.UtcNow.AddMonths(-1).Date;
            if (!end.HasValue) end = DateTime.UtcNow.Date;

            ViewBag.FilterStart = start?.ToString("yyyy-MM-dd");
            ViewBag.FilterEnd = end?.ToString("yyyy-MM-dd");

            // Get over-budget reports that need action (Submitted) or are already forwarded (PendingCEOApproval)
            var reports = await _db.ExpenseReports
                .Where(r => r.BudgetCheck == BudgetCheckStatus.OverBudget &&
                            (r.Status == ReportStatus.Submitted || r.Status == ReportStatus.PendingCEOApproval) &&
                            r.SubmissionDate >= start && 
                            r.SubmissionDate <= end.Value.AddDays(1))
                .OrderByDescending(r => r.SubmissionDate)
                .ToListAsync();

            var userIds = reports.Select(r => r.UserId).Where(id => id != null).Distinct().ToList();
            var users = await _userManager.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName);

            var list = new List<OverBudgetReportDto>();
            foreach(var r in reports)
            {
                // Get items from this report only
                var items = await _db.ExpenseItems.Where(i => i.ReportId == r.Id).ToListAsync();
                decimal exceed = 0m;

                foreach(var group in items.GroupBy(i => i.Category))
                {
                    var cat = group.Key;
                    var total = group.Sum(i => i.Amount);
                    var budget = await _db.Budgets.FirstOrDefaultAsync(b => b.Category == cat);

                    if (budget != null)
                    {
                        // Calculate spent amount from over-budget reports in the date range
                        // Use item date if available, fall back to report submission date, then to trip start date for old data
                        var currentSpent = await _db.ExpenseItems
                            .Where(ii => ii.Category == cat && 
                                         ((ii.Date >= start && ii.Date <= end.Value.AddDays(1))
                                          || (ii.Date == null && ii.Report.SubmissionDate >= start && ii.Report.SubmissionDate <= end.Value.AddDays(1))
                                          || (ii.Date == null && ii.Report.SubmissionDate < start && ii.Report.TripStart >= start && ii.Report.TripStart <= end.Value.AddDays(1))) &&
                                         (ii.Report.Status == ReportStatus.Submitted || ii.Report.Status == ReportStatus.PendingCEOApproval || ii.Report.Status == ReportStatus.Approved) &&
                                         ii.Report.BudgetCheck == BudgetCheckStatus.OverBudget)
                            .SumAsync(ii => (decimal?)ii.Amount) ?? 0m;

                        if (currentSpent > budget.Allocated)
                        {
                            exceed += currentSpent - budget.Allocated;
                        }
                    }
                }

                list.Add(new OverBudgetReportDto 
                { 
                    Report = r, 
                    UserName = r.UserId != null && users.ContainsKey(r.UserId) ? users[r.UserId] : "Unknown", 
                    ExceedAmount = exceed 
                });
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
            // Load all budgets (matching CEO's approach for consistency)
            var budgets = await _db.Budgets.OrderBy(b => b.Category).ToListAsync();

            // Calculate actual spending from reimbursed expense items only (Finance paid)
            // This ensures the summary always shows accurate spent amounts
            var spentByCategory = await _db.ExpenseItems
                .Where(ei => ei.Category != null && ei.Report != null 
                             && ei.Report.Reimbursed == true)
                .GroupBy(ei => ei.Category)
                .Select(g => new { Category = g.Key, Total = g.Sum(ei => ei.Amount) })
                .ToListAsync();

            // Set spent amount for each budget from the calculated spending
            foreach (var budget in budgets)
            {
                budget.Spent = spentByCategory.FirstOrDefault(s => s.Category == budget.Category)?.Total ?? 0m;
            }

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

            // If over budget, do NOT set Approved — forward to CEO instead
            if (report.BudgetCheck == BudgetCheckStatus.OverBudget)
            {
                report.ForwardedToCEO = true;
                report.Status = ReportStatus.PendingCEOApproval;

                var managerApproval = new Models.Approval
                {
                    ReportId = report.Id,
                    ApprovedByUserId = _userManager.GetUserId(User),
                    Status = Models.ApprovalStatus.Approved,
                    DecisionDate = DateTime.UtcNow,
                    Stage = "Manager",
                    Remarks = "Forwarded to CEO for over-budget approval"
                };
                _db.Approvals.Add(managerApproval);

                _db.AuditLogs.Add(new AuditLog
                {
                    Action = "ForwardToCEO",
                    Module = "Expense Reports",
                    Role = "Manager",
                    PerformedByUserId = _userManager.GetUserId(User),
                    RelatedRecordId = report.Id,
                    Details = $"Forwarded over-budget report #{report.Id} (₱{report.TotalAmount:N2}) to CEO for final approval"
                });

                await _db.SaveChangesAsync();

                await _notificationService.NotifyReportApprovedByManager(report.Id, report.UserId, true);
                await _notificationService.NotifyReportForwardedToCEO(report.Id, report.TotalAmount);

                TempData["Success"] = "Over-budget report forwarded to CEO for final approval.";
            }
            else
            {
                // Within budget — approve and send to Finance
                report.Status = ReportStatus.Approved;
                report.Reimbursed = false;
                report.ForwardedToCEO = false;

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

                _db.AuditLogs.Add(new AuditLog
                {
                    Action = "ApproveReport",
                    Module = "Expense Reports",
                    Role = "Manager",
                    PerformedByUserId = _userManager.GetUserId(User),
                    RelatedRecordId = report.Id,
                    Details = $"Approved expense report #{report.Id} (₱{report.TotalAmount:N2}) — sent to Finance"
                });

                await _db.SaveChangesAsync();

                await _notificationService.NotifyReportApprovedByManager(report.Id, report.UserId, false);

                TempData["Success"] = "Report approved and sent to Finance for reimbursement.";
            }

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return Json(new { success = true, message = TempData["Success"]?.ToString() });
            return RedirectToAction("Reports");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectReport(int id, string? remarks, string? returnUrl)
        {
            var report = await _db.ExpenseReports.FindAsync(id);
            if (report == null) return NotFound();

            report.Status = ReportStatus.Rejected;
            report.BudgetCheck = BudgetCheckStatus.WithinBudget;
            // record manager rejection with reason
            var rejectionRemarks = string.IsNullOrWhiteSpace(remarks) ? "Rejected by manager" : remarks.Trim();
            var rejection = new Models.Approval
            {
                ReportId = report.Id,
                ApprovedByUserId = _userManager.GetUserId(User),
                Status = Models.ApprovalStatus.Rejected,
                DecisionDate = DateTime.UtcNow,
                Stage = "Manager",
                Remarks = rejectionRemarks
            };
            _db.Approvals.Add(rejection);

            _db.AuditLogs.Add(new AuditLog
            {
                Action = "RejectReport",
                Module = "Expense Reports",
                Role = "Manager",
                PerformedByUserId = _userManager.GetUserId(User),
                RelatedRecordId = report.Id,
                Details = $"Rejected expense report #{report.Id} — {rejectionRemarks}"
            });

            await _db.SaveChangesAsync();

            // Notify the driver about rejection (include reason)
            await _notificationService.NotifyReportRejectedByManager(report.Id, report.UserId, rejectionRemarks);

            TempData["Success"] = "Report rejected.";

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            var referer = Request.Headers["Referer"].ToString();
            if (!string.IsNullOrEmpty(referer) && Url.IsLocalUrl(referer))
                return Redirect(referer);

            return RedirectToAction("Reports");
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

            _db.AuditLogs.Add(new AuditLog
            {
                Action = "ForwardToCEO",
                Module = "Expense Reports",
                Role = "Manager",
                PerformedByUserId = _userManager.GetUserId(User),
                RelatedRecordId = report.Id,
                Details = $"Forwarded report #{report.Id} to CEO for approval"
            });

            await _db.SaveChangesAsync();

            // Notify CEO about the forwarded report
            await _notificationService.NotifyReportForwardedToCEO(report.Id, report.TotalAmount);

            TempData["Success"] = "Report forwarded to CEO for approval.";
            return RedirectToAction("Dashboard");
        }
    }
}