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
                        var rawUrl = _s3Service.GetPreSignedUrl(item.ReceiptPath);
                        // Scheme validated before redirect — SCS0027 suppressed (false positive after validation)
#pragma warning disable SCS0027
                        if (!string.IsNullOrEmpty(rawUrl)
                            && Uri.TryCreate(rawUrl, UriKind.Absolute, out var parsedS3Url)
                            && (parsedS3Url.Scheme == Uri.UriSchemeHttps || parsedS3Url.Scheme == Uri.UriSchemeHttp))
                            return Redirect(parsedS3Url.AbsoluteUri);
#pragma warning restore SCS0027
                    }

                    // Bare S3 key — convert to root-relative path (no external redirect)
                    if (!item.ReceiptPath.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                        !item.ReceiptPath.StartsWith("/", StringComparison.Ordinal))
                    {
                        var sanitized = item.ReceiptPath.Replace("\\", "/").TrimStart('/');
                        return LocalRedirect("/" + sanitized);
                    }

                    // Absolute URL — scheme validated, AbsoluteUri used (not raw DB string)
#pragma warning disable SCS0027
                    if (Uri.TryCreate(item.ReceiptPath, UriKind.Absolute, out var parsedUrl)
                        && (parsedUrl.Scheme == Uri.UriSchemeHttps || parsedUrl.Scheme == Uri.UriSchemeHttp))
                        return Redirect(parsedUrl.AbsoluteUri);
#pragma warning restore SCS0027

                    // Root-relative path — LocalRedirect prevents open redirect
                    if (item.ReceiptPath.StartsWith("/", StringComparison.Ordinal))
                        return LocalRedirect(item.ReceiptPath);

                    return NotFound();
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
                    var rawUrl = _s3Service.GetPreSignedUrl(expense.ReceiptPath);
                    // Scheme validated before redirect — SCS0027 suppressed (false positive after validation)
#pragma warning disable SCS0027
                    if (!string.IsNullOrEmpty(rawUrl)
                        && Uri.TryCreate(rawUrl, UriKind.Absolute, out var parsedS3Url)
                        && (parsedS3Url.Scheme == Uri.UriSchemeHttps || parsedS3Url.Scheme == Uri.UriSchemeHttp))
                        return Redirect(parsedS3Url.AbsoluteUri);
#pragma warning restore SCS0027
                }

                // Bare S3 key — convert to root-relative path (no external redirect)
                if (!expense.ReceiptPath.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                    !expense.ReceiptPath.StartsWith("/", StringComparison.Ordinal))
                {
                    var sanitized = expense.ReceiptPath.Replace("\\", "/").TrimStart('/');
                    return LocalRedirect("/" + sanitized);
                }

                // Absolute URL — scheme validated, AbsoluteUri used (not raw DB string)
#pragma warning disable SCS0027
                if (Uri.TryCreate(expense.ReceiptPath, UriKind.Absolute, out var parsedUrl)
                    && (parsedUrl.Scheme == Uri.UriSchemeHttps || parsedUrl.Scheme == Uri.UriSchemeHttp))
                    return Redirect(parsedUrl.AbsoluteUri);
#pragma warning restore SCS0027

                // Root-relative path — LocalRedirect prevents open redirect
                if (expense.ReceiptPath.StartsWith("/", StringComparison.Ordinal))
                    return LocalRedirect(expense.ReceiptPath);

                return NotFound();
            }

            return NotFound();
        }
    }
}
