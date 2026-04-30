using System;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using System.Linq;

namespace CEMS.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class ExternalLoginModel : PageModel
    {
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ILogger<ExternalLoginModel> _logger;

        public ExternalLoginModel(
            SignInManager<IdentityUser> signInManager,
            UserManager<IdentityUser> userManager,
            ILogger<ExternalLoginModel> logger)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _logger = logger;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public string ProviderDisplayName { get; set; }
        public string ReturnUrl { get; set; }
        [TempData]
        public string ErrorMessage { get; set; }

        public class InputModel
        {
            [Required]
            [EmailAddress]
            public string Email { get; set; }
        }

        public IActionResult OnGet()
        {
            return RedirectToPage("./Login");
        }

        public IActionResult OnPost(string provider, string returnUrl = null)
        {
            var redirectUrl = Url.Page("./ExternalLogin", pageHandler: "Callback", values: new { returnUrl });
            var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
            return new ChallengeResult(provider, properties);
        }

        public async Task<IActionResult> OnGetCallbackAsync(string returnUrl = null, string remoteError = null)
        {
            returnUrl = returnUrl ?? Url.Content("~/");
            if (remoteError != null)
            {
                ErrorMessage = $"Error from external provider: {remoteError}";
                return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
            }

            var info = await _signInManager.GetExternalLoginInfoAsync();
            if (info == null)
            {
                ErrorMessage = "Error loading external login information. Your browser might be blocking cross-site cookies.";
                _logger.LogWarning("GetExternalLoginInfoAsync returned null. This is often caused by SameSite cookie policies over HTTP.");
                return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
            }

            // Sign in the user with this external login provider if the user already has a login.
            var result = await _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: true);
            if (result.Succeeded)
            {
                _logger.LogInformation("{Name} logged in with {LoginProvider} provider.", info.Principal.Identity.Name, info.LoginProvider);
                
                var loggedInUser = await _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
                var googleEmail = info.Principal.FindFirstValue(ClaimTypes.Email);
                
                // Generic Self-Heal: Check if they are logged into a dummy account instead of their real Profile account
                if (loggedInUser != null && !string.IsNullOrEmpty(googleEmail))
                {
                    IdentityUser correctUser = null;
                    var db = HttpContext.RequestServices.GetRequiredService<CEMS.Data.ApplicationDbContext>();
                    
                    var driver = db.DriverProfiles.FirstOrDefault(p => p.GmailAddress == googleEmail);
                    if (driver != null) correctUser = await _userManager.FindByIdAsync(driver.UserId);
                    
                    if (correctUser == null) {
                        var mgr = db.ManagerProfiles.FirstOrDefault(p => p.GmailAddress == googleEmail);
                        if (mgr != null) correctUser = await _userManager.FindByIdAsync(mgr.UserId);
                    }
                    if (correctUser == null) {
                        var ceo = db.CEOProfiles.FirstOrDefault(p => p.GmailAddress == googleEmail);
                        if (ceo != null) correctUser = await _userManager.FindByIdAsync(ceo.UserId);
                    }
                    if (correctUser == null) {
                        var fin = db.FinanceProfiles.FirstOrDefault(p => p.GmailAddress == googleEmail);
                        if (fin != null) correctUser = await _userManager.FindByIdAsync(fin.UserId);
                    }

                    // If they are in the wrong account, steal the login!
                    if (correctUser != null && correctUser.Id != loggedInUser.Id)
                    {
                        await _signInManager.SignOutAsync();
                        await _userManager.RemoveLoginAsync(loggedInUser, info.LoginProvider, info.ProviderKey);
                        await _userManager.AddLoginAsync(correctUser, info);
                        await _signInManager.SignInAsync(correctUser, isPersistent: false);
                        
                        // Clean up the dummy account
                        if (loggedInUser.Email == googleEmail) {
                            var roles = await _userManager.GetRolesAsync(loggedInUser);
                            if (roles.Count > 0) await _userManager.RemoveFromRolesAsync(loggedInUser, roles);
                            await _userManager.DeleteAsync(loggedInUser);
                        }

                        _logger.LogInformation("Corrected profile login mapping for {Email}", correctUser.Email);
                        return await RedirectToDashboardAsync(correctUser.Email, returnUrl);
                    }
                }
                
                var actualEmail = loggedInUser?.Email ?? googleEmail ?? "";
                return await RedirectToDashboardAsync(actualEmail, returnUrl);
            }
            if (result.IsLockedOut)
            {
                return RedirectToPage("./Lockout");
            }
            else
            {
                // If the user does not have an account, check if the email exists
                var email = info.Principal.FindFirstValue(ClaimTypes.Email);
                if (!string.IsNullOrEmpty(email))
                {
                    IdentityUser user = null;
                    
                    // Priority 1: Check if any existing profile explicitly connected this Gmail address
                    var db = HttpContext.RequestServices.GetRequiredService<CEMS.Data.ApplicationDbContext>();
                    var driver = db.DriverProfiles.FirstOrDefault(p => p.GmailAddress == email);
                    if (driver != null) user = await _userManager.FindByIdAsync(driver.UserId);
                    
                    if (user == null) {
                        var mgr = db.ManagerProfiles.FirstOrDefault(p => p.GmailAddress == email);
                        if (mgr != null) user = await _userManager.FindByIdAsync(mgr.UserId);
                    }
                    if (user == null) {
                        var ceo = db.CEOProfiles.FirstOrDefault(p => p.GmailAddress == email);
                        if (ceo != null) user = await _userManager.FindByIdAsync(ceo.UserId);
                    }
                    if (user == null) {
                        var fin = db.FinanceProfiles.FirstOrDefault(p => p.GmailAddress == email);
                        if (fin != null) user = await _userManager.FindByIdAsync(fin.UserId);
                    }

                    // Priority 2: Fallback to checking the exact Identity Email
                    if (user == null)
                    {
                        user = await _userManager.FindByEmailAsync(email);
                    }

                    if (user != null)
                    {
                        // Auto-link the account!
                        var addLoginResult = await _userManager.AddLoginAsync(user, info);
                        if (addLoginResult.Succeeded)
                        {
                            await _signInManager.SignInAsync(user, isPersistent: false);
                            _logger.LogInformation("Automatically linked external login to user {Email}.", user.Email);
                            return await RedirectToDashboardAsync(user.Email, returnUrl);
                        }
                    }
                }

                // If no account exists, we ask the user to create an account.
                ReturnUrl = returnUrl;
                ProviderDisplayName = info.ProviderDisplayName;
                if (info.Principal.HasClaim(c => c.Type == ClaimTypes.Email))
                {
                    Input = new InputModel
                    {
                        Email = info.Principal.FindFirstValue(ClaimTypes.Email)
                    };
                }
                return Page();
            }
        }

        public async Task<IActionResult> OnPostConfirmationAsync(string returnUrl = null)
        {
            returnUrl = returnUrl ?? Url.Content("~/");
            // Get the information about the user from the external login provider
            var info = await _signInManager.GetExternalLoginInfoAsync();
            if (info == null)
            {
                ErrorMessage = "Error loading external login information during confirmation.";
                return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
            }

            if (ModelState.IsValid)
            {
                var user = new IdentityUser { UserName = Input.Email, Email = Input.Email, EmailConfirmed = true };
                var result = await _userManager.CreateAsync(user);
                if (result.Succeeded)
                {
                    // Assign default role as "Manager" for new users
                    await _userManager.AddToRoleAsync(user, "Manager");

                    result = await _userManager.AddLoginAsync(user, info);
                    if (result.Succeeded)
                    {
                        await _signInManager.SignInAsync(user, isPersistent: false);
                        _logger.LogInformation("User created an account using {Name} provider.", info.LoginProvider);
                        return await RedirectToDashboardAsync(user.Email, returnUrl);
                    }
                }
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }

            ProviderDisplayName = info.ProviderDisplayName;
            ReturnUrl = returnUrl;
            return Page();
        }

        private async Task<IActionResult> RedirectToDashboardAsync(string email, string defaultUrl)
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user != null)
            {
                var roles = await _userManager.GetRolesAsync(user);
                
                // Self-heal: If user has no roles, assign them to Manager
                if (roles.Count == 0)
                {
                    await _userManager.AddToRoleAsync(user, "Manager");
                    roles.Add("Manager");
                }
                
                if (roles.Contains("SuperAdmin")) return RedirectToAction("Dashboard", "SuperAdmin");
                if (roles.Contains("CEO")) return RedirectToAction("Dashboard", "CEO");
                if (roles.Contains("Manager")) return RedirectToAction("Dashboard", "Manager");
                if (roles.Contains("Driver")) return RedirectToAction("Dashboard", "Driver");
                if (roles.Contains("Finance")) return RedirectToAction("Dashboard", "Finance");
            }
            return LocalRedirect(defaultUrl);
        }
    }
}
