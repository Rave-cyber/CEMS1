using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using CEMS.Models;

namespace CEMS.Controllers
{
    [Authorize(Roles = "SuperAdmin")]
    public class SuperAdminController : Controller
    {
        private readonly Data.ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;

        public SuperAdminController(Data.ApplicationDbContext db, UserManager<IdentityUser> userManager, RoleManager<IdentityRole> roleManager)
        {
            _db = db;
            _userManager = userManager;
            _roleManager = roleManager;
        }

        // ───────────── Dashboard ─────────────
        public async Task<IActionResult> Dashboard()
        {
            ViewBag.TotalUsers = await _userManager.Users.CountAsync();
            ViewBag.TotalRoles = await _roleManager.Roles.CountAsync();

            var recentLogs = await _db.AuditLogs
                .OrderByDescending(l => l.Timestamp)
                .Take(10)
                .ToListAsync();

            var logUserIds = recentLogs
                .Select(l => l.PerformedByUserId)
                .Where(id => id != null)
                .Distinct()
                .ToList();
            var logUsers = await _userManager.Users
                .Where(u => logUserIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.UserName);
            ViewBag.RecentLogs = recentLogs;
            ViewBag.LogUsers = logUsers;

            // Role counts
            var roles = await _roleManager.Roles.ToListAsync();
            var roleCounts = new Dictionary<string, int>();
            foreach (var role in roles)
            {
                var usersInRole = await _userManager.GetUsersInRoleAsync(role.Name!);
                roleCounts[role.Name!] = usersInRole.Count;
            }
            ViewBag.RoleCounts = roleCounts;

            return View("Dashboard/Index");
        }

        // ───────────── User Management ─────────────
        public async Task<IActionResult> Users()
        {
            var allUsers = await _userManager.Users.OrderBy(u => u.Email).ToListAsync();
            var userDtos = new List<UserWithRolesDto>();

            foreach (var user in allUsers)
            {
                var roles = await _userManager.GetRolesAsync(user);
                userDtos.Add(new UserWithRolesDto
                {
                    UserId = user.Id,
                    Email = user.Email ?? "N/A",
                    UserName = user.UserName ?? "N/A",
                    Roles = roles.ToList(),
                    IsLockedOut = user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.UtcNow
                });
            }

            ViewBag.Users = userDtos;
            ViewBag.AllRoles = await _roleManager.Roles.Select(r => r.Name).ToListAsync();

            return View("Users/Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateAccount(string email, string password, string fullName, string role)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(fullName))
            {
                TempData["Error"] = "Email, password, and full name are required.";
                return RedirectToAction("Users");
            }

            var existingUser = await _userManager.FindByEmailAsync(email);
            if (existingUser != null)
            {
                TempData["Error"] = $"A user with email '{email}' already exists.";
                return RedirectToAction("Users");
            }

            var user = new IdentityUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                LockoutEnabled = false
            };

            var result = await _userManager.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                TempData["Error"] = "Failed to create account: " + string.Join(", ", result.Errors.Select(e => e.Description));
                return RedirectToAction("Users");
            }

            if (!string.IsNullOrWhiteSpace(role))
            {
                await _userManager.AddToRoleAsync(user, role);
            }

            var adminId = _userManager.GetUserId(User);

            // Create profile record based on role
            switch (role)
            {
                case "CEO":
                    _db.CEOProfiles.Add(new CEOProfile { UserId = user.Id, FullName = fullName, IsActive = true });
                    break;
                case "Manager":
                    _db.ManagerProfiles.Add(new ManagerProfile { UserId = user.Id, FullName = fullName, Department = "General", IsActive = true, CreatedByUserId = adminId });
                    break;
                case "Finance":
                    _db.FinanceProfiles.Add(new FinanceProfile { UserId = user.Id, FullName = fullName, Department = "Accounting", IsActive = true, CreatedByUserId = adminId });
                    break;
                case "Driver":
                    _db.DriverProfiles.Add(new DriverProfile { UserId = user.Id, FullName = fullName, IsActive = true, CreatedByUserId = adminId });
                    break;
            }

            _db.AuditLogs.Add(new AuditLog
            {
                Action = "CreateAccount",
                PerformedByUserId = adminId,
                TargetUserId = user.Id,
                Details = $"Created account '{email}' with role '{role}'"
            });

            await _db.SaveChangesAsync();
            TempData["Success"] = $"Account '{email}' created successfully with role '{role}'.";
            return RedirectToAction("Users");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignRole(string userId, string role)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound();

            if (!await _roleManager.RoleExistsAsync(role))
            {
                TempData["Error"] = $"Role '{role}' does not exist.";
                return RedirectToAction("Users");
            }

