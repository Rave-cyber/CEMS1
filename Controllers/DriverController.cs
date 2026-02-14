using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using System.Threading.Tasks;
using System.Linq;
using System;
using Microsoft.AspNetCore.Http;
using System.IO;
using CEMS.Models;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace CEMS.Controllers
{
    [Authorize(Roles = "Driver")]
    [Route("Driver")]
    public class DriverController : Controller
    {
        private readonly Data.ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;

        public DriverController(Data.ApplicationDbContext db, UserManager<IdentityUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }
        [HttpGet("Dashboard")]
        public async Task<IActionResult> Dashboard()
        {
            var userId = _userManager.GetUserId(User);

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

            var reportsQuery = _db.ExpenseReports.Where(r => r.UserId == userId);
            if (start.HasValue)
                reportsQuery = reportsQuery.Where(r => r.SubmissionDate >= start.Value);
            if (end.HasValue)
                reportsQuery = reportsQuery.Where(r => r.SubmissionDate <= end.Value);

            var nowUtc = DateTime.UtcNow;
            var monthStart = new DateTime(nowUtc.Year, nowUtc.Month, 1);

            var pendingCount = await reportsQuery.CountAsync(r => r.Status == ReportStatus.Submitted || r.Status == ReportStatus.PendingCEOApproval);
            var approvedCount = await reportsQuery.CountAsync(r => r.Status == ReportStatus.Approved);
            var rejectedCount = await reportsQuery.CountAsync(r => r.Status == ReportStatus.Rejected);

            var approvedItemsQuery = _db.ExpenseItems
                .Include(i => i.Report)
                .Where(i => i.Report != null && i.Report.UserId == userId && i.Report.Status == ReportStatus.Approved);

            if (start.HasValue)
                approvedItemsQuery = approvedItemsQuery.Where(i => i.Date >= start.Value);
            if (end.HasValue)
                approvedItemsQuery = approvedItemsQuery.Where(i => i.Date <= end.Value);

            var approvedTotal = await approvedItemsQuery.SumAsync(i => (decimal?)i.Amount) ?? 0m;

            var approvedThisMonthQuery = approvedItemsQuery.Where(i => i.Date >= monthStart);
            var approvedThisMonthTotal = await approvedThisMonthQuery.SumAsync(i => (decimal?)i.Amount) ?? 0m;

            var recentItemsQuery = _db.ExpenseItems
                .Include(i => i.Report)
                .Where(i => i.Report != null && i.Report.UserId == userId);

            if (start.HasValue)
                recentItemsQuery = recentItemsQuery.Where(i => i.Date >= start.Value);
            if (end.HasValue)
                recentItemsQuery = recentItemsQuery.Where(i => i.Date <= end.Value);

            var recentItems = await recentItemsQuery
                .OrderByDescending(i => i.Date)
                .Take(10)
                .ToListAsync();

            var recent = recentItems.Select(i => new
            {
                id = i.Id,
                date = i.Date.ToString("o"),
                category = i.Category,
                amount = i.Amount,
                description = i.Description,
                reportStatus = i.Report != null ? i.Report.Status.ToString() : ReportStatus.Submitted.ToString(),
                budgetCheck = i.Report != null ? i.Report.BudgetCheck.ToString() : "WithinBudget",
                receiptUrl = (i.ReceiptData != null && i.ReceiptData.Length > 0) ? Url.Action("Receipt", "Driver", new { id = i.Id }) : (string.IsNullOrEmpty(i.ReceiptPath) ? null : i.ReceiptPath)
            }).ToList();

            // Budget data so driver sees limits
            var budgets = await _db.Budgets.Select(b => new { b.Category, b.Allocated, b.Spent }).ToListAsync();

            return Json(new
            {
                pendingCount,
                approvedCount,
                rejectedCount,
                approvedTotal,
                approvedThisMonthTotal,
                recent,
                budgets
            });
        }

        [HttpGet("MyReports")]
        public async Task<IActionResult> MyReports()
        {
            var userId = _userManager.GetUserId(User);

            var reports = await _db.ExpenseReports
                .Include(r => r.Items)
                .Where(r => r.UserId == userId)
                .OrderByDescending(r => r.SubmissionDate)
                .ToListAsync();

            ViewBag.Reports = reports;

            // Load budgets for context
            var budgets = await _db.Budgets.ToListAsync();
            ViewBag.Budgets = budgets;

            // Load approval remarks for rejected/approved reports
            var reportIds = reports.Select(r => r.Id).ToList();
            var approvals = await _db.Approvals
                .Where(a => reportIds.Contains(a.ReportId))
                .OrderBy(a => a.DecisionDate)
                .ToListAsync();
            ViewBag.Approvals = approvals;

            return View("MyReports/Index");
        }

        [HttpGet("EditReport/{id:int}")]
        public async Task<IActionResult> EditReport(int id)
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

            return View("EditReport/Index", report);
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

                item.Category = form[prefix + ".Category"];
                if (decimal.TryParse(form[prefix + ".Amount"], out var amt))
                    item.Amount = amt;
                item.Description = form[prefix + ".Description"];

                var file = Request.Form.Files.FirstOrDefault(f => f.Name == prefix + ".Receipt");
                if (file != null && file.Length > 0)
                {
                    using var ms = new MemoryStream();
                    await file.CopyToAsync(ms);
                    item.ReceiptData = ms.ToArray();
                    item.ReceiptContentType = file.ContentType;
                    item.ReceiptPath = null;
                }

                updatedItems.Add(item);
                index++;
            }

            if (!updatedItems.Any())
            {
                TempData["Error"] = "No expense items were updated.";
                return View("EditReport/Index", report);
            }

            report.TotalAmount = updatedItems.Sum(i => i.Amount);
            report.Status = ReportStatus.Submitted;
            report.SubmissionDate = DateTime.UtcNow;
            report.ForwardedToCEO = false;
            report.CEOApproved = false;
            report.Reimbursed = false;

            var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
            var reportCategories = updatedItems.Select(i => i.Category).Distinct().ToList();
            var overBudget = false;

            foreach (var category in reportCategories)
            {
                var budget = await _db.Budgets.FirstOrDefaultAsync(b => b.Category == category);
                if (budget == null)
                    continue;

                var currentMonthSpent = await _db.ExpenseItems
                    .Where(i => i.Category == category && i.Date >= monthStart && i.ReportId != report.Id)
                    .SumAsync(i => (decimal?)i.Amount) ?? 0m;

                var reportCategoryTotal = updatedItems.Where(i => i.Category == category).Sum(i => i.Amount);

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

            TempData["Success"] = "Report updated and resubmitted for review.";
            return RedirectToAction("MyReports");
        }

        [HttpGet("Expenses")]
        public IActionResult Expenses()
        {
            return View("Expenses/Index");
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
                // Ensure we set server-side fields (UserId, Date, Status, ReceiptPath)
                var user = await _userManager.GetUserAsync(User);
                if (user == null)
                    throw new InvalidOperationException("Unable to determine current user.");

                model.UserId = user.Id;
                model.Date = model.Date == default ? DateTime.UtcNow : model.Date;
                model.Status = Models.ExpenseStatus.Pending;

                if (receipt != null && receipt.Length > 0)
                {
                    using (var ms = new MemoryStream())
                    {
                        await receipt.CopyToAsync(ms);
                        model.ReceiptData = ms.ToArray();
                    }
                    model.ReceiptContentType = receipt.ContentType;
                    // Keep ReceiptPath null when storing blob in DB
                    model.ReceiptPath = null;
                }

                // Re-validate model after we populated server-only fields so required fields don't fail
                // Clear existing modelstate and re-run validation for this model
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

                _db.Expenses.Add(model);
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
                    expense.Category = form[prefix + ".Category"];
                    if (decimal.TryParse(form[prefix + ".Amount"], out var amt))
                        expense.Amount = amt;
                    expense.Description = form[prefix + ".Description"];

                    var file = files.FirstOrDefault(f => f.Name == prefix + ".Receipt");
                    if (file != null && file.Length > 0)
                    {
                        using var ms = new MemoryStream();
                        await file.CopyToAsync(ms);
                        expense.ReceiptData = ms.ToArray();
                        expense.ReceiptContentType = file.ContentType;
                    }

                    items.Add(expense);
                    index++;
                }

                if (items.Count == 0)
                {
                    return Json(new { success = false, message = "No expenses to submit." });
                }

                var report = new ExpenseReport
                {
                    UserId = user.Id,
                    SubmissionDate = DateTime.UtcNow,
                    Status = ReportStatus.Submitted,
                    TotalAmount = items.Sum(i => i.Amount)
                };

                var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
                var reportCategories = items.Select(i => i.Category).Distinct().ToList();
                var overBudget = false;

                foreach (var category in reportCategories)
                {
                    var budget = await _db.Budgets.FirstOrDefaultAsync(b => b.Category == category);
                    if (budget == null)
                        continue;

                    var currentMonthSpent = await _db.ExpenseItems
                        .Where(i => i.Category == category && i.Date >= monthStart)
                        .SumAsync(i => (decimal?)i.Amount) ?? 0m;

                    var reportCategoryTotal = items.Where(i => i.Category == category).Sum(i => i.Amount);

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

                return Json(new { success = true, message = $"Submitted {items.Count} expense(s) in report #{report.Id}." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        

        [HttpGet("Receipt/{id:int}")]
        public async Task<IActionResult> Receipt(int id)
        {
            var currentUserId = _userManager.GetUserId(User);

            var item = await _db.ExpenseItems
                .Include(i => i.Report)
                .FirstOrDefaultAsync(i => i.Id == id);

            if (item != null)
            {
                var isOwner = item.Report != null && item.Report.UserId == currentUserId;
                var canView = isOwner || User.IsInRole("Manager") || User.IsInRole("Finance") || User.IsInRole("CEO");

                if (!canView)
                    return Forbid();

                if (item.ReceiptData != null && item.ReceiptData.Length > 0)
                    return File(item.ReceiptData, item.ReceiptContentType ?? "application/octet-stream");

                if (!string.IsNullOrEmpty(item.ReceiptPath))
                    return Redirect(item.ReceiptPath);

                return NotFound();
            }

            var expense = await _db.Expenses.FindAsync(id);
            if (expense == null)
                return NotFound();

            var legacyOwner = expense.UserId != null && expense.UserId == currentUserId;
            var legacyCanView = legacyOwner || User.IsInRole("Manager") || User.IsInRole("Finance") || User.IsInRole("CEO");

            if (!legacyCanView)
                return Forbid();

            if (expense.ReceiptData != null && expense.ReceiptData.Length > 0)
            {
                return File(expense.ReceiptData, expense.ReceiptContentType ?? "application/octet-stream");
            }

            if (!string.IsNullOrEmpty(expense.ReceiptPath))
            {
                return Redirect(expense.ReceiptPath);
            }

            return NotFound();
        }

        [HttpGet("History")]
        public async Task<IActionResult> History(DateTime? start, DateTime? end, string status)
        {
            var userId = _userManager.GetUserId(User);
            var q = _db.ExpenseItems
                .Include(i => i.Report)
                .Where(i => i.Report != null && i.Report.UserId == userId);

            if (start.HasValue)
                q = q.Where(i => i.Date >= start.Value);
            if (end.HasValue)
                q = q.Where(i => i.Date <= end.Value);
            if (!string.IsNullOrEmpty(status) && Enum.TryParse<ReportStatus>(status, out var s))
                q = q.Where(i => i.Report != null && i.Report.Status == s);

            var itemsRaw = await q
                .OrderByDescending(i => i.Date)
                .ToListAsync();

            var items = itemsRaw.Select(i => new ExpenseItemSummaryDto
            {
                Item = i,
                ReportStatus = i.Report != null ? i.Report.Status : ReportStatus.Submitted
            }).ToList();

            ViewBag.Items = items;
            return View("History/Index");
        }

        public IActionResult Index()
        {
            return RedirectToAction("Dashboard");
        }
    }
}