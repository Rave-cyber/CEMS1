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
        private readonly ILogger<LogoutModel> _logger;

        private readonly ApplicationDbContext _db;

        public LogoutModel(SignInManager<IdentityUser> signInManager, ILogger<LogoutModel> logger, ApplicationDbContext db)
        {
            _signInManager = signInManager;
            _logger = logger;
            _db = db;
        }

        public async Task<IActionResult> OnPost(string returnUrl = null)
        {
            var userId = User?.Identity?.Name;
            await _signInManager.SignOutAsync();
            _logger.LogInformation("User logged out.");

            try
            {
                var log = new CEMS.Models.AuditLog
                {
                    Action = "UserLogout",
                    Module = "Auth",
                    PerformedByUserId = User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
                    Details = "User logged out"
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