            if (await _userManager.IsInRoleAsync(user, role))
            {
                TempData["Error"] = $"User '{user.Email}' already has role '{role}'.";
                return RedirectToAction("Users");
            }

            await _userManager.AddToRoleAsync(user, role);

            _db.AuditLogs.Add(new AuditLog
            {
                Action = "AssignRole",
                PerformedByUserId = _userManager.GetUserId(User),
                TargetUserId = userId,
                Details = $"Assigned role '{role}' to '{user.Email}'"
            });
            await _db.SaveChangesAsync();

            TempData["Success"] = $"Role '{role}' assigned to '{user.Email}'.";
            return RedirectToAction("Users");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveRole(string userId, string role)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound();

            if (!await _userManager.IsInRoleAsync(user, role))
            {
                TempData["Error"] = $"User '{user.Email}' does not have role '{role}'.";
                return RedirectToAction("Users");
            }

            await _userManager.RemoveFromRoleAsync(user, role);

            _db.AuditLogs.Add(new AuditLog
            {
                Action = "RemoveRole",
                PerformedByUserId = _userManager.GetUserId(User),
                TargetUserId = userId,
                Details = $"Removed role '{role}' from '{user.Email}'"
            });
            await _db.SaveChangesAsync();

            TempData["Success"] = $"Role '{role}' removed from '{user.Email}'.";
            return RedirectToAction("Users");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleLockout(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound();

            var currentUserId = _userManager.GetUserId(User);
            if (user.Id == currentUserId)
            {
                TempData["Error"] = "You cannot lock out your own account.";
                return RedirectToAction("Users");
            }

            bool isLockedOut = user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.UtcNow;
            if (isLockedOut)
            {
                user.LockoutEnd = null;
                user.LockoutEnabled = false;
            }
            else
            {
                user.LockoutEnd = DateTimeOffset.MaxValue;
                user.LockoutEnabled = true;
            }

            await _userManager.UpdateAsync(user);

            _db.AuditLogs.Add(new AuditLog
            {
                Action = isLockedOut ? "UnlockAccount" : "LockAccount",
                PerformedByUserId = currentUserId,
                TargetUserId = userId,
                Details = $"{(isLockedOut ? "Unlocked" : "Locked")} account '{user.Email}'"
            });
            await _db.SaveChangesAsync();

            TempData["Success"] = $"Account '{user.Email}' has been {(isLockedOut ? "unlocked" : "locked")}.";
            return RedirectToAction("Users");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAccount(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound();

            var currentUserId = _userManager.GetUserId(User);
            if (user.Id == currentUserId)
            {
                TempData["Error"] = "You cannot delete your own account.";
                return RedirectToAction("Users");
            }

            var email = user.Email;
            await _userManager.DeleteAsync(user);

            _db.AuditLogs.Add(new AuditLog
            {
                Action = "DeleteAccount",
                PerformedByUserId = currentUserId,
                TargetUserId = userId,
                Details = $"Deleted account '{email}'"
            });
            await _db.SaveChangesAsync();

            TempData["Success"] = $"Account '{email}' has been deleted.";
            return RedirectToAction("Users");
        }

        // ───────────── Audit Logs ─────────────
        public async Task<IActionResult> AuditLogs(string? action, DateTime? start, DateTime? end)
        {
            var q = _db.AuditLogs.AsQueryable();

            if (!string.IsNullOrEmpty(action))
                q = q.Where(l => l.Action == action);
            if (start.HasValue)
                q = q.Where(l => l.Timestamp >= start.Value);
            if (end.HasValue)
                q = q.Where(l => l.Timestamp <= end.Value);

            var logs = await q.OrderByDescending(l => l.Timestamp).Take(200).ToListAsync();

            var userIds = logs
                .SelectMany(l => new[] { l.PerformedByUserId, l.TargetUserId })
                .Where(id => id != null)
                .Distinct()
                .ToList();
            var users = await _userManager.Users
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.UserName);

            ViewBag.Logs = logs;
            ViewBag.LogUsers = users;
            ViewBag.FilterAction = action;
            ViewBag.FilterStart = start;
            ViewBag.FilterEnd = end;

            var actions = await _db.AuditLogs.Select(l => l.Action).Distinct().ToListAsync();
            ViewBag.Actions = actions;

            return View("AuditLogs/Index");
        }

        public IActionResult Index()
        {
            return RedirectToAction("Dashboard");
        }
    }

    // DTO for user listing
    public class UserWithRolesDto
    {
        public string UserId { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string UserName { get; set; } = null!;
        public List<string> Roles { get; set; } = [];
        public bool IsLockedOut { get; set; }
    }
}
