using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using System.Threading.Tasks;
using System.Linq;
using System;
using Microsoft.AspNetCore.Http;
using System.IO;
using CEMS.Models;
using CEMS.Services;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CEMS.Controllers
{
    [Authorize(Roles = "Driver")]
    [Route("Driver")]
    public class DriverController : Controller
    {
        private readonly Data.ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly NotificationService _notificationService;
        private readonly IS3StorageService _s3Service;

        public DriverController(Data.ApplicationDbContext db, UserManager<IdentityUser> userManager, NotificationService notificationService, IS3StorageService s3Service)
        {
            _db = db;
            _userManager = userManager;
            _notificationService = notificationService;
            _s3Service = s3Service;
        }
        [HttpGet("Dashboard")]
        public async Task<IActionResult> Dashboard()
        {
            var userId = _userManager.GetUserId(User);

            // Get user's full name from DriverProfile
            if (!string.IsNullOrEmpty(userId))
            {
                var driverProfile = await _db.Set<DriverProfile>().FirstOrDefaultAsync(d => d.UserId == userId);
                ViewBag.UserFullName = driverProfile?.FullName ?? User.Identity?.Name ?? "Driver";
            }
            else
            {
                ViewBag.UserFullName = User.Identity?.Name ?? "Driver";
            }

            // Load recent driver expenses
            var expenses = _db.Expenses
                .Where(e => e.UserId == userId)
                .OrderByDescending(e => e.Date)
                .Take(10)
                .ToList();
            ViewBag.RecentExpenses = expenses;

            // Load budgets so driver can see current limits
            var budgets = await _db.Budgets.OrderBy(b => b.Category).ToListAsync();
            ViewBag.Budgets = budgets;
            ViewBag.TotalBudget = budgets.Sum(b => b.Allocated);
            ViewBag.TotalSpent = budgets.Sum(b => b.Spent);
            ViewBag.TotalRemaining = budgets.Sum(b => b.Allocated - b.Spent);

            // Load driver's reports with statuses
            var reports = await _db.ExpenseReports
                .Where(r => r.UserId == userId)
                .OrderByDescending(r => r.SubmissionDate)
                .Take(5)
                .ToListAsync();
            ViewBag.RecentReports = reports;

            return View("Dashboard/Index");
        }

        [HttpGet("Metrics")]
        public async Task<IActionResult> Metrics(DateTime? start, DateTime? end)
        {
            var userId = _userManager.GetUserId(User);
            end ??= DateTime.UtcNow.Date;

            var reportsQuery = _db.ExpenseReports.Where(r => r.UserId == userId);
            if (start.HasValue)
                reportsQuery = reportsQuery.Where(r => r.SubmissionDate >= start.Value);
            if (end.HasValue)
                reportsQuery = reportsQuery.Where(r => r.SubmissionDate <= end.Value.AddDays(1));

            var nowUtc = DateTime.UtcNow;
            var monthStart = new DateTime(nowUtc.Year, nowUtc.Month, 1);

            var pendingCount = await reportsQuery.CountAsync(r => r.Status == ReportStatus.Submitted || r.Status == ReportStatus.PendingCEOApproval);
            var approvedCount = await reportsQuery.CountAsync(r => r.Status == ReportStatus.Approved);
            var rejectedCount = await reportsQuery.CountAsync(r => r.Status == ReportStatus.Rejected);

            // Efficiently compute approved/reimbursed totals without loading related blobs (e.g. ReceiptData)
            var approvedItemsQuery = _db.ExpenseItems
                .Where(i => i.Report != null && i.Report.UserId == userId && i.Report.Status == ReportStatus.Approved);

            if (start.HasValue)
                approvedItemsQuery = approvedItemsQuery.Where(i => i.Report!.SubmissionDate >= start.Value);
            if (end.HasValue)
                approvedItemsQuery = approvedItemsQuery.Where(i => i.Report!.SubmissionDate <= end.Value.AddDays(1));

            var approvedTotal = await approvedItemsQuery.SumAsync(i => (decimal?)i.Amount) ?? 0m;
            var approvedThisMonthTotal = await approvedItemsQuery.Where(i => i.Report!.SubmissionDate >= monthStart).SumAsync(i => (decimal?)i.Amount) ?? 0m;

            var reimbursedItemsQuery = _db.ExpenseItems
                .Where(i => i.Report != null && i.Report.UserId == userId && i.Report.Reimbursed == true);

            if (start.HasValue)
                reimbursedItemsQuery = reimbursedItemsQuery.Where(i => i.Report!.SubmissionDate >= start.Value);
            if (end.HasValue)
                reimbursedItemsQuery = reimbursedItemsQuery.Where(i => i.Report!.SubmissionDate <= end.Value.AddDays(1));

            var reimbursedTotal = await reimbursedItemsQuery.SumAsync(i => (decimal?)i.Amount) ?? 0m;

            // Project only needed fields for recent items to avoid loading large ReceiptData blobs
            var recentItems = await _db.ExpenseItems
                .Where(i => i.Report != null && i.Report.UserId == userId)
                .Where(i => !start.HasValue || i.Report!.SubmissionDate >= start.Value)
                .Where(i => !end.HasValue || i.Report!.SubmissionDate <= end.Value.AddDays(1))
                .OrderByDescending(i => i.Report!.SubmissionDate)
                .Take(10)
                .Select(i => new
                {
                    i.Id,
                    Date = i.Date,
                    i.Category,
                    i.Amount,
                    i.Description,
                    ReportStatus = i.Report!.Status,
                    BudgetCheck = i.Report!.BudgetCheck,
                    ReceiptExists = i.ReceiptData != null,
                    i.ReceiptPath
                })
                .ToListAsync();

            var recent = recentItems.Select(i => new
            {
                id = i.Id,
                date = i.Date.ToString("o"),
                category = i.Category,
                amount = i.Amount,
                description = i.Description,
                reportStatus = i.ReportStatus.ToString(),
                budgetCheck = i.BudgetCheck.ToString(),
                receiptUrl = (i.ReceiptExists || !string.IsNullOrEmpty(i.ReceiptPath)) ? Url.Action("View", "Receipt", new { id = i.Id }) : null
            }).ToList();

            // Calculate spent dynamically from reimbursed expense items only (Finance paid)
            var spentByCategory = await _db.ExpenseItems
                .Where(ei => ei.Report != null && ei.Report.UserId == userId && ei.Report.Reimbursed == true
                             && ei.Report.SubmissionDate >= start && ei.Report.SubmissionDate <= end.Value.AddDays(1))
                .GroupBy(ei => ei.Category)
                .Select(g => new { Category = g.Key, Total = g.Sum(ei => ei.Amount) })
                .ToListAsync();

            // Get all budgets and merge with spent data
            var allBudgets = await _db.Budgets.ToListAsync();
            var budgets = allBudgets.Select(b => new
            {
                b.Category,
                b.Allocated,
                spent = spentByCategory.FirstOrDefault(s => s.Category == b.Category)?.Total ?? 0m
            }).ToList();

            return Json(new
            {
                pendingCount,
                approvedCount,
                rejectedCount,
                approvedTotal,
                approvedThisMonthTotal,
                reimbursedTotal,
                recent,
                budgets
            });
        }

        [HttpGet("MyReports")]
        public async Task<IActionResult> MyReports(int page = 1, int pageSize = 5, string? status = null)
        {
            var userId = _userManager.GetUserId(User);

            // Build base query and apply optional status filter
            var baseQuery = _db.ExpenseReports.Where(r => r.UserId == userId);
            if (!string.IsNullOrEmpty(status) && Enum.TryParse<ReportStatus>(status, out var parsedStatus))
            {
                baseQuery = baseQuery.Where(r => r.Status == parsedStatus);
            }

        
            var totalCount = await baseQuery.CountAsync();

            var reports = await baseQuery
                .Include(r => r.Items)
                .OrderByDescending(r => r.SubmissionDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.Reports = reports;
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalCount = totalCount;
            ViewBag.TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            ViewBag.FilterStatus = status;

            // Load budgets for context
            var budgets = await _db.Budgets.ToListAsync();
            ViewBag.Budgets = budgets;

            // Load active categories for the edit modal dropdown
            ViewBag.Categories = budgets.Where(b => b.IsActive).OrderBy(b => b.Category).Select(b => b.Category).Distinct().ToList();

        
            var reportIds = reports.Select(r => r.Id).ToList();
            var approvals = await _db.Approvals
                .Where(a => reportIds.Contains(a.ReportId))
                .OrderBy(a => a.DecisionDate)
                .ToListAsync();
            ViewBag.Approvals = approvals;

            return View("MyReports/Index");
        }

        [HttpGet("EditReport/{id:int}")]
        public IActionResult EditReport(int id)
        {
            // Keep old route for compatibility: redirect to MyReports (editing is done via modal)
            return RedirectToAction("MyReports");
        }

        [HttpGet("GetReport/{id:int}")]
        public async Task<IActionResult> GetReport(int id)
        {
            var userId = _userManager.GetUserId(User);
            var report = await _db.ExpenseReports
                .Include(r => r.Items)
                .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);

            if (report == null)
                return NotFound();

            // Only allow editing when report is not yet manager-approved
            if (report.Status == ReportStatus.Approved || report.Status == ReportStatus.PendingCEOApproval)
                return Forbid();

            var dto = new
            {
                report.Id,
                Status = report.Status.ToString(),
                report.TotalAmount,
                report.BudgetCheck,
                TripStart = report.TripStart?.ToString("yyyy-MM-dd"),
                TripEnd = report.TripEnd?.ToString("yyyy-MM-dd"),
                report.TripDays,
                Items = report.Items.Select(i => new {
                    i.Id,
                    Date = i.Date.ToString("yyyy-MM-dd"),
                    i.Category,
                    Amount = i.Amount,
                    Description = i.Description,
                    HasReceipt = (i.ReceiptData != null && i.ReceiptData.Length > 0) || !string.IsNullOrEmpty(i.ReceiptPath),
                    ReceiptUrl = (i.ReceiptData != null && i.ReceiptData.Length > 0) || !string.IsNullOrEmpty(i.ReceiptPath)
                        ? Url.Action("View", "Receipt", new { id = i.Id })
                        : null
                }).ToList()
            };

            return Json(dto);
        }

        [HttpPost("EditReport/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditReport(int id, IFormFileCollection files)
        {
            var userId = _userManager.GetUserId(User);

            var report = await _db.ExpenseReports
                .Include(r => r.Items)
                .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);

            if (report == null)
                return NotFound();

            if (report.Status == ReportStatus.Approved)
            {
                TempData["Error"] = "Approved reports cannot be edited.";
                return RedirectToAction("MyReports");
            }

            var form = Request.Form;
            var updatedItems = new List<ExpenseItem>();

            var index = 0;
            while (true)
            {
                var prefix = $"items[{index}]";
                if (!form.ContainsKey(prefix + ".Id"))
                    break;

                if (!int.TryParse(form[prefix + ".Id"], out var itemId))
                {
                    index++;
                    continue;
                }

                var item = report.Items.FirstOrDefault(i => i.Id == itemId);
                if (item == null)
                {
                    index++;
                    continue;
                }

                if (DateTime.TryParse(form[prefix + ".Date"], out var dt))
                    item.Date = dt;

                item.Category = (form[prefix + ".Category"].ToString() ?? "").Trim();
                if (decimal.TryParse(form[prefix + ".Amount"], out var amt))
                    item.Amount = amt;
                item.Description = form[prefix + ".Description"].ToString()?.Trim();

                var file = Request.Form.Files.FirstOrDefault(f => f.Name == prefix + ".Receipt");
                if (file != null && file.Length > 0)
                {
                    var receiptError = ValidateReceiptFile(file);
                    if (receiptError != null)
                    {
                        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                            return Json(new { success = false, message = $"Row {index + 1} receipt: {receiptError}" });
                        TempData["Error"] = receiptError;
                        return RedirectToAction("MyReports");
                    }

                    if (_s3Service.IsEnabled)
                    {
                        using var stream = file.OpenReadStream();
                        var s3Key = await _s3Service.UploadFileAsync(stream, file.FileName, file.ContentType);
                        item.ReceiptPath = s3Key;
                        item.ReceiptContentType = file.ContentType;
                        item.ReceiptData = null;
                    }
                    else
                    {
                        using var ms = new MemoryStream();
                        await file.CopyToAsync(ms);
                        item.ReceiptData = ms.ToArray();
                        item.ReceiptContentType = file.ContentType;
                        item.ReceiptPath = null;
                    }
                }

                updatedItems.Add(item);
                index++;
            }

            if (!updatedItems.Any())
            {
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    return Json(new { success = false, message = "No expense items were updated." });

                TempData["Error"] = "No expense items were updated.";
                return RedirectToAction("MyReports");
            }

            report.TotalAmount = updatedItems.Sum(i => i.Amount);
            report.Status = ReportStatus.Submitted;
            report.SubmissionDate = DateTime.UtcNow;
            report.ForwardedToCEO = false;
            report.CEOApproved = false;
            report.Reimbursed = false;

        
            DateTime? tripStart = null;
            DateTime? tripEnd = null;
            if (DateTime.TryParse(form["TripStart"], out var ts))
                tripStart = ts;
            if (DateTime.TryParse(form["TripEnd"], out var te))
                tripEnd = te;

            if (!tripStart.HasValue || !tripEnd.HasValue)
            {
                tripStart = updatedItems.Min(i => i.Date);
                tripEnd = updatedItems.Max(i => i.Date);
            }
            if (tripEnd < tripStart)
                tripEnd = tripStart;

            var tripDays = Math.Max(1, (int)(tripEnd!.Value.Date - tripStart!.Value.Date).TotalDays + 1);
            report.TripStart = tripStart;
            report.TripEnd = tripEnd;
            report.TripDays = tripDays;

            // Duration-aware budget check (per-diem pro-rata)
            var reportCategories = updatedItems.Select(i => i.Category).Distinct().ToList();
            var overBudget = false;
            var daysInMonth = DateTime.DaysInMonth(tripStart.Value.Year, tripStart.Value.Month);
            var monthStart = new DateTime(tripStart.Value.Year, tripStart.Value.Month, 1);

            foreach (var category in reportCategories)
            {
                var budget = await _db.Budgets.FirstOrDefaultAsync(b => b.Category == category);
                if (budget == null)
                {
                    overBudget = true;
                    break;
                }

                var reportCategoryTotal = updatedItems.Where(i => i.Category == category).Sum(i => i.Amount);

                // calculation sa (monthly budget / days in month) * trip days
                var allowedForTrip = budget.Allocated / daysInMonth * tripDays;
                if (reportCategoryTotal > allowedForTrip)
                {
                    overBudget = true;
                    break;
                }

                // Also enforce monthly ceiling
                var currentMonthSpent = await _db.ExpenseItems
                    .Where(i => i.Report != null && i.Category == category && i.Report.SubmissionDate >= monthStart && i.ReportId != report.Id)
                    .SumAsync(i => (decimal?)i.Amount) ?? 0m;

                if (currentMonthSpent + reportCategoryTotal > budget.Allocated)
                {
                    overBudget = true;
                    break;
                }
            }

            report.BudgetCheck = overBudget ? BudgetCheckStatus.OverBudget : BudgetCheckStatus.WithinBudget;

            var approvals = await _db.Approvals.Where(a => a.ReportId == report.Id).ToListAsync();
            if (approvals.Any())
                _db.Approvals.RemoveRange(approvals);

            await _db.SaveChangesAsync();

            // Audit log
            _db.AuditLogs.Add(new CEMS.Models.AuditLog
            {
                Action = "EditExpenseReport",
                Module = "Expense Reports",
                Role = "Driver",
                PerformedByUserId = userId,
                RelatedRecordId = report.Id,
                Details = $"Driver edited and resubmitted expense report #{report.Id} — ₱{report.TotalAmount:N2}, {report.BudgetCheck}"
            });
            await _db.SaveChangesAsync();

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return Json(new { success = true, message = "Report updated and resubmitted for review." });
            }

            TempData["Success"] = "Report updated and resubmitted for review.";
            return RedirectToAction("MyReports");
        }

        [HttpGet("Expenses")]
        public async Task<IActionResult> Expenses()
        {
            // Load active categories from CEO-configured budgets so drivers can only select allowed categories
            var categories = await _db.Budgets
                .Where(b => b.IsActive)
                .OrderBy(b => b.Category)
                .Select(b => b.Category)
                .Distinct()
                .ToListAsync();

            var vm = new DriverExpenseFormViewModel
            {
                Categories = categories
            };

            return View("Expenses/Index", vm);
        }

        [HttpGet("Submit")]
        public IActionResult Submit()
        {
            return View("Submit/Index");
        }

        [HttpPost("Submit")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Submit(Models.Expense model, IFormFile receipt)
        {
            try
            {
    
                var user = await _userManager.GetUserAsync(User);
                if (user == null)
                    throw new InvalidOperationException("Unable to determine current user.");

                model.UserId = user.Id;
                model.Date = model.Date == default ? DateTime.UtcNow : model.Date;
                model.Status = Models.ExpenseStatus.Pending;
                // Trim category and description
                model.Category = model.Category?.Trim() ?? "";
                model.Description = model.Description?.Trim();

                if (receipt != null && receipt.Length > 0)
                {
                    var receiptError = ValidateReceiptFile(receipt);
                    if (receiptError != null)
                    {
                        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                            return Json(new { success = false, message = receiptError });
                        ModelState.AddModelError("receipt", receiptError);
                        return View("Submit/Index", model);
                    }

                    if (_s3Service.IsEnabled)
                    {
                        using var stream = receipt.OpenReadStream();
                        var s3Key = await _s3Service.UploadFileAsync(stream, receipt.FileName, receipt.ContentType);
                        model.ReceiptPath = s3Key;
                        model.ReceiptContentType = receipt.ContentType;
                        model.ReceiptData = null;
                    }
                    else
                    {
                        using (var ms = new MemoryStream())
                        {
                            await receipt.CopyToAsync(ms);
                            model.ReceiptData = ms.ToArray();
                        }
                        model.ReceiptContentType = receipt.ContentType;
                        model.ReceiptPath = null;
                    }
                }

                
                ModelState.Clear();
                if (!TryValidateModel(model))
                {
                    var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToArray();
                    if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    {
                        return Json(new { success = false, errors });
                    }

                    // Return to form with model errors
                    return View("Submit/Index", model);
                }

                // Create an ExpenseReport with the single expense item
                var expenseItem = new ExpenseItem
                {
                    Date = model.Date,
                    Category = model.Category,
                    Amount = model.Amount,
                    Description = model.Description,
                    ReceiptData = model.ReceiptData,
                    ReceiptContentType = model.ReceiptContentType
                };

                var report = new ExpenseReport
                {
                    UserId = user.Id,
                    SubmissionDate = DateTime.UtcNow,
                    Status = ReportStatus.Submitted,
                    TotalAmount = model.Amount,
                    TripStart = model.Date,
                    TripEnd = model.Date,
                    TripDays = 1,
                    Items = new List<ExpenseItem> { expenseItem }
                };

                // Check budget
                var budget = await _db.Budgets.FirstOrDefaultAsync(b => b.Category == model.Category);
                if (budget == null)
                {
                    report.BudgetCheck = BudgetCheckStatus.OverBudget;
                }
                else
                {
                    var daysInMonth = DateTime.DaysInMonth(model.Date.Year, model.Date.Month);
                    var monthStart = new DateTime(model.Date.Year, model.Date.Month, 1);
                    var allowedForDay = budget.Allocated / daysInMonth;

                    var currentMonthSpent = await _db.ExpenseItems
                        .Where(i => i.Report != null && i.Category == model.Category && i.Report.SubmissionDate >= monthStart)
                        .SumAsync(i => (decimal?)i.Amount) ?? 0m;

                    if (model.Amount > allowedForDay || currentMonthSpent + model.Amount > budget.Allocated)
                    {
                        report.BudgetCheck = BudgetCheckStatus.OverBudget;
                    }
                    else
                    {
                        report.BudgetCheck = BudgetCheckStatus.WithinBudget;
                    }
                }

                _db.ExpenseReports.Add(report);
                await _db.SaveChangesAsync();

                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = true, message = "Expense submitted and pending review." });
                }

                TempData["Success"] = "Expense submitted and pending review.";
                return RedirectToAction("Dashboard");
            }
            catch (Exception ex)
            {
                // Log to console for debugging; in production use ILogger
                Console.WriteLine($"Error submitting expense: {ex.Message}");

                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = false, message = "Failed to submit expense. " + ex.Message });
                }

                TempData["Error"] = "Failed to submit expense. " + ex.Message;
                return View("Submit/Index", model);
            }
        }

        [HttpPost("SubmitMultiple")]
        [Authorize(Roles = "Driver")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitMultiple()
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                if (user == null)
                    return Json(new { success = false, message = "User not found" });

                var items = new List<ExpenseItem>();
                // Bind dynamic rows from form-data: items[i].Field
                var files = Request.Form.Files;
                var form = Request.Form;

                int index = 0;
                while (true)
                {
                    var prefix = $"items[{index}]";
                    if (!form.ContainsKey(prefix + ".Category") && !form.ContainsKey(prefix + ".Amount"))
                        break;

                    var expense = new ExpenseItem();
                    // Date optional
                    if (DateTime.TryParse(form[prefix + ".Date"], out var dt))
                        expense.Date = dt;
                    else
                        expense.Date = DateTime.UtcNow;
                    expense.Category = (form[prefix + ".Category"].ToString() ?? "").Trim();
                    if (decimal.TryParse(form[prefix + ".Amount"], out var amt))
                        expense.Amount = amt;
                    expense.Description = form[prefix + ".Description"].ToString()?.Trim();

                    var file = files.FirstOrDefault(f => f.Name == prefix + ".Receipt");
                    if (file != null && file.Length > 0)
                    {
                        var receiptError = ValidateReceiptFile(file);
                        if (receiptError != null)
                            return Json(new { success = false, message = $"Row {index + 1} receipt: {receiptError}" });

                        if (_s3Service.IsEnabled)
                        {
                            using var stream = file.OpenReadStream();
                            var s3Key = await _s3Service.UploadFileAsync(stream, file.FileName, file.ContentType);
                            expense.ReceiptPath = s3Key;
                            expense.ReceiptContentType = file.ContentType;
                            expense.ReceiptData = null;
                        }
                        else
                        {
                            using var ms = new MemoryStream();
                            await file.CopyToAsync(ms);
                            expense.ReceiptData = ms.ToArray();
                            expense.ReceiptContentType = file.ContentType;
                        }
                    }

                    items.Add(expense);
                    index++;
                }

                if (items.Count == 0)
                {
                    return Json(new { success = false, message = "No expenses to submit." });
                }

                if (items.Count > 20)
                {
                    return Json(new { success = false, message = "Maximum 20 expense items per submission." });
                }

                // Resolve trip dates from form or infer from item dates
                DateTime? tripStart = null;
                DateTime? tripEnd = null;
                if (DateTime.TryParse(form["TripStart"], out var tsf))
                    tripStart = tsf;
                if (DateTime.TryParse(form["TripEnd"], out var tef))
                    tripEnd = tef;

                if (!tripStart.HasValue || !tripEnd.HasValue)
                {
                    tripStart = items.Min(i => i.Date);
                    tripEnd = items.Max(i => i.Date);
                }
                if (tripEnd < tripStart)
                    tripEnd = tripStart;

                var tripDays = Math.Max(1, (int)(tripEnd!.Value.Date - tripStart!.Value.Date).TotalDays + 1);

                var report = new ExpenseReport
                {
                    UserId = user.Id,
                    SubmissionDate = DateTime.UtcNow,
                    Status = ReportStatus.Submitted,
                    TotalAmount = items.Sum(i => i.Amount),
                    TripStart = tripStart,
                    TripEnd = tripEnd,
                    TripDays = tripDays
                };

                // Duration-aware budget check (per-diem pro-rata)
                var reportCategories = items.Select(i => i.Category).Distinct().ToList();
                var overBudget = false;
                var daysInMonth = DateTime.DaysInMonth(tripStart.Value.Year, tripStart.Value.Month);
                var monthStart = new DateTime(tripStart.Value.Year, tripStart.Value.Month, 1);

                foreach (var category in reportCategories)
                {
                    var budget = await _db.Budgets.FirstOrDefaultAsync(b => b.Category == category);
                    if (budget == null)
                    {
                        overBudget = true;
                        break;
                    }

                    var reportCategoryTotal = items.Where(i => i.Category == category).Sum(i => i.Amount);

                    // Per-trip allowance: (monthly budget / days in month) * trip days
                    var allowedForTrip = budget.Allocated / daysInMonth * tripDays;
                    if (reportCategoryTotal > allowedForTrip)
                    {
                        overBudget = true;
                        break;
                    }

                    // Also enforce monthly ceiling
                    var currentMonthSpent = await _db.ExpenseItems
                        .Where(i => i.Report != null && i.Category == category && i.Report.SubmissionDate >= monthStart)
                        .SumAsync(i => (decimal?)i.Amount) ?? 0m;

                    if (currentMonthSpent + reportCategoryTotal > budget.Allocated)
                    {
                        overBudget = true;
                        break;
                    }
                }

                report.BudgetCheck = overBudget ? BudgetCheckStatus.OverBudget : BudgetCheckStatus.WithinBudget;

                foreach (var item in items)
                {
                    item.Report = report;
                }

                _db.ExpenseReports.Add(report);
                await _db.ExpenseItems.AddRangeAsync(items);
                await _db.SaveChangesAsync();

                // Audit log
                _db.AuditLogs.Add(new CEMS.Models.AuditLog
                {
                    Action = "SubmitExpenseReport",
                    Module = "Expense Reports",
                    Role = "Driver",
                    PerformedByUserId = user.Id,
                    RelatedRecordId = report.Id,
                    Details = $"Driver submitted expense report #{report.Id} with {items.Count} item(s) totalling ₱{report.TotalAmount:N2} — {report.BudgetCheck}"
                });
                await _db.SaveChangesAsync();

                // Notify managers about the new submission
                await _notificationService.NotifyReportSubmitted(report.Id, report.TotalAmount, user.UserName ?? user.Email ?? "Driver");

                // If over budget, also send over-budget alerts
                if (overBudget)
                {
                    await _notificationService.NotifyReportOverBudget(report.Id, report.TotalAmount, user.UserName ?? user.Email ?? "Driver");
                }

                return Json(new { success = true, message = $"Submitted {items.Count} expense(s) in report #{report.Id}." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }



        // ── Receipt upload validation ──────────────────────────────────────────
        private static readonly string[] _allowedReceiptExtensions =
            { ".jpg", ".jpeg", ".png", ".webp", ".pdf", ".doc", ".docx" };

        // Allowed MIME types mapped to extensions (guards against extension spoofing)
        private static readonly string[] _allowedReceiptMimeTypes =
        {
            "image/jpeg", "image/png", "image/webp",
            "application/pdf",
            "application/msword",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
        };

        private const long MaxReceiptBytes = 5 * 1024 * 1024; // 5 MB

        /// <summary>Returns an error message if the file is invalid, or null if it passes.</summary>
        private static string? ValidateReceiptFile(IFormFile file)
        {
            if (file.Length > MaxReceiptBytes)
                return $"Receipt file is too large (max 5 MB). Your file is {file.Length / 1024 / 1024.0:F1} MB.";

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!_allowedReceiptExtensions.Contains(ext))
                return $"File type '{ext}' is not allowed. Use JPG, PNG, WebP, PDF, DOC, or DOCX.";

            if (!_allowedReceiptMimeTypes.Contains(file.ContentType.ToLowerInvariant()))
                return $"File content type '{file.ContentType}' is not allowed.";

            return null; // valid
        }

        // ── End receipt validation ─────────────────────────────────────────────



        [HttpGet("History")]
        public async Task<IActionResult> History(DateTime? start, DateTime? end, string status, int page = 1, int pageSize = 10)
        {
            var userId = _userManager.GetUserId(User);
            var q = _db.ExpenseItems
                .Include(i => i.Report)
                .Where(i => i.Report != null && i.Report.UserId == userId);

            if (start.HasValue)
                q = q.Where(i => i.Report!.SubmissionDate >= start.Value);
            if (end.HasValue)
                q = q.Where(i => i.Report!.SubmissionDate <= end.Value.AddDays(1));
            if (!string.IsNullOrEmpty(status) && Enum.TryParse<ReportStatus>(status, out var s))
                q = q.Where(i => i.Report != null && i.Report.Status == s);

            var totalCount = await q.CountAsync();

            var itemsRaw = await q
                .OrderByDescending(i => i.Report!.SubmissionDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var items = itemsRaw.Select(i => new ExpenseItemSummaryDto
            {
                Item = i,
                ReportStatus = i.Report != null ? i.Report.Status : ReportStatus.Submitted
            }).ToList();

            ViewBag.Items = items;
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalCount = totalCount;
            ViewBag.TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

            return View("History/Index");
        }

        public IActionResult Index()
        {
            return RedirectToAction("Dashboard");
        }
    }
}