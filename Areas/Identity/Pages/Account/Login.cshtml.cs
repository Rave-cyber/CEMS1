
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

namespace CEMS.Areas.Identity.Pages.Account
{
    public class LoginModel : PageModel
    {
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ILogger<LoginModel> _logger;
        private readonly ApplicationDbContext _db;

        public LoginModel(SignInManager<IdentityUser> signInManager, UserManager<IdentityUser> userManager, ILogger<LoginModel> logger, ApplicationDbContext db)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _logger = logger;
            _db = db;
        }

  
        [BindProperty]
        public InputModel Input { get; set; }

        public IList<AuthenticationScheme> ExternalLogins { get; set; }

        public string ReturnUrl { get; set; }

        [TempData]
        public string ErrorMessage { get; set; }

      
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
        }

        public async Task<IActionResult> OnPostAsync(string returnUrl = null)
        {
            returnUrl ??= Url.Content("~/");

            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

            if (ModelState.IsValid)
            {
         
                var result = await _signInManager.PasswordSignInAsync(Input.Email, Input.Password, Input.RememberMe, lockoutOnFailure: false);
                if (result.Succeeded)
                {
                    _logger.LogInformation("User logged in.");

                    // Fetch user once and record audit log for successful login
                    var user = await _userManager.FindByEmailAsync(Input.Email);
                    try
                    {
                        var log = new AuditLog
                        {
                            Action = "UserLogin",
                            Module = "Auth",
                            PerformedByUserId = user?.Id,
                            Details = $"Login successful for {Input.Email}"
                        };
                        _db.AuditLogs.Add(log);
                        await _db.SaveChangesAsync();
                    }
                    catch { /* ignore audit failures */ }


                    var isDefaultReturn = string.IsNullOrEmpty(returnUrl) || returnUrl == Url.Content("~/") || returnUrl == "/";

                    if (!isDefaultReturn)
                    {
                        return LocalRedirect(returnUrl);
                    }


                    var rolesList = user == null ? new List<string>() : (await _userManager.GetRolesAsync(user)).ToList();

                    // Self-heal: If user has no roles, assign them to Manager
                    if (user != null && rolesList.Count == 0)
                    {
                        await _userManager.AddToRoleAsync(user, "Manager");
                        rolesList.Add("Manager");
                    }

                    if (rolesList.Contains("SuperAdmin"))
                        return RedirectToAction("Dashboard", "SuperAdmin");
                    if (rolesList.Contains("CEO"))
                        return RedirectToAction("Dashboard", "CEO");
                    if (rolesList.Contains("Manager"))
                        return RedirectToAction("Dashboard", "Manager");
                    if (rolesList.Contains("Driver"))
                        return RedirectToAction("Dashboard", "Driver");
                    if (rolesList.Contains("Finance"))
                        return RedirectToAction("Dashboard", "Finance");

                    // Fallback to home index
                    return LocalRedirect(returnUrl);
                }
                if (result.RequiresTwoFactor)
                {
                    return RedirectToPage("./LoginWith2fa", new { ReturnUrl = returnUrl, RememberMe = Input.RememberMe });
                }
                if (result.IsLockedOut)
                {
                    _logger.LogWarning("User account locked out.");
                    return RedirectToPage("./Lockout");
                }
                else
                {
                    // Log failed login attempt
                    try
                    {
                        var log = new AuditLog
                        {
                            Action = "FailedLoginAttempt",
                            Module = "Auth",
                            PerformedByUserId = null,
                            Details = $"Failed login attempt for {Input.Email}"
                        };
                        _db.AuditLogs.Add(log);
                        await _db.SaveChangesAsync();
                    }
                    catch { }

                    ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                    return Page();
                }
            }

            return Page();
        }
    }
}
