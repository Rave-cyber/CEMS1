using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using System.Threading.Tasks;
using System.Linq;
using System;
using CEMS.Services;
using Microsoft.EntityFrameworkCore;

namespace CEMS.Controllers
{
    [Authorize]
    [Route("Receipt")]
    public class ReceiptController : Controller
    {
        private readonly Data.ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IS3StorageService _s3Service;

        public ReceiptController(Data.ApplicationDbContext db, UserManager<IdentityUser> userManager, IS3StorageService s3Service)
        {
            _db = db;
            _userManager = userManager;
            _s3Service = s3Service;
        }

        [HttpGet("View/{id:int}")]
        public async Task<IActionResult> View(int id)
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
                {
                    // Try S3 pre-signed URL first when enabled
                    if (_s3Service.IsEnabled)
                    {
                        var url = _s3Service.GetPreSignedUrl(item.ReceiptPath);
                        if (!string.IsNullOrEmpty(url))
                            return Redirect(url);
                    }

                    // If stored path is a bare S3 key (e.g. "receipts/2026/03/05/..."),
                    // convert to root-relative (/receipts/...) so browser requests from site root
                    if (!item.ReceiptPath.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                        !item.ReceiptPath.StartsWith("/", StringComparison.Ordinal))
                    {
                        var rootRelative = "/" + item.ReceiptPath.Replace("\\", "/");
                        return Redirect(rootRelative);
                    }

                    // Fallback: redirect to whatever path is stored (absolute or relative)
                    return Redirect(item.ReceiptPath);
                }

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
                // Try S3 pre-signed URL first when enabled
                if (_s3Service.IsEnabled)
                {
                    var url = _s3Service.GetPreSignedUrl(expense.ReceiptPath);
                    if (!string.IsNullOrEmpty(url))
                        return Redirect(url);
                }

                // If stored path is a bare S3 key, convert to root-relative
                if (!expense.ReceiptPath.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                    !expense.ReceiptPath.StartsWith("/", StringComparison.Ordinal))
                {
                    var rootRelative = "/" + expense.ReceiptPath.Replace("\\", "/");
                    return Redirect(rootRelative);
                }

                // Fallback: redirect to whatever path is stored
                return Redirect(expense.ReceiptPath);
            }

            return NotFound();
        }
    }
}
