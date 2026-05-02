// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using CEMS.Data;
using CEMS.Models;

namespace CEMS.Areas.Identity.Pages.Account
{
    public class LogoutModel : PageModel
    {
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ILogger<LogoutModel> _logger;
        private readonly ApplicationDbContext _db;

        public LogoutModel(SignInManager<IdentityUser> signInManager, UserManager<IdentityUser> userManager, ILogger<LogoutModel> logger, ApplicationDbContext db)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _logger = logger;
            _db = db;
        }

        public async Task<IActionResult> OnPost(string returnUrl = null)
        {
            // Capture identity before signing out
            var userId = User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var userEmail = User?.Identity?.Name;

            string? role = null;
            if (!string.IsNullOrEmpty(userId))
            {
                var identityUser = await _userManager.FindByIdAsync(userId);
                if (identityUser != null)
                {
                    var roles = await _userManager.GetRolesAsync(identityUser);
                    role = roles.FirstOrDefault();
                }
            }

            await _signInManager.SignOutAsync();
            _logger.LogInformation("User logged out.");

            try
            {
                var log = new CEMS.Models.AuditLog
                {
                    Action = "UserLogout",
                    Module = "Auth",
                    Role = role,
                    PerformedByUserId = userId,
                    Details = $"User logged out: {userEmail ?? userId ?? "unknown"}"
                };
                _db.AuditLogs.Add(log);
                await _db.SaveChangesAsync();
            }
            catch { }
            if (returnUrl != null)
            {
                return LocalRedirect(returnUrl);
            }
            else
            {
                // This needs to be a redirect so that the browser performs a new
                // request and the identity for the user gets updated.
                return RedirectToPage();
            }
        }
    }
}
