#nullable disable
using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Caching.Memory;
using CEMS.Data;
using CEMS.Services;

namespace CEMS.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class ForgotPasswordModel : PageModel
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ApplicationDbContext _db;
        private readonly IGmailService _gmailService;
        private readonly IMemoryCache _cache;

        private const string OTP_PREFIX    = "otp_fp_";
        private const string EMAIL_PREFIX  = "otp_email_";
        private const int    OTP_MINUTES   = 10;

        public ForgotPasswordModel(
            UserManager<IdentityUser> userManager,
            ApplicationDbContext db,
            IGmailService gmailService,
            IMemoryCache cache)
        {
            _userManager  = userManager;
            _db           = db;
            _gmailService = gmailService;
            _cache        = cache;
        }

        // ── Step 1: enter account email ──────────────────────────────────────
        [BindProperty] public string Step { get; set; } = "email";

        [BindProperty]
        [Required, EmailAddress]
        public string AccountEmail { get; set; }

        // ── Step 2: enter OTP ────────────────────────────────────────────────
        [BindProperty]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "OTP must be 6 digits.")]
        public string Otp { get; set; }

        // ── Step 3: new password ─────────────────────────────────────────────
        [BindProperty]
        [StringLength(100, MinimumLength = 6)]
        [RegularExpression(
            @"^(?=.*[0-9])(?=.*[!@#$%^&*()_+\-=\[\]{};':""\\|,.<>\/?]).{6,}$",
            ErrorMessage = "Password must be at least 6 characters with 1 number and 1 special character.")]
        [DataType(DataType.Password)]
        public string NewPassword { get; set; }

        [BindProperty]
        [DataType(DataType.Password)]
        [Compare("NewPassword", ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; }

        // Masked Gmail shown on OTP step
        public string MaskedGmail { get; set; }

        public IActionResult OnGet() => Page();

        // ── POST: Step 1 — find account & send OTP ───────────────────────────
        public async Task<IActionResult> OnPostSendOtpAsync()
        {
            if (string.IsNullOrWhiteSpace(AccountEmail))
            {
                ModelState.AddModelError(nameof(AccountEmail), "Email is required.");
                Step = "email";
                return Page();
            }

            var user = await _userManager.FindByEmailAsync(AccountEmail.Trim());
            if (user == null)
            {
                // Don't reveal whether the account exists
                Step = "email";
                ModelState.AddModelError(string.Empty,
                    "If that account exists and has a linked Gmail, an OTP has been sent.");
                return Page();
            }

            // Find linked Gmail + refresh token from any profile
            var (gmailAddress, refreshToken) = await GetLinkedGmail(user.Id);

            if (string.IsNullOrEmpty(gmailAddress))
            {
                Step = "email";
                ModelState.AddModelError(string.Empty,
                    "This account does not have a linked Gmail address. " +
                    "Please log in with your password and connect Gmail from your profile first.");
                return Page();
            }

            // Generate cryptographically secure 6-digit OTP
            var otp = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            _cache.Set(OTP_PREFIX   + user.Id, otp,          TimeSpan.FromMinutes(OTP_MINUTES));
            _cache.Set(EMAIL_PREFIX + user.Id, AccountEmail,  TimeSpan.FromMinutes(OTP_MINUTES));

            // Send via Gmail API
            var html = $@"
<div style='font-family:Segoe UI,sans-serif;max-width:480px;margin:0 auto;padding:32px;background:#f8fafc;border-radius:12px;'>
  <div style='text-align:center;margin-bottom:24px;'>
    <h2 style='color:#1a6bb5;margin:0;'>CEMS Password Reset</h2>
  </div>
  <div style='background:white;border-radius:10px;padding:28px;box-shadow:0 2px 8px rgba(0,0,0,.06);'>
    <p style='color:#374151;margin-top:0;'>Your one-time password (OTP) for resetting your CEMS account password is:</p>
    <div style='text-align:center;margin:24px 0;'>
      <span style='font-size:2.5rem;font-weight:800;letter-spacing:12px;color:#1a6bb5;background:#eff6ff;padding:16px 28px;border-radius:10px;display:inline-block;'>{otp}</span>
    </div>
    <p style='color:#6b7280;font-size:.9rem;'>This OTP expires in <strong>{OTP_MINUTES} minutes</strong>. Do not share it with anyone.</p>
    <p style='color:#6b7280;font-size:.9rem;margin-bottom:0;'>If you did not request a password reset, you can safely ignore this email.</p>
  </div>
  <p style='text-align:center;color:#9ca3af;font-size:.8rem;margin-top:20px;'>CEMS — Company Expense Management System</p>
</div>";

            var (sent, sendError) = await _gmailService.SendEmailWithErrorAsync(refreshToken, gmailAddress,
                "CEMS Password Reset OTP", html);

            if (!sent)
            {
                Step = "email";
                ModelState.AddModelError(string.Empty,
                    $"Gmail send failed: {sendError}");
                return Page();
            }

            // Move to OTP step
            Step        = "otp";
            MaskedGmail = MaskEmail(gmailAddress);
            TempData["fp_userId"] = user.Id;
            return Page();
        }

        // ── POST: Step 2 — verify OTP ────────────────────────────────────────
        public async Task<IActionResult> OnPostVerifyOtpAsync()
        {
            var userId = TempData.Peek("fp_userId") as string;
            if (string.IsNullOrEmpty(userId))
            {
                Step = "email";
                ModelState.AddModelError(string.Empty, "Session expired. Please start again.");
                return Page();
            }

            var storedOtp = _cache.Get<string>(OTP_PREFIX + userId);
            if (string.IsNullOrEmpty(storedOtp) || storedOtp != Otp?.Trim())
            {
                Step        = "otp";
                MaskedGmail = "your linked Gmail";
                TempData.Keep("fp_userId");
                ModelState.AddModelError(nameof(Otp), "Invalid or expired OTP. Please try again.");
                return Page();
            }

            // OTP valid — move to password step
            _cache.Remove(OTP_PREFIX + userId);
            TempData.Keep("fp_userId");
            Step = "password";
            return Page();
        }

        // ── POST: Step 3 — set new password ──────────────────────────────────
        public async Task<IActionResult> OnPostResetPasswordAsync()
        {
            var userId = TempData["fp_userId"] as string;
            if (string.IsNullOrEmpty(userId))
            {
                Step = "email";
                ModelState.AddModelError(string.Empty, "Session expired. Please start again.");
                return Page();
            }

            if (!ModelState.IsValid)
            {
                Step = "password";
                TempData["fp_userId"] = userId;
                return Page();
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                Step = "email";
                ModelState.AddModelError(string.Empty, "Account not found.");
                return Page();
            }

            var token  = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, token, NewPassword);

            if (!result.Succeeded)
            {
                Step = "password";
                TempData["fp_userId"] = userId;
                foreach (var e in result.Errors)
                    ModelState.AddModelError(string.Empty, e.Description);
                return Page();
            }

            return RedirectToPage("./ResetPasswordConfirmation");
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private async Task<(string gmail, string refreshToken)> GetLinkedGmail(string userId)
        {
            // Use ToListAsync then FirstOrDefault to avoid ambiguity between
            // EF Core and System.Linq.Async FirstOrDefaultAsync overloads
            // Note: only require GmailAddress — token may be stored as access token
            var drvList = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .ToListAsync(_db.DriverProfiles.Where(p => p.UserId == userId && p.GmailAddress != null));
            var drv = drvList.FirstOrDefault();
            if (drv != null && !string.IsNullOrEmpty(drv.GmailAddress))
                return (drv.GmailAddress!, drv.GmailRefreshToken ?? "");

            var mgrList = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .ToListAsync(_db.ManagerProfiles.Where(p => p.UserId == userId && p.GmailAddress != null));
            var mgr = mgrList.FirstOrDefault();
            if (mgr != null && !string.IsNullOrEmpty(mgr.GmailAddress))
                return (mgr.GmailAddress!, mgr.GmailRefreshToken ?? "");

            var ceoList = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .ToListAsync(_db.CEOProfiles.Where(p => p.UserId == userId && p.GmailAddress != null));
            var ceo = ceoList.FirstOrDefault();
            if (ceo != null && !string.IsNullOrEmpty(ceo.GmailAddress))
                return (ceo.GmailAddress!, ceo.GmailRefreshToken ?? "");

            var finList = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .ToListAsync(_db.FinanceProfiles.Where(p => p.UserId == userId && p.GmailAddress != null));
            var fin = finList.FirstOrDefault();
            if (fin != null && !string.IsNullOrEmpty(fin.GmailAddress))
                return (fin.GmailAddress!, fin.GmailRefreshToken ?? "");

            return (null, null);
        }

        private static string MaskEmail(string email)
        {
            var at  = email.IndexOf('@');
            if (at <= 1) return email;
            var name   = email[..at];
            var domain = email[at..];
            var visible = name.Length > 3 ? name[..3] : name[..1];
            return visible + new string('*', name.Length - visible.Length) + domain;
        }
    }
}
