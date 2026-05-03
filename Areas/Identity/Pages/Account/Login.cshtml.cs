
#nullable disable

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using CEMS.Data;
using CEMS.Models;
using CEMS.Services;

namespace CEMS.Areas.Identity.Pages.Account
{
    public class LoginModel : PageModel
    {
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ILogger<LoginModel> _logger;
        private readonly ApplicationDbContext _db;
        private readonly ILoginAttemptTracker _attemptTracker;
        private readonly ISecurityThreatDetector _threatDetector;

        public LoginModel(SignInManager<IdentityUser> signInManager, UserManager<IdentityUser> userManager,
            ILogger<LoginModel> logger, ApplicationDbContext db,
            ILoginAttemptTracker attemptTracker, ISecurityThreatDetector threatDetector)
        {
            _signInManager    = signInManager;
            _userManager      = userManager;
            _logger           = logger;
            _db               = db;
            _attemptTracker   = attemptTracker;
            _threatDetector   = threatDetector;
        }

  
        [BindProperty]
        public InputModel Input { get; set; }

        public IList<AuthenticationScheme> ExternalLogins { get; set; }

        public string ReturnUrl { get; set; }

        [TempData]
        public string ErrorMessage { get; set; }

        // Properties for lockout display
        public bool IsLockedOut { get; set; }
        public int? RemainingSeconds { get; set; }
        public int FailedAttempts { get; set; }

      
        public class InputModel
        {
          
            [Required]
            [EmailAddress]
            public string Email { get; set; }

            [Required]
            [DataType(DataType.Password)]
            public string Password { get; set; }

           
            [Display(Name = "Remember me?")]
            public bool RememberMe { get; set; }
        }

        public async Task OnGetAsync(string returnUrl = null)
        {
            if (!string.IsNullOrEmpty(ErrorMessage))
            {
                ModelState.AddModelError(string.Empty, ErrorMessage);
            }

            returnUrl ??= Url.Content("~/");

            // Clear the existing external cookie to ensure a clean login process
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

            ReturnUrl = returnUrl;

            // Check for lockout on page load
            var emailFromQuery = HttpContext.Request.Query["email"].ToString();
            if (!string.IsNullOrEmpty(emailFromQuery))
            {
                IsLockedOut = await _attemptTracker.IsLockedOutAsync(emailFromQuery);
                RemainingSeconds = await _attemptTracker.GetRemainingSecondsAsync(emailFromQuery);
                FailedAttempts = await _attemptTracker.GetFailedAttemptsAsync(emailFromQuery);
            }
        }

