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
            // Load pending expenses for manager review
            var pending = await _db.Expenses
                .Where(e => e.Status == Models.ExpenseStatus.Pending)
                .OrderByDescending(e => e.Date)
                .Take(20)
                .ToListAsync();

            // Collect user ids and load usernames in one query
            var userIds = pending.Select(e => e.UserId).Where(id => id != null).Distinct().ToList();
            var users = await _userManager.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName);

            var pendingWithUsers = pending.Select(e => new Models.PendingExpenseDto {
                Expense = e,
                UserName = (e.UserId != null && users.ContainsKey(e.UserId)) ? users[e.UserId] : "Unknown"
            }).ToList();

            ViewBag.Pending = pendingWithUsers;
            return View("Dashboard/Index");
        }

        // Diagnostic endpoint for managers to verify DB contents and pending counts
        [HttpGet]
        public async Task<IActionResult> Diagnostics()
        {
            var total = await _db.Expenses.CountAsync();
            var pendingCount = await _db.Expenses.Where(e => e.Status == ExpenseStatus.Pending).CountAsync();
            var approvedCount = await _db.Expenses.Where(e => e.Status == ExpenseStatus.Approved).CountAsync();
            var rejectedCount = await _db.Expenses.Where(e => e.Status == ExpenseStatus.Rejected).CountAsync();

            var recent = await _db.Expenses.OrderByDescending(e => e.Date).Take(20)
                .Select(e => new { e.Id, e.UserId, e.Category, e.Amount, e.Date, e.Status })
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

        public IActionResult Budget()
        {
            return View("Budget/Index");
        }

        public IActionResult Index()
        {
            return RedirectToAction("Dashboard");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Approve(int id)
        {
            var expense = await _db.Expenses.FindAsync(id);
            if (expense == null) return NotFound();

            expense.Status = Models.ExpenseStatus.Approved;

            // Deduct from budget if category exists
            var budget = _db.Budgets.FirstOrDefault(b => b.Category == expense.Category);
            if (budget != null)
            {
                budget.Spent += expense.Amount;
            }

            await _db.SaveChangesAsync();
            return RedirectToAction("Dashboard");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reject(int id)
        {
            var expense = await _db.Expenses.FindAsync(id);
            if (expense == null) return NotFound();

            expense.Status = Models.ExpenseStatus.Rejected;
            await _db.SaveChangesAsync();
            return RedirectToAction("Dashboard");
        }
    }
}