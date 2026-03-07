using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using CEMS.Models;
using CEMS.Services;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
using iText.Kernel.Font;
using iText.IO.Font.Constants;
using iText.Kernel.Geom;

namespace CEMS.Controllers
{
    [Authorize(Roles = "Finance")]
    public class FinanceController : Controller
    {
        private readonly Data.ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IPayMongoService _payMongo;
        private readonly NotificationService _notificationService;

        public FinanceController(Data.ApplicationDbContext db, UserManager<IdentityUser> userManager, IPayMongoService payMongo, NotificationService notificationService)
        {
            _db = db;
            _userManager = userManager;
            _payMongo = payMongo;
            _notificationService = notificationService;
        }

        // Dashboard: list reports ready for reimbursement with optional date filter
        // Defaults to current month when no dates are provided
        public async Task<IActionResult> Dashboard(DateTime? start, DateTime? end)
        {
            var now = DateTime.UtcNow.Date;
            var startDate = start?.Date ?? new DateTime(now.Year, now.Month, 1);
            var endDate = end?.Date ?? now;
            var endExclusive = endDate.AddDays(1);

            // Get user's full name from FinanceProfile
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                var financeProfile = await _db.Set<FinanceProfile>().FirstOrDefaultAsync(f => f.UserId == userId);
                ViewBag.UserFullName = financeProfile?.FullName ?? User.Identity?.Name ?? "Finance";
            }
            else
            {
                ViewBag.UserFullName = User.Identity?.Name ?? "Finance";
            }

            // Only show reports approved by manager and cleared for finance in the date range
            var reportsQuery = _db.ExpenseReports
                .Include(r => r.Items)
                .Where(r => r.Status == ReportStatus.Approved && !r.Reimbursed)
                .Where(r => (r.BudgetCheck == BudgetCheckStatus.WithinBudget) || (r.CEOApproved == true))
                .Where(r => r.SubmissionDate >= startDate && r.SubmissionDate < endExclusive)
                .OrderByDescending(r => r.SubmissionDate);

            var reports = await reportsQuery.ToListAsync();

            var reportUserIds = reports.Select(r => r.UserId).Where(id => id != null).Distinct().ToList();
            var userIds = reportUserIds; // legacy alias kept for compatibility
            var users = await _userManager.Users.Where(u => reportUserIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName);

            // Also load profile display names (Driver/Manager/CEO/Finance) to show full names instead of emails
            var profileNames = new Dictionary<string, string>();
            var driverProfiles = await _db.DriverProfiles.Where(p => reportUserIds.Contains(p.UserId)).ToListAsync();
            foreach (var p in driverProfiles) profileNames[p.UserId] = p.FullName ?? p.User?.UserName ?? p.UserId;
            var managerProfiles = await _db.ManagerProfiles.Where(p => reportUserIds.Contains(p.UserId)).ToListAsync();
            foreach (var p in managerProfiles) profileNames[p.UserId] = p.FullName ?? p.User?.UserName ?? p.UserId;
            var financeProfiles = await _db.FinanceProfiles.Where(p => reportUserIds.Contains(p.UserId)).ToListAsync();
            foreach (var p in financeProfiles) profileNames[p.UserId] = p.FullName ?? p.User?.UserName ?? p.UserId;
            var ceoProfiles = await _db.CEOProfiles.Where(p => reportUserIds.Contains(p.UserId)).ToListAsync();
            foreach (var p in ceoProfiles) profileNames[p.UserId] = p.FullName ?? p.User?.UserName ?? p.UserId;

            ViewBag.UserFullNames = profileNames;

            var dto = reports.Select(r => new PendingExpenseReportDto { Report = r, UserName = r.UserId != null && users.ContainsKey(r.UserId) ? users[r.UserId] : "Unknown" }).ToList();
            ViewBag.Reports = dto;
            ViewBag.PendingCount = dto.Count;
            ViewBag.TotalToProcess = dto.Sum(x => x.Report.TotalAmount);

            // processed in the selected range: finance approvals whose DecisionDate falls inside
            var processedQuery = _db.Approvals
                .Include(a => a.Report)
                .Where(a => a.Stage == "Finance" && a.Status == ApprovalStatus.Approved && a.DecisionDate.HasValue && a.DecisionDate.Value >= startDate && a.DecisionDate.Value < endExclusive);

            var processedTodayCount = await processedQuery.CountAsync();
            var processedTodayTotal = await processedQuery.SumAsync(a => (decimal?)(a.Report != null ? a.Report.TotalAmount : 0m)) ?? 0m;

            // totals for the range (kept in ViewBag as Monthly* for backward-compat with the view)
            var rangeCount = processedTodayCount;
            var rangeTotal = processedTodayTotal;

            ViewBag.ProcessedTodayTotal = processedTodayTotal;
            ViewBag.ProcessedTodayCount = processedTodayCount;
            ViewBag.MonthlyTotal = rangeTotal;
            ViewBag.MonthlyCount = rangeCount;

            // expose selected start/end for the view so inputs can be prefilled
            ViewBag.FilterStart = startDate.ToString("yyyy-MM-dd");
            ViewBag.FilterEnd = endDate.ToString("yyyy-MM-dd");

