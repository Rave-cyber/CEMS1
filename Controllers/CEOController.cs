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

        public CEOController(Data.ApplicationDbContext db, UserManager<IdentityUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        public IActionResult Dashboard()
        {
            return View("Dashboard/Index");
        }

        public IActionResult Budget()
        {
            return View("Budget/Index");
        }

        public IActionResult Reports()
        {
            return View("Reports/Index");
        }

        public IActionResult Users()
        {
            return View("Users/Index");
        }

        public IActionResult Index()
        {
            return RedirectToAction("Dashboard");
        }

        // List reports forwarded to CEO that need final approval
        public async Task<IActionResult> Approvals()
        {
            var reports = await _db.ExpenseReports
                .Where(r => r.ForwardedToCEO && !r.CEOApproved)
                .OrderByDescending(r => r.SubmissionDate)
                .ToListAsync();

            var userIds = reports.Select(r => r.UserId).Where(id => id != null).Distinct().ToList();
            var users = await _userManager.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName);

            var dto = reports.Select(r => new PendingExpenseReportDto { Report = r, UserName = r.UserId != null && users.ContainsKey(r.UserId) ? users[r.UserId] : "Unknown" }).ToList();
            ViewBag.Forwarded = dto;
            return View("Approvals/Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveForReimbursement(int id)
        {
            var report = await _db.ExpenseReports.FindAsync(id);
            if (report == null) return NotFound();

            report.CEOApproved = true;
            report.ForwardedToCEO = false; // now ready for finance
            await _db.SaveChangesAsync();
            TempData["Success"] = "Report approved for reimbursement.";
            return RedirectToAction("Approvals");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CEOApprove(int id, string? remarks)
        {
            var report = await _db.ExpenseReports.FindAsync(id);
            if (report == null) return NotFound();

            report.CEOApproved = true;
            report.ForwardedToCEO = false;

            var approval = new Models.Approval
            {
                ReportId = id,
                ApprovedByUserId = _userManager.GetUserId(User),
                Status = Models.ApprovalStatus.Approved,
                DecisionDate = DateTime.UtcNow,
                Stage = "CEO",
                Remarks = remarks
            };
            _db.Approvals.Add(approval);
            await _db.SaveChangesAsync();

            TempData["Success"] = "Report approved by CEO.";
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

            var approval = new Models.Approval
            {
                ReportId = id,
                ApprovedByUserId = _userManager.GetUserId(User),
                Status = Models.ApprovalStatus.Rejected,
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
        public async Task<IActionResult> RejectForReimbursement(int id)
        {
            var report = await _db.ExpenseReports.FindAsync(id);
            if (report == null) return NotFound();

            report.CEOApproved = false;
            report.ForwardedToCEO = false; // remove forwarding
            report.Status = ReportStatus.Rejected;
            await _db.SaveChangesAsync();
            TempData["Success"] = "Report rejected by CEO.";
            return RedirectToAction("Approvals");
        }
    }
}
