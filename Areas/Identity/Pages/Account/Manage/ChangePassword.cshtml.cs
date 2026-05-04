using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using CEMS.Data;
using CEMS.Models;

namespace CEMS.Areas.Identity.Pages.Account.Manage
{
    public class ChangePasswordModel : PageModel
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly ILogger<ChangePasswordModel> _logger;
        private readonly ApplicationDbContext _db;

        public ChangePasswordModel(
            UserManager<IdentityUser> userManager,
            SignInManager<IdentityUser> signInManager,
            ILogger<ChangePasswordModel> logger,
            ApplicationDbContext db)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _logger = logger;
            _db = db;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        [TempData]
        public string? StatusMessage { get; set; }

        /// <summary>True when the user does NOT have 2FA enabled.</summary>
        public bool Requires2faSetup { get; set; }

        public class InputModel
        {
            [Required]
            [DataType(DataType.Password)]
            [Display(Name = "Current password")]
            public string OldPassword { get; set; } = "";

            [Required]
            [StringLength(100, MinimumLength = 6,
                ErrorMessage = "The {0} must be at least {2} and at most {1} characters.")]
            [DataType(DataType.Password)]
            [Display(Name = "New password")]
            public string NewPassword { get; set; } = "";

            [DataType(DataType.Password)]
            [Display(Name = "Confirm new password")]
            [Compare("NewPassword", ErrorMessage = "Passwords do not match.")]
            public string ConfirmPassword { get; set; } = "";

            [Display(Name = "Authenticator Code")]
            public string? TwoFactorCode { get; set; }
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound();

            if (!await _userManager.HasPasswordAsync(user))
                return RedirectToPage("./SetPassword");

            Requires2faSetup = !await _userManager.GetTwoFactorEnabledAsync(user);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound();

            // Block if 2FA not set up
            Requires2faSetup = !await _userManager.GetTwoFactorEnabledAsync(user);
            if (Requires2faSetup)
            {
                ModelState.AddModelError(string.Empty,
                    "You must enable Google Authenticator before changing your password.");
                return Page();
            }

            if (!ModelState.IsValid)
                return Page();

            // Validate the authenticator code
            if (string.IsNullOrWhiteSpace(Input.TwoFactorCode))
            {
                ModelState.AddModelError("Input.TwoFactorCode",
                    "Authenticator code is required.");
                return Page();
            }

            var code = Input.TwoFactorCode.Replace(" ", "").Replace("-", "");
            var valid = await _userManager.VerifyTwoFactorTokenAsync(
                user, _userManager.Options.Tokens.AuthenticatorTokenProvider, code);

            if (!valid)
            {
                ModelState.AddModelError("Input.TwoFactorCode",
                    "Invalid authenticator code. Please try again.");
                return Page();
            }

            // Change the password
            var result = await _userManager.ChangePasswordAsync(
                user, Input.OldPassword, Input.NewPassword);

            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                {
                    _logger.LogWarning("ChangePassword error for {Id}: {Code} - {Desc}",
                        user.Id, error.Code, error.Description);
                    ModelState.AddModelError(string.Empty, error.Description);
                }
                return Page();
            }

            // Force update the security stamp so all other sessions are invalidated
            await _userManager.UpdateSecurityStampAsync(user);
            await _signInManager.RefreshSignInAsync(user);
            _logger.LogInformation("User {Id} successfully changed password via 2FA.", user.Id);

            _db.AuditLogs.Add(new AuditLog
            {
                Action = "ChangePassword",
                Module = "Security",
                Role = (await _userManager.GetRolesAsync(user)).FirstOrDefault(),
                PerformedByUserId = user.Id,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                Details = "Password changed — verified with Google Authenticator."
            });
            await _db.SaveChangesAsync();

            StatusMessage = "✅ Your password has been changed. Please log in again if prompted.";
            return RedirectToPage();
        }
    }
}