            return View("Dashboard/Index");
        }

        // Metrics endpoint used by the dashboard to load dynamic numbers/charts
        [HttpGet]
        public async Task<IActionResult> Metrics(DateTime? start, DateTime? end)
        {
            var now = DateTime.UtcNow.Date;
            var startDate = start?.Date ?? new DateTime(now.Year, now.Month, 1);
            var endDate = end?.Date ?? now;
            var endExclusive = endDate.AddDays(1);

            var processedQuery = _db.Approvals
                .Include(a => a.Report)
                .Where(a => a.Stage == "Finance" && a.Status == ApprovalStatus.Approved && a.DecisionDate.HasValue && a.DecisionDate.Value >= startDate && a.DecisionDate.Value < endExclusive);

            var processedCount = await processedQuery.CountAsync();
            var processedTotal = await processedQuery.SumAsync(a => (decimal?)(a.Report != null ? a.Report.TotalAmount : 0m)) ?? 0m;

            // to-process reports in the selected range
            var toProcessQuery = _db.ExpenseReports
                .Where(r => r.Status == ReportStatus.Approved && !r.Reimbursed)
                .Where(r => (r.BudgetCheck == BudgetCheckStatus.WithinBudget) || (r.CEOApproved == true))
                .Where(r => r.SubmissionDate >= startDate && r.SubmissionDate < endExclusive);
            var toProcessCount = await toProcessQuery.CountAsync();
            var toProcessTotal = await toProcessQuery.SumAsync(r => (decimal?)r.TotalAmount) ?? 0m;

            // monthly trend for the selected range (group by month). If the range is empty, defaults to current month range above.
            var monthlyData = new List<object>();
            // build list of month starts between startDate and endDate
            var monthCursor = new DateTime(startDate.Year, startDate.Month, 1);
            var lastMonth = new DateTime(endDate.Year, endDate.Month, 1);
            while (monthCursor <= lastMonth)
            {
                var mStart = monthCursor;
                var mEnd = mStart.AddMonths(1);
                var total = await _db.Approvals
                    .Include(a => a.Report)
                    .Where(a => a.Stage == "Finance" && a.Status == ApprovalStatus.Approved && a.DecisionDate.HasValue && a.DecisionDate.Value >= mStart && a.DecisionDate.Value < mEnd)
                    .SumAsync(a => (decimal?)(a.Report != null ? a.Report.TotalAmount : 0m)) ?? 0m;
                monthlyData.Add(new { label = mStart.ToString("MMM yyyy"), total });
                monthCursor = monthCursor.AddMonths(1);
            }

            // category breakdown for reimbursed reports
            // category breakdown for reimbursed reports within the selected date range
            var categoryData = await _db.ExpenseItems
                .Include(i => i.Report)
                .Where(i => i.Report != null && i.Report.Reimbursed && i.Report.SubmissionDate >= startDate && i.Report.SubmissionDate < endExclusive)
                .GroupBy(i => i.Category)
                .Select(g => new { Category = g.Key, Total = g.Sum(i => (decimal?)i.Amount) ?? 0m })
                .OrderByDescending(x => x.Total)
                .Take(8)
                .ToListAsync();

            // Report status distribution for pie chart
            var statusQuery = _db.ExpenseReports.Where(r => r.SubmissionDate >= startDate && r.SubmissionDate < endExclusive).AsQueryable();
            var submittedCount = await statusQuery.Where(r => r.Status == ReportStatus.Submitted).CountAsync();
            var approvedCount = await statusQuery.Where(r => r.Status == ReportStatus.Approved).CountAsync();
            var rejectedCount = await statusQuery.Where(r => r.Status == ReportStatus.Rejected).CountAsync();
            var pendingCount = await statusQuery.Where(r => r.Status == ReportStatus.PendingCEOApproval).CountAsync();

            return Json(new
            {
                toProcess = toProcessTotal,
                toProcessCount = toProcessCount,
                processedToday = processedTotal,
                processedCount = processedCount,
                monthlyTotal = processedTotal,
                monthlyCount = processedCount,
                monthlyData,
                categories = categoryData,
                statusCounts = new { submitted = submittedCount, approved = approvedCount, rejected = rejectedCount, pending = pendingCount }
            });
        }

