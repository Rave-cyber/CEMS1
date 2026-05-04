using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using CEMS.Data;
using CEMS.Models;

namespace CEMS.Areas.Identity.Pages.Account.Manage
{
    public class ProfileModel : PageModel
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ApplicationDbContext _db;

        public ProfileModel(UserManager<IdentityUser> userManager, ApplicationDbContext db)
        {
            _userManager = userManager;
            _db = db;
        }

        [TempData]
        public string? StatusMessage { get; set; }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public string? UserEmail { get; set; }
        public string? UserRole { get; set; }
        public string? ProfileImagePath { get; set; }

        public class InputModel
        {
            [Display(Name = "Full Name")]
            [MaxLength(100)]
            public string? FullName { get; set; }

            [Display(Name = "Contact Number")]
            [MaxLength(20)]
            [RegularExpression(@"^[\d\s\+\-\(\)]{7,20}$",
                ErrorMessage = "Enter a valid phone number (7–20 digits).")]
            public string? ContactNumber { get; set; }
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound();

            UserEmail = user.Email;
            var roles = await _userManager.GetRolesAsync(user);
            UserRole = roles.FirstOrDefault() ?? "User";

            var (fullName, contact, imagePath) = await GetProfileDataAsync(user.Id, UserRole);
            Input.FullName = fullName;
            Input.ContactNumber = contact;
            ProfileImagePath = imagePath;

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound();

            UserEmail = user.Email;
            var roles = await _userManager.GetRolesAsync(user);
            UserRole = roles.FirstOrDefault() ?? "User";

            if (!ModelState.IsValid)
            {
                var (fn, ct, ip) = await GetProfileDataAsync(user.Id, UserRole);
                ProfileImagePath = ip;
                return Page();
            }

            await UpdateProfileAsync(user.Id, UserRole, Input.FullName, Input.ContactNumber);
            await _db.SaveChangesAsync();

            _db.AuditLogs.Add(new AuditLog
            {
                Action = "UpdateProfile",
                Module = "Profile",
                Role = UserRole,
                PerformedByUserId = user.Id,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                Details = "User updated profile from Security Settings."
            });
            await _db.SaveChangesAsync();

            StatusMessage = "Your profile has been updated.";
            return RedirectToPage();
        }

        // ── Helpers ─────────────────────────────────────────────────
        private async Task<(string? FullName, string? Contact, string? ImagePath)> GetProfileDataAsync(
            string userId, string role)
        {
            switch (role)
            {
                case "CEO":
                    var ceo = await _db.CEOProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
                    if (ceo != null) return (ceo.FullName, ceo.ContactNumber, ceo.ProfileImagePath);
                    break;
                case "Manager":
                    var mgr = await _db.ManagerProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
                    if (mgr != null) return (mgr.FullName, mgr.ContactNumber, mgr.ProfileImagePath);
                    break;
                case "Finance":
                    var fin = await _db.FinanceProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
                    if (fin != null) return (fin.FullName, fin.ContactNumber, fin.ProfileImagePath);
                    break;
                case "Driver":
                    var drv = await _db.DriverProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
                    if (drv != null) return (drv.FullName, drv.ContactNumber, drv.ProfileImagePath);
                    break;
                case "SuperAdmin":
                    // SuperAdmin may not have a role-specific profile — use Identity phone number
                    return (null, await _userManager.GetPhoneNumberAsync(
                        (await _userManager.FindByIdAsync(userId))!), null);
            }
            return (null, null, null);
        }

        private async Task UpdateProfileAsync(string userId, string role, string? fullName, string? contact)
        {
            switch (role)
            {
                case "CEO":
                    var ceo = await _db.CEOProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
                    if (ceo != null) { ceo.FullName = fullName ?? ceo.FullName; ceo.ContactNumber = contact; }
                    break;
                case "Manager":
                    var mgr = await _db.ManagerProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
                    if (mgr != null) { mgr.FullName = fullName ?? mgr.FullName; mgr.ContactNumber = contact; }
                    break;
                case "Finance":
                    var fin = await _db.FinanceProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
                    if (fin != null) { fin.FullName = fullName ?? fin.FullName; fin.ContactNumber = contact; }
                    break;
                case "Driver":
                    var drv = await _db.DriverProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
                    if (drv != null) { drv.FullName = fullName ?? drv.FullName; drv.ContactNumber = contact; }
                    break;
                case "SuperAdmin":
                    var su = await _userManager.FindByIdAsync(userId);
                    if (su != null) await _userManager.SetPhoneNumberAsync(su, contact);
                    break;
            }
        }
    }
}
