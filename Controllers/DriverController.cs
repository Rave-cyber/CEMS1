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
        public IActionResult Dashboard()
        {
            // Load recent driver expenses dynamically
            var userId = _userManager.GetUserId(User);
            var expenses = _db.Expenses
                .Where(e => e.UserId == userId)
                .OrderByDescending(e => e.Date)
                .Take(10)
                .ToList();

            ViewBag.RecentExpenses = expenses;
            return View("Dashboard/Index");
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

        [HttpPost]
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

        [HttpPost]
        [Authorize(Roles = "Driver")]
        public async Task<IActionResult> SubmitMultiple()
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                if (user == null)
                    return Json(new { success = false, message = "User not found" });

                var items = new List<Expense>();
                // Bind dynamic rows from form-data: items[i].Field
                var files = Request.Form.Files;
                var form = Request.Form;

                int index = 0;
                while (true)
                {
                    var prefix = $"items[{index}]";
                    if (!form.ContainsKey(prefix + ".Category") && !form.ContainsKey(prefix + ".Amount"))
                        break;

                    var expense = new Expense();
                    expense.UserId = user.Id;
                    expense.Status = ExpenseStatus.Pending;
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

                await _db.Expenses.AddRangeAsync(items);
                await _db.SaveChangesAsync();

                return Json(new { success = true, message = $"Submitted {items.Count} expense(s)." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        

        [HttpGet]
        public async Task<IActionResult> Receipt(int id)
        {
            var expense = await _db.Expenses.FindAsync(id);
            if (expense == null)
                return NotFound();

            // Ensure current user owns the expense or user has Manager/Finance/CEO role
            var currentUserId = _userManager.GetUserId(User);
            var isOwner = expense.UserId != null && expense.UserId == currentUserId;
            var canView = isOwner || User.IsInRole("Manager") || User.IsInRole("Finance") || User.IsInRole("CEO");

            if (!canView)
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
            var q = _db.Expenses.Where(e => e.UserId == userId);
            if (start.HasValue)
                q = q.Where(e => e.Date >= start.Value);
            if (end.HasValue)
                q = q.Where(e => e.Date <= end.Value);
            if (!string.IsNullOrEmpty(status) && Enum.TryParse<ExpenseStatus>(status, out var s))
                q = q.Where(e => e.Status == s);

            var items = await q.OrderByDescending(e => e.Date).ToListAsync();
            ViewBag.Items = items;
            return View("History/Index");
        }

        public IActionResult Index()
        {
            return RedirectToAction("Dashboard");
        }
    }
}