        // List approved reports (visible to finance) — either within budget or CEO approved
        public async Task<IActionResult> ApprovedReports(DateTime? start, DateTime? end, string? reimbursed, string? search)
        {
            var now = DateTime.UtcNow.Date;
            var startDate = start?.Date ?? new DateTime(now.Year, now.Month, 1);
            var endDate = end?.Date ?? now;
            var endExclusive = endDate.AddDays(1);

            // base query: approved and eligible for finance
            var q = _db.ExpenseReports
                .Where(r => r.Status == ReportStatus.Approved && (r.BudgetCheck == BudgetCheckStatus.WithinBudget || r.CEOApproved == true))
                .Where(r => r.SubmissionDate >= startDate && r.SubmissionDate < endExclusive);

            // reimbursed filter: support pending (no payment), initiated (payment record exists), done (report.Reimbursed == true)
            reimbursed = (reimbursed ?? "pending").ToLowerInvariant();
            if (reimbursed == "pending")
            {
                q = q.Where(r => !r.Reimbursed && !_db.ReimbursementPayments.Any(p => p.ReportId == r.Id && p.Status != "expired"));
            }
            else if (reimbursed == "initiated")
            {
                q = q.Where(r => !r.Reimbursed && _db.ReimbursementPayments.Any(p => p.ReportId == r.Id && p.Status != "expired"));
            }
            else if (reimbursed == "done")
            {
                q = q.Where(r => r.Reimbursed);
            }

            var reports = await q.OrderByDescending(r => r.SubmissionDate).ToListAsync();

            // Apply search filter (report id or driver name/full name)
            if (!string.IsNullOrWhiteSpace(search))
            {
                var searchTrim = search.Trim();
                if (int.TryParse(searchTrim, out var idSearch))
                {
                    reports = reports.Where(r => r.Id == idSearch).ToList();
                }
                else
                {
                    var lower = searchTrim.ToLowerInvariant();
                    // build dictionary of all users' usernames to search against
                    var usersById = await _userManager.Users.ToDictionaryAsync(u => u.Id, u => u.UserName ?? "");

                    // collect user ids that match by profile full name
                    var matchingUserIds = new List<string>();
                    var driverProfiles = await _db.DriverProfiles.Where(p => p.FullName != null && p.FullName.ToLower().Contains(lower)).ToListAsync();
                    matchingUserIds.AddRange(driverProfiles.Select(p => p.UserId));
                    var mgrProfiles = await _db.ManagerProfiles.Where(p => p.FullName != null && p.FullName.ToLower().Contains(lower)).ToListAsync();
                    matchingUserIds.AddRange(mgrProfiles.Select(p => p.UserId));
                    var finProfiles = await _db.FinanceProfiles.Where(p => p.FullName != null && p.FullName.ToLower().Contains(lower)).ToListAsync();
                    matchingUserIds.AddRange(finProfiles.Select(p => p.UserId));
                    var ceoProfiles = await _db.CEOProfiles.Where(p => p.FullName != null && p.FullName.ToLower().Contains(lower)).ToListAsync();
                    matchingUserIds.AddRange(ceoProfiles.Select(p => p.UserId));

                    // usernames that match
                    matchingUserIds.AddRange(usersById.Where(kv => (kv.Value ?? string.Empty).ToLower().Contains(lower)).Select(kv => kv.Key));

                    matchingUserIds = matchingUserIds.Distinct().ToList();

                    reports = reports.Where(r => (r.UserId != null && matchingUserIds.Contains(r.UserId)) || (r.SubmissionDate.ToString().ToLower().Contains(lower))).ToList();
                }
            }

            var userIds = reports.Select(r => r.UserId).Where(id => id != null).Distinct().ToList();
            var users = await _userManager.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName);

            var dto = reports.Select(r => new PendingExpenseReportDto { Report = r, UserName = r.UserId != null && users.ContainsKey(r.UserId) ? users[r.UserId] : "Unknown" }).ToList();
            ViewBag.ApprovedReports = dto;

            ViewBag.FilterSearch = search ?? "";

