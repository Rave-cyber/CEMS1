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
    [Authorize(Roles = "Finance")]
    public class FinanceController : Controller
    {
        private readonly Data.ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IPayMongoService _payMongo;

        public FinanceController(Data.ApplicationDbContext db, UserManager<IdentityUser> userManager, IPayMongoService payMongo)
        {
            _db = db;
            _userManager = userManager;
            _payMongo = payMongo;
        }

        // Dashboard: list reports ready for reimbursement with optional date filter
        // Defaults to current month when no dates are provided
        public async Task<IActionResult> Dashboard(DateTime? start, DateTime? end)
        {
            var now = DateTime.UtcNow.Date;
            var startDate = start?.Date ?? new DateTime(now.Year, now.Month, 1);
            var endDate = end?.Date ?? now;
            var endExclusive = endDate.AddDays(1);

            // Only show reports approved by manager and cleared for finance in the date range
            var reportsQuery = _db.ExpenseReports
                .Include(r => r.Items)
                .Where(r => r.Status == ReportStatus.Approved && !r.Reimbursed)
                .Where(r => (r.BudgetCheck == BudgetCheckStatus.WithinBudget) || (r.CEOApproved == true))
                .Where(r => r.SubmissionDate >= startDate && r.SubmissionDate < endExclusive)
                .OrderByDescending(r => r.SubmissionDate);

            var reports = await reportsQuery.ToListAsync();

            var userIds = reports.Select(r => r.UserId).Where(id => id != null).Distinct().ToList();
            var users = await _userManager.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName);

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

            // monthly trend for last 6 months (based on decision date)
            var months = Enumerable.Range(0, 6).Select(i => DateTime.UtcNow.AddMonths(-i)).Select(d => new { d.Year, d.Month }).Reverse().ToList();
            var monthlyData = new List<object>();
            foreach (var m in months)
            {
                var mStart = new DateTime(m.Year, m.Month, 1);
                var mEnd = mStart.AddMonths(1);
                var total = await _db.Approvals
                    .Include(a => a.Report)
                    .Where(a => a.Stage == "Finance" && a.Status == ApprovalStatus.Approved && a.DecisionDate.HasValue && a.DecisionDate.Value >= mStart && a.DecisionDate.Value < mEnd)
                    .SumAsync(a => (decimal?)(a.Report != null ? a.Report.TotalAmount : 0m)) ?? 0m;
                monthlyData.Add(new { label = mStart.ToString("MMM yyyy"), total });
            }

            // category breakdown for reimbursed reports
            var categoryData = await _db.ExpenseItems
                .Include(i => i.Report)
                .Where(i => i.Report != null && i.Report.Reimbursed)
                .GroupBy(i => i.Category)
                .Select(g => new { Category = g.Key, Total = g.Sum(i => (decimal?)i.Amount) ?? 0m })
                .OrderByDescending(x => x.Total)
                .Take(8)
                .ToListAsync();

            return Json(new
            {
                processedToday = processedTotal,
                processedCount = processedCount,
                monthlyTotal = processedTotal,
                monthlyCount = processedCount,
                monthlyData,
                categories = categoryData
            });
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

        // Payment history: fetched from ReimbursementPayments table (no webhook needed)
        public async Task<IActionResult> Payments(string? search, string? status, DateTime? start, DateTime? end, int page = 1, int pageSize = 15)
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