        public async Task<IActionResult> OnPostAsync(string returnUrl = null)
        {
            returnUrl ??= Url.Content("~/");

            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

            // Capture request context for threat detection
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
                         ?? HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()
                         ?? "unknown";
            var userAgent = HttpContext.Request.Headers["User-Agent"].ToString();

            // Check IP-level block first
            if (await _attemptTracker.IsIpBlockedAsync(ipAddress))
            {
                ModelState.AddModelError(string.Empty, "Too many failed attempts from your network. Please try again later.");
                return Page();
            }

            // Check if user is locked out
            IsLockedOut = await _attemptTracker.IsLockedOutAsync(Input.Email);
            RemainingSeconds = await _attemptTracker.GetRemainingSecondsAsync(Input.Email);
            FailedAttempts = await _attemptTracker.GetFailedAttemptsAsync(Input.Email);

            if (IsLockedOut)
            {
                ModelState.AddModelError(string.Empty, $"Too many failed attempts. Please wait {RemainingSeconds} seconds before trying again.");
                return Page();
            }

            if (ModelState.IsValid)
            {
                var result = await _signInManager.PasswordSignInAsync(Input.Email, Input.Password, Input.RememberMe, lockoutOnFailure: false);
                if (result.Succeeded)
                {
                    await _attemptTracker.ClearAttemptsAsync(Input.Email);
                    _logger.LogInformation("User logged in.");

                    var user = await _userManager.FindByEmailAsync(Input.Email);
                    try
                    {
                        var userRoles = user == null ? new List<string>() : (await _userManager.GetRolesAsync(user)).ToList();
                        _db.AuditLogs.Add(new AuditLog
                        {
                            Action = "UserLogin",
                            Module = "Auth",
                            Role = userRoles.FirstOrDefault(),
                            PerformedByUserId = user?.Id,
                            IpAddress = ipAddress,
                            UserAgent = userAgent,
                            Details = $"Login successful for {Input.Email}"
                        });
                        await _db.SaveChangesAsync();
                    }
                    catch { }

                    // Run threat analysis (new IP detection etc.) — fire and forget errors
                    try { await _threatDetector.AnalyzeLoginAsync(Input.Email, ipAddress, userAgent, succeeded: true); } catch { }

                    var isDefaultReturn = string.IsNullOrEmpty(returnUrl) || returnUrl == Url.Content("~/") || returnUrl == "/";
                    if (!isDefaultReturn) return LocalRedirect(returnUrl);

                    var rolesList = user == null ? new List<string>() : (await _userManager.GetRolesAsync(user)).ToList();
                    if (user != null && rolesList.Count == 0)
                    {
                        await _userManager.AddToRoleAsync(user, "Manager");
                        rolesList.Add("Manager");
                    }

                    if (rolesList.Contains("SuperAdmin")) return RedirectToAction("Dashboard", "SuperAdmin");
                    if (rolesList.Contains("CEO"))        return RedirectToAction("Dashboard", "CEO");
                    if (rolesList.Contains("Manager"))    return RedirectToAction("Dashboard", "Manager");
                    if (rolesList.Contains("Driver"))     return RedirectToAction("Dashboard", "Driver");
                    if (rolesList.Contains("Finance"))    return RedirectToAction("Dashboard", "Finance");

                    return LocalRedirect(returnUrl);
                }
                if (result.RequiresTwoFactor)
                {
                    await _attemptTracker.ClearAttemptsAsync(Input.Email);
                    return RedirectToPage("./LoginWith2fa", new { ReturnUrl = returnUrl, RememberMe = Input.RememberMe });
                }
                if (result.IsLockedOut)
                {
                    _logger.LogWarning("User account locked out.");
                    return RedirectToPage("./Lockout");
                }
                else
                {
                    await _attemptTracker.RecordFailedAttemptAsync(Input.Email);
                    await _attemptTracker.RecordFailedAttemptByIpAsync(ipAddress);

                    FailedAttempts   = await _attemptTracker.GetFailedAttemptsAsync(Input.Email);
                    RemainingSeconds = await _attemptTracker.GetRemainingSecondsAsync(Input.Email);

                    try
                    {
                        var failedUser = await _userManager.FindByEmailAsync(Input.Email);
                        _db.AuditLogs.Add(new AuditLog
                        {
                            Action = "FailedLoginAttempt",
                            Module = "Auth",
                            PerformedByUserId = null,
                            TargetUserId = failedUser?.Id,
                            IpAddress = ipAddress,
                            UserAgent = userAgent,
                            Details = $"Failed login attempt for {Input.Email} (Attempt {FailedAttempts}/5)"
                        });
                        await _db.SaveChangesAsync();
                    }
                    catch { }

                    // Run threat analysis — brute force / credential stuffing detection
                    try { await _threatDetector.AnalyzeLoginAsync(Input.Email, ipAddress, userAgent, succeeded: false); } catch { }

                    IsLockedOut = await _attemptTracker.IsLockedOutAsync(Input.Email);
                    if (IsLockedOut && RemainingSeconds.HasValue)
                        ModelState.AddModelError(string.Empty, $"Too many failed attempts ({FailedAttempts}/5). Account locked for {RemainingSeconds} seconds.");
                    else
                        ModelState.AddModelError(string.Empty, $"Invalid login attempt. Attempts remaining: {5 - FailedAttempts}");

                    return Page();
                }
            }

            return Page();
        }
    }
}