            // load payment records for these reports so the view can show "Initiated" state
            var reportIds = reports.Select(r => r.Id).ToList();
            var payments = await _db.ReimbursementPayments
                .Where(p => reportIds.Contains(p.ReportId))
                .ToListAsync();
            var paymentsMap = payments.GroupBy(p => p.ReportId).ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.CreatedAt).First());
            ViewBag.Payments = paymentsMap;

            // load approvals for these reports so we can show approver names (one-per-stage, latest decision)
            var approvalsList = await _db.Approvals
                .Where(a => reportIds.Contains(a.ReportId) && a.Status == ApprovalStatus.Approved)
                .ToListAsync();

            var approverIds = approvalsList.Select(a => a.ApprovedByUserId).Where(id => id != null).Distinct().ToList();
            var approverUsers = await _userManager.Users.Where(u => approverIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName);

            // load approver profile names for nicer display
            var approverProfileNames = new Dictionary<string, string>();
            if (approverIds.Any())
            {
                var mgrs = await _db.ManagerProfiles.Where(p => approverIds.Contains(p.UserId)).ToListAsync();
                foreach (var p in mgrs) if (!approverProfileNames.ContainsKey(p.UserId)) approverProfileNames[p.UserId] = p.FullName ?? p.User?.UserName ?? p.UserId;

                var fins = await _db.FinanceProfiles.Where(p => approverIds.Contains(p.UserId)).ToListAsync();
                foreach (var p in fins) if (!approverProfileNames.ContainsKey(p.UserId)) approverProfileNames[p.UserId] = p.FullName ?? p.User?.UserName ?? p.UserId;

                var ceos = await _db.CEOProfiles.Where(p => approverIds.Contains(p.UserId)).ToListAsync();
                foreach (var p in ceos) if (!approverProfileNames.ContainsKey(p.UserId)) approverProfileNames[p.UserId] = p.FullName ?? p.User?.UserName ?? p.UserId;

                var drs = await _db.DriverProfiles.Where(p => approverIds.Contains(p.UserId)).ToListAsync();
                foreach (var p in drs) if (!approverProfileNames.ContainsKey(p.UserId)) approverProfileNames[p.UserId] = p.FullName ?? p.User?.UserName ?? p.UserId;
            }

            // Build mapping: ReportId -> (Stage -> approverName)
            var approvalsMap = new Dictionary<int, Dictionary<string, string>>();
            var groupedByReport = approvalsList.GroupBy(a => a.ReportId);
            foreach (var grp in groupedByReport)
            {
                var stageMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var byStage = grp.GroupBy(a => (a.Stage ?? string.Empty).Trim());
                foreach (var sgrp in byStage)
                {
                    // pick latest approval for the stage
                    var latest = sgrp.OrderByDescending(a => a.DecisionDate).FirstOrDefault();
                    if (latest == null) continue;
                    string name = "Unknown";
                    if (!string.IsNullOrEmpty(latest.ApprovedByUserId))
                    {
                        if (approverProfileNames.ContainsKey(latest.ApprovedByUserId)) name = approverProfileNames[latest.ApprovedByUserId];
                        else if (approverUsers.ContainsKey(latest.ApprovedByUserId)) name = approverUsers[latest.ApprovedByUserId];
                        else name = latest.ApprovedByUserId;
                    }
                    stageMap[sgrp.Key] = name;
                }
                approvalsMap[grp.Key] = stageMap;
            }
            ViewBag.ReportApprovals = approvalsMap;
            ViewBag.FilterStart = startDate.ToString("yyyy-MM-dd");
            ViewBag.FilterEnd = endDate.ToString("yyyy-MM-dd");
            ViewBag.FilterReimbursed = reimbursed;
            return View("ApprovedReports/Index");
        }

        // Reimbursements list (same as dashboard but explicit view)
        public async Task<IActionResult> Reimbursements(string? search, DateTime? start, DateTime? end)
        {
            var q = _db.ExpenseReports
                .Where(r => r.Status == ReportStatus.Approved && !r.Reimbursed && (r.BudgetCheck == BudgetCheckStatus.WithinBudget || r.CEOApproved == true));

            // Date range filter (optional)
            if (start.HasValue || end.HasValue)
            {
                var s = start?.Date ?? DateTime.MinValue;
                var e = end?.Date ?? DateTime.UtcNow.Date;
                var eExclusive = e.AddDays(1);
                q = q.Where(r => r.SubmissionDate >= s && r.SubmissionDate < eExclusive);
            }

            // Apply search (report id or driver / profile name)
            if (!string.IsNullOrWhiteSpace(search))
            {
                var searchTrim = search.Trim();
                if (int.TryParse(searchTrim, out var idSearch))
                {
                    q = q.Where(r => r.Id == idSearch);
                }
                else
                {
                    var lower = searchTrim.ToLowerInvariant();
                    var usersById = await _userManager.Users.ToDictionaryAsync(u => u.Id, u => u.UserName ?? "");

                    var matchingUserIds = new List<string>();
                    var driverProfiles = await _db.DriverProfiles.Where(p => p.FullName != null && p.FullName.ToLower().Contains(lower)).ToListAsync();
                    matchingUserIds.AddRange(driverProfiles.Select(p => p.UserId));
                    var mgrProfiles = await _db.ManagerProfiles.Where(p => p.FullName != null && p.FullName.ToLower().Contains(lower)).ToListAsync();
                    matchingUserIds.AddRange(mgrProfiles.Select(p => p.UserId));
                    var finProfiles = await _db.FinanceProfiles.Where(p => p.FullName != null && p.FullName.ToLower().Contains(lower)).ToListAsync();
                    matchingUserIds.AddRange(finProfiles.Select(p => p.UserId));
                    var ceoProfiles = await _db.CEOProfiles.Where(p => p.FullName != null && p.FullName.ToLower().Contains(lower)).ToListAsync();
                    matchingUserIds.AddRange(ceoProfiles.Select(p => p.UserId));

                    matchingUserIds.AddRange(usersById.Where(kv => (kv.Value ?? string.Empty).ToLower().Contains(lower)).Select(kv => kv.Key));
                    matchingUserIds = matchingUserIds.Distinct().ToList();

                    q = q.Where(r => r.UserId != null && matchingUserIds.Contains(r.UserId));
                }
            }

            var reports = await q.OrderByDescending(r => r.SubmissionDate).ToListAsync();

            var userIds = reports.Select(r => r.UserId).Where(id => id != null).Distinct().ToList();
            var users = await _userManager.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName);

            var dto = reports.Select(r => new PendingExpenseReportDto { Report = r, UserName = r.UserId != null && users.ContainsKey(r.UserId) ? users[r.UserId] : "Unknown" }).ToList();
            ViewBag.Reports = dto;
            ViewBag.FilterSearch = search ?? "";
            ViewBag.FilterStart = start?.ToString("yyyy-MM-dd") ?? "";
            ViewBag.FilterEnd = end?.ToString("yyyy-MM-dd") ?? "";

            // Load existing payment links for these reports
            var reportIds = reports.Select(r => r.Id).ToList();
            var payments = await _db.ReimbursementPayments
                .Where(p => reportIds.Contains(p.ReportId))
                .ToListAsync();
            ViewBag.Payments = payments
                .GroupBy(p => p.ReportId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.CreatedAt).First());

            return View("Reimbursements/Index");
        }

        public IActionResult ReportDetails(int id)
        {
            // ReportDetails view removed. Redirect back to ApprovedReports list.
            return RedirectToAction("ApprovedReports");
        }

        // Payment history: fetched from ReimbursementPayments table (no webhook needed)
        public async Task<IActionResult> Payments(string? search, string? status, DateTime? start, DateTime? end, int page = 1, int pageSize = 10)
        {
            var query = _db.ReimbursementPayments
                .Include(p => p.Report)
                .AsQueryable();

            // Status filter
            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(p => p.Status == status);

            // Date range filter (on CreatedAt)
            if (start.HasValue)
                query = query.Where(p => p.CreatedAt >= start.Value.Date);
            if (end.HasValue)
                query = query.Where(p => p.CreatedAt < end.Value.Date.AddDays(1));

            // Search filter (report ID or PayMongo link ID)
            if (!string.IsNullOrWhiteSpace(search))
            {
                if (int.TryParse(search.Trim(), out var searchId))
                    query = query.Where(p => p.ReportId == searchId);
                else
                    query = query.Where(p => p.PayMongoLinkId.Contains(search.Trim()));
            }

            var totalCount = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            page = Math.Max(1, Math.Min(page, Math.Max(1, totalPages)));

            var payments = await query
                .OrderByDescending(p => p.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Resolve driver (Report.UserId) and processor user names
            var driverIds = payments.Where(p => p.Report?.UserId != null).Select(p => p.Report!.UserId!).Distinct().ToList();
            var processorIds = payments.Where(p => p.ProcessedByUserId != null).Select(p => p.ProcessedByUserId!).Distinct().ToList();
            var allUserIds = driverIds.Union(processorIds).Distinct().ToList();
            var userMap = await _userManager.Users
                .Where(u => allUserIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.UserName ?? u.Email ?? "Unknown");

            ViewBag.PaymentList = payments;
            ViewBag.UserMap = userMap;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalCount = totalCount;
            ViewBag.Search = search ?? "";
            ViewBag.StatusFilter = status ?? "";
            ViewBag.FilterStart = start?.ToString("yyyy-MM-dd") ?? "";
            ViewBag.FilterEnd = end?.ToString("yyyy-MM-dd") ?? "";

            return View("Payments/Index");
        }

        // Export payment history as PDF with current filters applied
        public async Task<IActionResult> ExportPayments(string? search, string? status, DateTime? start, DateTime? end)
        {
            var query = _db.ReimbursementPayments
                .Include(p => p.Report)
                .AsQueryable();

            // Status filter
            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(p => p.Status == status);

            // Date range filter (on CreatedAt)
            if (start.HasValue)
                query = query.Where(p => p.CreatedAt >= start.Value.Date);
            if (end.HasValue)
                query = query.Where(p => p.CreatedAt < end.Value.Date.AddDays(1));

            // Search filter (report ID or PayMongo link ID)
            if (!string.IsNullOrWhiteSpace(search))
            {
                if (int.TryParse(search.Trim(), out var searchId))
                    query = query.Where(p => p.ReportId == searchId);
                else
                    query = query.Where(p => p.PayMongoLinkId.Contains(search.Trim()));
            }

            var payments = await query
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();

            // Resolve driver (Report.UserId) and processor user names
            var driverIds = payments.Where(p => p.Report?.UserId != null).Select(p => p.Report!.UserId!).Distinct().ToList();
            var processorIds = payments.Where(p => p.ProcessedByUserId != null).Select(p => p.ProcessedByUserId!).Distinct().ToList();
            var allUserIds = driverIds.Union(processorIds).Distinct().ToList();
            var userMap = await _userManager.Users
                .Where(u => allUserIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.UserName ?? u.Email ?? "Unknown");

            // Generate PDF using iText (improved layout)
            using (var memoryStream = new System.IO.MemoryStream())
            {
                var writer = new PdfWriter(memoryStream);
                var pdf = new PdfDocument(writer);
                // use landscape A4 to give more horizontal space
                pdf.SetDefaultPageSize(PageSize.A4.Rotate());
                var document = new Document(pdf);

                // tighter page margins
                document.SetMargins(24, 24, 24, 24);

                // fonts
                var font = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
                var bold = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);

                // Title
                var title = new Paragraph("Payment History Report")
                    .SetFont(bold)
                    .SetFontSize(16)
                    .SetMarginBottom(8);
                document.Add(title);

                // Determine exporter name
                var exporterId = _userManager.GetUserId(User);
                var exporter = exporterId != null ? await _userManager.FindByIdAsync(exporterId) : null;
                var exporterName = exporter != null ? (exporter.UserName ?? exporter.Email ?? exporterId) : (exporterId ?? "Unknown");
                if (!string.IsNullOrEmpty(exporterId))
                {
                    var dprof = await _db.DriverProfiles.FirstOrDefaultAsync(p => p.UserId == exporterId);
                    if (dprof != null && !string.IsNullOrEmpty(dprof.FullName)) exporterName = dprof.FullName;
                    var mprof = await _db.ManagerProfiles.FirstOrDefaultAsync(p => p.UserId == exporterId);
                    if (mprof != null && !string.IsNullOrEmpty(mprof.FullName)) exporterName = mprof.FullName;
                    var fprof = await _db.FinanceProfiles.FirstOrDefaultAsync(p => p.UserId == exporterId);
                    if (fprof != null && !string.IsNullOrEmpty(fprof.FullName)) exporterName = fprof.FullName;
                    var cprof = await _db.CEOProfiles.FirstOrDefaultAsync(p => p.UserId == exporterId);
                    if (cprof != null && !string.IsNullOrEmpty(cprof.FullName)) exporterName = cprof.FullName;
                }

                // Report info (include exporter)
                var reportInfo = new Paragraph()
                    .SetFont(font)
                    .SetFontSize(9)
                    .SetMarginBottom(10)
                    .Add($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n")
                    .Add($"Exported by: {exporterName}\n")
                    .Add($"Total Records: {payments.Count}\n");

                if (!string.IsNullOrEmpty(search)) reportInfo.Add($"Search: {search}\n");
                if (!string.IsNullOrEmpty(status)) reportInfo.Add($"Status Filter: {status}\n");
                if (start.HasValue) reportInfo.Add($"Date From: {start:yyyy-MM-dd}\n");
                if (end.HasValue) reportInfo.Add($"Date To: {end:yyyy-MM-dd}\n");

                document.Add(reportInfo);

                // Table with adjusted column widths to avoid scrambling
                var columnWidths = new float[] { 6f, 26f, 8f, 6f, 10f, 24f, 8f, 8f, 8f, 12f };
                var table = new Table(UnitValue.CreatePercentArray(columnWidths));
                table.SetWidth(UnitValue.CreatePercentValue(100));
                table.SetKeepTogether(true);

                // Table headers
                var headers = new[] { "Report ID", "Driver / Payee", "Amount", "Currency", "Payment Method", "PayMongo ID", "Ref #", "Status", "Paid At", "Processed By" };
                foreach (var header in headers)
                {
                    var cell = new Cell()
                        .Add(new Paragraph(header).SetFont(bold).SetFontSize(9))
                        .SetBackgroundColor(new iText.Kernel.Colors.DeviceRgb(16, 185, 129))
                        .SetFontColor(new iText.Kernel.Colors.DeviceRgb(255, 255, 255))
                        .SetPadding(4);
                    table.AddHeaderCell(cell);
                }

                // Table rows (smaller font to fit)
                foreach (var p in payments)
                {
                    var driverName = (p.Report?.UserId != null && userMap.ContainsKey(p.Report.UserId)) ? userMap[p.Report.UserId] : "—";
                    var processedByName = (p.ProcessedByUserId != null && userMap.ContainsKey(p.ProcessedByUserId)) ? userMap[p.ProcessedByUserId] : "—";
                    var paidAtStr = p.PaidAt.HasValue ? p.PaidAt.Value.ToLocalTime().ToString("MMM d, yyyy") : "—";
                    var paymongo = string.IsNullOrEmpty(p.PayMongoLinkId) ? "" : (p.PayMongoLinkId.Length > 18 ? p.PayMongoLinkId.Substring(0, 12) + "..." + p.PayMongoLinkId.Substring(p.PayMongoLinkId.Length-6) : p.PayMongoLinkId);

                    table.AddCell(new Cell().Add(new Paragraph($"#{p.ReportId}").SetFont(font).SetFontSize(8)).SetPadding(4));
                    table.AddCell(new Cell().Add(new Paragraph(driverName).SetFont(font).SetFontSize(8)).SetPadding(4));
                    table.AddCell(new Cell().Add(new Paragraph($"₱{p.Amount:N2}").SetFont(font).SetFontSize(8)).SetPadding(4));
                    table.AddCell(new Cell().Add(new Paragraph("PHP").SetFont(font).SetFontSize(8)).SetPadding(4));
                    table.AddCell(new Cell().Add(new Paragraph("GCash").SetFont(font).SetFontSize(8)).SetPadding(4));
                    table.AddCell(new Cell().Add(new Paragraph(paymongo).SetFont(font).SetFontSize(7)).SetPadding(4));
                    table.AddCell(new Cell().Add(new Paragraph($"RPT-{p.ReportId}-{p.Id}").SetFont(font).SetFontSize(7)).SetPadding(4));
                    table.AddCell(new Cell().Add(new Paragraph(p.Status).SetFont(font).SetFontSize(8)).SetPadding(4));
                    table.AddCell(new Cell().Add(new Paragraph(paidAtStr).SetFont(font).SetFontSize(8)).SetPadding(4));
                    table.AddCell(new Cell().Add(new Paragraph(processedByName).SetFont(font).SetFontSize(8)).SetPadding(4));
                }

                document.Add(table);
                document.Close();

                var bytes = memoryStream.ToArray();
                return File(bytes, "application/pdf", $"payment-history-{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
            }
        }

        // Refresh a single payment's status from PayMongo API (no webhook needed)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RefreshPaymentStatus(int id)
        {
            var payment = await _db.ReimbursementPayments
                .Include(p => p.Report)
                    .ThenInclude(r => r!.Items)
                .FirstOrDefaultAsync(p => p.Id == id);
            if (payment == null) return NotFound();

            try
            {
                var apiStatus = await _payMongo.GetCheckoutStatusAsync(payment.PayMongoLinkId);
                payment.Status = apiStatus;

                if (apiStatus == "paid" && payment.Report != null && !payment.Report.Reimbursed)
                {
                    payment.PaidAt = DateTime.UtcNow;
                    payment.Report.Reimbursed = true;

                    foreach (var item in payment.Report.Items)
                    {
                        var category = item.Category?.Trim() ?? "";
                        var budget = await _db.Budgets.FirstOrDefaultAsync(b => b.Category == category);
                        if (budget != null)
                            budget.Spent += item.Amount;
                    }

                    _db.Approvals.Add(new Approval
                    {
                        ReportId = payment.ReportId,
                        ApprovedByUserId = _userManager.GetUserId(User),
                        Status = ApprovalStatus.Approved,
                        DecisionDate = DateTime.UtcNow,
                        Stage = "Finance",
                        Remarks = "Reimbursed via PayMongo (synced)"
                    });

                    TempData["Success"] = $"✅ Report #{payment.ReportId} confirmed paid!";

                    // Notify driver and managers about reimbursement
                    if (payment.Report != null)
                        await _notificationService.NotifyReimbursementProcessed(payment.ReportId, payment.Report.UserId, payment.Report.TotalAmount);

                    _db.AuditLogs.Add(new AuditLog
                    {
                        Action = "PaymentSynced",
                        Module = "Reimbursements",
                        Role = "Finance",
                        PerformedByUserId = _userManager.GetUserId(User),
                        RelatedRecordId = payment.ReportId,
                        Details = $"Synced payment for report #{payment.ReportId} — confirmed paid"
                    });
                }
                else
                {
                    TempData["Success"] = $"Report #{payment.ReportId} status: {apiStatus}";
                }

                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Failed to sync: {ex.Message}";
            }

            return RedirectToAction("Payments");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reimburse(int id)
        {
            var report = await _db.ExpenseReports.FindAsync(id);
            if (report == null) return NotFound();

            // Check if already has an active payment session
            var existing = await _db.ReimbursementPayments
                .FirstOrDefaultAsync(p => p.ReportId == id && p.Status != "expired");
            if (existing != null && existing.Status == "paid")
            {
                TempData["Error"] = $"Report #{id} is already paid.";
                return RedirectToAction("Reimbursements");
            }

            try
            {
                // Build success/cancel URLs that point back to this app
                var baseUrl = $"{Request.Scheme}://{Request.Host}";
                var successUrl = $"{baseUrl}/Finance/PaymentSuccess?reportId={id}";
                var cancelUrl = $"{baseUrl}/Finance/Reimbursements";

                // Look up driver to pre-fill customer details in PayMongo checkout
                var driverUser = report.UserId != null
                    ? await _userManager.FindByIdAsync(report.UserId)
                    : null;

                var (sessionId, checkoutUrl) = await _payMongo.CreateCheckoutSessionAsync(
                    report.TotalAmount,
                    $"CEMS Reimbursement - Report #{report.Id}",
                    report.Id,
                    successUrl,
                    cancelUrl,
                    customerEmail: driverUser?.Email,
                    customerName: driverUser?.UserName
                );

                // Save the payment record (or update existing)
                if (existing != null)
                {
                    existing.PayMongoLinkId = sessionId;
                    existing.CheckoutUrl = checkoutUrl;
                    existing.Status = "unpaid";
                    existing.CreatedAt = DateTime.UtcNow;
                }
                else
                {
                    var payment = new ReimbursementPayment
                    {
                        ReportId = report.Id,
                        PayMongoLinkId = sessionId,
                        CheckoutUrl = checkoutUrl,
                        Status = "unpaid",
                        Amount = report.TotalAmount,
                        ProcessedByUserId = _userManager.GetUserId(User)
                    };
                    _db.ReimbursementPayments.Add(payment);
                }

                await _db.SaveChangesAsync();

                _db.AuditLogs.Add(new AuditLog
                {
                    Action = "InitiatePayment",
                    Module = "Reimbursements",
                    Role = "Finance",
                    PerformedByUserId = _userManager.GetUserId(User),
                    RelatedRecordId = report.Id,
                    Details = $"Initiated PayMongo payment for report #{report.Id} (₱{report.TotalAmount:N2})"
                });
                await _db.SaveChangesAsync();

                // Redirect finance user directly to PayMongo checkout (GCash / card)
                return Redirect(checkoutUrl);
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Failed to create payment: {ex.Message}";
                return RedirectToAction("Reimbursements");
            }
        }

        // PayMongo redirects here after successful payment
        [HttpGet]
        public async Task<IActionResult> PaymentSuccess(int reportId)
        {
            var payment = await _db.ReimbursementPayments
                .Include(p => p.Report)
                    .ThenInclude(r => r!.Items)
                .FirstOrDefaultAsync(p => p.ReportId == reportId && p.Status != "expired");

            if (payment == null)
            {
                TempData["Error"] = "Payment record not found.";
                return RedirectToAction("Reimbursements");
            }

            try
            {
                // Verify payment with PayMongo API
                var status = await _payMongo.GetCheckoutStatusAsync(payment.PayMongoLinkId);
                payment.Status = status;

                if (status == "paid" && payment.Report != null)
                {
                    payment.PaidAt = DateTime.UtcNow;
                    payment.Report.Reimbursed = true;

                    foreach (var item in payment.Report.Items)
                    {
                        var category = item.Category?.Trim() ?? "";
                        var budget = await _db.Budgets.FirstOrDefaultAsync(b => b.Category == category);
                        if (budget != null)
                            budget.Spent += item.Amount;
                    }

                    // Record finance approval
                    _db.Approvals.Add(new Approval
                    {
                        ReportId = payment.ReportId,
                        ApprovedByUserId = payment.ProcessedByUserId,
                        Status = ApprovalStatus.Approved,
                        DecisionDate = DateTime.UtcNow,
                        Stage = "Finance",
                        Remarks = "Reimbursed via PayMongo (GCash/Card)"
                    });

                    await _db.SaveChangesAsync();
                    TempData["Success"] = $"✅ Report #{reportId} has been paid and marked as reimbursed!";

                    // Notify driver and managers about reimbursement
                    await _notificationService.NotifyReimbursementProcessed(reportId, payment.Report.UserId, payment.Report.TotalAmount);

                    _db.AuditLogs.Add(new AuditLog
                    {
                        Action = "PaymentConfirmed",
                        Module = "Reimbursements",
                        Role = "Finance",
                        PerformedByUserId = payment.ProcessedByUserId,
                        RelatedRecordId = reportId,
                        Details = $"Payment confirmed for report #{reportId} via PayMongo"
                    });
                    await _db.SaveChangesAsync();
                }
                else
                {
                    await _db.SaveChangesAsync();
                    TempData["Success"] = $"Payment status for Report #{reportId}: {status}. It may take a moment to process.";
                }
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error verifying payment: {ex.Message}";
            }

            return RedirectToAction("Reimbursements");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CheckPaymentStatus(int id)
        {
            var payment = await _db.ReimbursementPayments
                .Include(p => p.Report)
                    .ThenInclude(r => r!.Items)
                .FirstOrDefaultAsync(p => p.Id == id);
            if (payment == null) return NotFound();

            try
            {
                var status = await _payMongo.GetCheckoutStatusAsync(payment.PayMongoLinkId);
                payment.Status = status;

                if (status == "paid" && payment.Report != null && !payment.Report.Reimbursed)
                {
                    payment.PaidAt = DateTime.UtcNow;
                    payment.Report.Reimbursed = true;

                    foreach (var item in payment.Report.Items)
                    {
                        var category = item.Category?.Trim() ?? "";
                        var budget = await _db.Budgets.FirstOrDefaultAsync(b => b.Category == category);
                        if (budget != null)
                            budget.Spent += item.Amount;
                    }

                    _db.Approvals.Add(new Approval
                    {
                        ReportId = payment.ReportId,
                        ApprovedByUserId = _userManager.GetUserId(User),
                        Status = ApprovalStatus.Approved,
                        DecisionDate = DateTime.UtcNow,
                        Stage = "Finance",
                        Remarks = "Reimbursed via PayMongo (verified)"
                    });

                    TempData["Success"] = $"✅ Report #{payment.ReportId} paid and reimbursed!";
                }
                else
                {
                    TempData["Success"] = $"Payment status for Report #{payment.ReportId}: {status}";
                }

                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Failed to check payment status: {ex.Message}";
            }

            return RedirectToAction("Reimbursements");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkReimbursedManual(int id)
        {
            var report = await _db.ExpenseReports
                .Include(r => r.Items)
                .FirstOrDefaultAsync(r => r.Id == id);
            if (report == null) return NotFound();

            report.Reimbursed = true;

            foreach (var item in report.Items)
            {
                var category = item.Category?.Trim() ?? "";
                var budget = await _db.Budgets.FirstOrDefaultAsync(b => b.Category == category);
                if (budget != null)
                    budget.Spent += item.Amount;
            }

            _db.Approvals.Add(new Approval
            {
                ReportId = report.Id,
                ApprovedByUserId = _userManager.GetUserId(User),
                Status = ApprovalStatus.Approved,
                DecisionDate = DateTime.UtcNow,
                Stage = "Finance",
                Remarks = "Manually marked as reimbursed"
            });

            _db.AuditLogs.Add(new AuditLog
            {
                Action = "ManualReimbursement",
                Module = "Reimbursements",
                Role = "Finance",
                PerformedByUserId = _userManager.GetUserId(User),
                RelatedRecordId = report.Id,
                Details = $"Manually marked report #{report.Id} as reimbursed (₱{report.TotalAmount:N2})"
            });

            await _db.SaveChangesAsync();

            // Notify driver and managers about reimbursement
            await _notificationService.NotifyReimbursementProcessed(report.Id, report.UserId, report.TotalAmount);

            TempData["Success"] = "Report marked as reimbursed (manual).";
            return RedirectToAction("Reimbursements");
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
