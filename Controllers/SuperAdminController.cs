using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using CEMS.Models;
using CEMS.Services;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
using System.Text;

namespace CEMS.Controllers
{
    [Authorize(Roles = "SuperAdmin")]
    public class SuperAdminController : Controller
    {
        private readonly Data.ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IDatabaseBackupService _backupService;
        private readonly ISecurityThreatDetector _threatDetector;

        public SuperAdminController(
            Data.ApplicationDbContext db,
            UserManager<IdentityUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IDatabaseBackupService backupService,
            ISecurityThreatDetector threatDetector)
        {
            _db = db;
            _userManager = userManager;
            _roleManager = roleManager;
            _backupService = backupService;
            _threatDetector = threatDetector;
        }

        // ───────────── Dashboard ─────────────
        public async Task<IActionResult> Dashboard(DateTime? start, DateTime? end)
        {
            // Set default date range if not provided (first of current month to today)
            if (!start.HasValue) start = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
            if (!end.HasValue) end = DateTime.UtcNow.Date;

            ViewBag.FilterStart = start?.ToString("yyyy-MM-dd");
            ViewBag.FilterEnd = end?.ToString("yyyy-MM-dd");

            ViewBag.TotalUsers = await _userManager.Users.CountAsync();
            ViewBag.TotalRoles = await _roleManager.Roles.CountAsync();

            // Recent logs within selected range
            var recentLogs = await _db.AuditLogs
                .Where(l => l.Timestamp >= start.Value.Date && l.Timestamp <= end.Value.Date.AddDays(1))
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

            // Expense report stats for dashboard charts (respect selected date range)
            var totalExpenseReports = await _db.ExpenseReports
                .Where(r => r.SubmissionDate >= start.Value.Date && r.SubmissionDate < end.Value.Date.AddDays(1))
                .CountAsync();
            ViewBag.TotalExpenseReports = totalExpenseReports;

            var expenseStatusCounts = await _db.ExpenseReports
                .Where(r => r.SubmissionDate >= start.Value.Date && r.SubmissionDate < end.Value.Date.AddDays(1))
                .GroupBy(e => e.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();
            ViewBag.ExpenseStatusCounts = expenseStatusCounts.ToDictionary(x => x.Status.ToString(), x => x.Count);

            // Monthly audit activity for the selected date range
            var monthlyAuditData = await _db.AuditLogs
                .Where(l => l.Timestamp >= start.Value.Date && l.Timestamp < end.Value.Date.AddDays(1))
                .GroupBy(l => new { l.Timestamp.Year, l.Timestamp.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
                .OrderBy(g => g.Year).ThenBy(g => g.Month)
                .ToListAsync();
            ViewBag.MonthlyAuditLabels = monthlyAuditData.Select(m => new DateTime(m.Year, m.Month, 1).ToString("MMM yyyy")).ToList();
            ViewBag.MonthlyAuditCounts = monthlyAuditData.Select(m => m.Count).ToList();

            // Active vs locked users (within date range does not apply; keep total status)
            var allUsersForStats = await _userManager.Users.ToListAsync();
            var lockedCount = allUsersForStats.Count(u => u.LockoutEnd.HasValue && u.LockoutEnd > DateTimeOffset.UtcNow);
            ViewBag.ActiveUsers = allUsersForStats.Count - lockedCount;
            ViewBag.LockedUsers = lockedCount;

            // Security threat summary
            var threatSummary = await _threatDetector.GetThreatSummaryAsync();
            ViewBag.ThreatSummary = threatSummary;

            return View("Dashboard/Index");
        }

        // ───────────── User Management ─────────────
        public async Task<IActionResult> Users(string? q, string? role, string? status, int page = 1, int pageSize = 10)
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

            // Apply filters
            if (!string.IsNullOrWhiteSpace(q))
            {
                userDtos = userDtos
                    .Where(u => u.Email.Contains(q, StringComparison.OrdinalIgnoreCase) || 
                                u.UserName.Contains(q, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (!string.IsNullOrWhiteSpace(role))
            {
                userDtos = userDtos
                    .Where(u => u.Roles.Contains(role, StringComparer.OrdinalIgnoreCase))
                    .ToList();
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                if (status.Equals("active", StringComparison.OrdinalIgnoreCase))
                {
                    userDtos = userDtos.Where(u => !u.IsLockedOut).ToList();
                }
                else if (status.Equals("inactive", StringComparison.OrdinalIgnoreCase))
                {
                    userDtos = userDtos.Where(u => u.IsLockedOut).ToList();
                }
            }

            // Pagination
            int totalCount = userDtos.Count;
            int totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            page = Math.Max(1, Math.Min(page, Math.Max(1, totalPages)));

            var paginatedUsers = userDtos
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            ViewBag.Users = paginatedUsers;
            ViewBag.AllRoles = await _roleManager.Roles.Select(r => r.Name).ToListAsync();
            ViewBag.FilterQuery = q;
            ViewBag.FilterRole = role;
            ViewBag.FilterStatus = status;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalCount = totalCount;
            ViewBag.PageSize = pageSize;

            return View("Users/Index");
        }

        // ───────────── Export Users as PDF ─────────────
        public async Task<IActionResult> ExportUsersPdf(string q, string role, string status)
        {
            var usersList = await _userManager.Users.OrderBy(u => u.Email).ToListAsync();
            var userDtos = new List<UserWithRolesDto>();

            foreach (var user in usersList)
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

            // Apply search filter
            if (!string.IsNullOrWhiteSpace(q))
            {
                var searchLower = q.ToLower();
                userDtos = userDtos.Where(u => 
                    u.Email.ToLower().Contains(searchLower) || 
                    u.UserName.ToLower().Contains(searchLower)).ToList();
            }

            // Apply role filter
            if (!string.IsNullOrWhiteSpace(role))
            {
                userDtos = userDtos.Where(u => u.Roles.Contains(role, StringComparer.OrdinalIgnoreCase)).ToList();
            }

            // Apply status filter
            if (!string.IsNullOrWhiteSpace(status))
            {
                if (status.Equals("active", StringComparison.OrdinalIgnoreCase))
                {
                    userDtos = userDtos.Where(u => !u.IsLockedOut).ToList();
                }
                else if (status.Equals("inactive", StringComparison.OrdinalIgnoreCase))
                {
                    userDtos = userDtos.Where(u => u.IsLockedOut).ToList();
                }
            }

            // Create PDF
            using (var memoryStream = new MemoryStream())
            {
                var writer = new PdfWriter(memoryStream);
                var pdf = new PdfDocument(writer);
                var document = new Document(pdf);

                // Title
                var title = new Paragraph("User Management Report")
                    .SetFontSize(18)
                    .SetBold()
                    .SetMarginBottom(10);
                document.Add(title);

                // Report info
                var reportInfo = new Paragraph()
                    .Add($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n")
                    .Add($"Total Records: {userDtos.Count}\n")
                    .SetFontSize(10)
                    .SetMarginBottom(15);
                if (!string.IsNullOrEmpty(q))
                    reportInfo.Add($"Search: {q}\n");
                if (!string.IsNullOrEmpty(role))
                    reportInfo.Add($"Role Filter: {role}\n");
                if (!string.IsNullOrEmpty(status))
                    reportInfo.Add($"Status Filter: {status}\n");
                document.Add(reportInfo);

                // Table
                var table = new Table(UnitValue.CreatePercentArray(new[] { 25f, 20f, 25f, 15f, 15f }));
                table.SetWidth(UnitValue.CreatePercentValue(100));

                // Table headers
                var headers = new[] { "Email", "Username", "Roles", "Status", "Lock Status" };
                foreach (var header in headers)
                {
                    var cell = new Cell()
                        .Add(new Paragraph(header).SetBold())
                        .SetBackgroundColor(new iText.Kernel.Colors.DeviceRgb(240, 243, 247))
                        .SetPadding(8);
                    table.AddHeaderCell(cell);
                }

                // Table rows
                foreach (var user in userDtos)
                {
                    var rolesText = user.Roles.Count > 0 ? string.Join(", ", user.Roles) : "No Role";
                    var statusText = user.IsLockedOut ? "Locked" : "Active";
                    var lockStatus = user.IsLockedOut ? "Yes" : "No";

                    table.AddCell(new Cell().Add(new Paragraph(user.Email).SetFontSize(9)));
                    table.AddCell(new Cell().Add(new Paragraph(user.UserName).SetFontSize(9)));
                    table.AddCell(new Cell().Add(new Paragraph(rolesText).SetFontSize(9)));
                    table.AddCell(new Cell().Add(new Paragraph(statusText).SetFontSize(9)));
                    table.AddCell(new Cell().Add(new Paragraph(lockStatus).SetFontSize(9)));
                }

                document.Add(table);
                document.Close();

                var bytes = memoryStream.ToArray();
                return File(bytes, "application/pdf", $"UserManagement_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateAccount(string email, string password, string fullName, string role,
            string? street, string? barangay, string? city, string? province, string? zipCode, string? country, string? contactNumber)
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

            // Normalize address fields - empty string becomes null
            var normalizedStreet = string.IsNullOrWhiteSpace(street) ? null : street.Trim();
            var normalizedBarangay = string.IsNullOrWhiteSpace(barangay) ? null : barangay.Trim();
            var normalizedCity = string.IsNullOrWhiteSpace(city) ? null : city.Trim();
            var normalizedProvince = string.IsNullOrWhiteSpace(province) ? null : province.Trim();
            var normalizedZipCode = string.IsNullOrWhiteSpace(zipCode) ? null : zipCode.Trim();
            var normalizedCountry = string.IsNullOrWhiteSpace(country) ? null : country.Trim();
            var normalizedContactNumber = string.IsNullOrWhiteSpace(contactNumber) ? null : contactNumber.Trim();

            // Create profile record based on role
            switch (role)
            {
                case "CEO":
                    _db.CEOProfiles.Add(new CEOProfile 
                    { 
                        UserId = user.Id, 
                        FullName = fullName?.Trim(), 
                        Street = normalizedStreet, 
                        Barangay = normalizedBarangay, 
                        City = normalizedCity, 
                        Province = normalizedProvince, 
                        ZipCode = normalizedZipCode, 
                        Country = normalizedCountry, 
                        ContactNumber = normalizedContactNumber, 
                        IsActive = true 
                    });
                    break;
                case "Manager":
                    _db.ManagerProfiles.Add(new ManagerProfile 
                    { 
                        UserId = user.Id, 
                        FullName = fullName?.Trim(), 
                        Department = "General", 
                        Street = normalizedStreet, 
                        Barangay = normalizedBarangay, 
                        City = normalizedCity, 
                        Province = normalizedProvince, 
                        ZipCode = normalizedZipCode, 
                        Country = normalizedCountry, 
                        ContactNumber = normalizedContactNumber, 
                        IsActive = true, 
                        CreatedByUserId = adminId 
                    });
                    break;
                case "Finance":
                    _db.FinanceProfiles.Add(new FinanceProfile 
                    { 
                        UserId = user.Id, 
                        FullName = fullName?.Trim(), 
                        Department = "Accounting", 
                        Street = normalizedStreet, 
                        Barangay = normalizedBarangay, 
                        City = normalizedCity, 
                        Province = normalizedProvince, 
                        ZipCode = normalizedZipCode, 
                        Country = normalizedCountry, 
                        ContactNumber = normalizedContactNumber, 
                        IsActive = true, 
                        CreatedByUserId = adminId 
                    });
                    break;
                case "Driver":
                    _db.DriverProfiles.Add(new DriverProfile 
                    { 
                        UserId = user.Id, 
                        FullName = fullName?.Trim(), 
                        Street = normalizedStreet, 
                        Barangay = normalizedBarangay, 
                        City = normalizedCity, 
                        Province = normalizedProvince, 
                        ZipCode = normalizedZipCode, 
                        Country = normalizedCountry, 
                        ContactNumber = normalizedContactNumber, 
                        IsActive = true, 
                        CreatedByUserId = adminId 
                    });
                    break;
                default:
                    // If no role is selected, we still create a basic user but no profile
                    break;
            }

            _db.AuditLogs.Add(new AuditLog
            {
                Action = "CreateAccount",
                Module = "User Management",
                Role = "SuperAdmin",
                PerformedByUserId = adminId,
                TargetUserId = user.Id,
                Details = $"Created account '{email}' with role '{role}'"
            });

            await _db.SaveChangesAsync();
            TempData["Success"] = $"Account '{email}' created successfully with role '{role}'.";
            return RedirectToAction("Users");
        }

        // GET: Edit user form
        public async Task<IActionResult> EditUser(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId)) return BadRequest();
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound();

            // Try to find a profile for this user (CEO/Manager/Finance/Driver)
            var ceo = await _db.CEOProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
            var manager = await _db.ManagerProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
            var finance = await _db.FinanceProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
            var driver = await _db.DriverProfiles.FirstOrDefaultAsync(p => p.UserId == userId);

            var vm = new EditUserViewModel
            {
                UserId = user.Id,
                Email = user.Email,
                Roles = (await _userManager.GetRolesAsync(user)).ToList(),
                FullName = ceo?.FullName ?? manager?.FullName ?? finance?.FullName ?? driver?.FullName,
                Street = ceo?.Street ?? manager?.Street ?? finance?.Street ?? driver?.Street,
                Barangay = ceo?.Barangay ?? manager?.Barangay ?? finance?.Barangay ?? driver?.Barangay,
                City = ceo?.City ?? manager?.City ?? finance?.City ?? driver?.City,
                Province = ceo?.Province ?? manager?.Province ?? finance?.Province ?? driver?.Province,
                ZipCode = ceo?.ZipCode ?? manager?.ZipCode ?? finance?.ZipCode ?? driver?.ZipCode,
                Country = ceo?.Country ?? manager?.Country ?? finance?.Country ?? driver?.Country,
                ContactNumber = ceo?.ContactNumber ?? manager?.ContactNumber ?? finance?.ContactNumber ?? driver?.ContactNumber
            };

            return PartialView("Users/Edit", vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditUser(EditUserViewModel model)
        {
            if (!ModelState.IsValid) return PartialView("Users/Edit", model);

            var user = await _userManager.FindByIdAsync(model.UserId);
            if (user == null) return NotFound();

            // Update email if changed
            if (!string.Equals(user.Email, model.Email, StringComparison.OrdinalIgnoreCase))
            {
                user.Email = model.Email;
                user.UserName = model.Email;
                await _userManager.UpdateAsync(user);
            }

            // Update profile fields for whichever profile exists
            var ceo = await _db.CEOProfiles.FirstOrDefaultAsync(p => p.UserId == model.UserId);
            var manager = await _db.ManagerProfiles.FirstOrDefaultAsync(p => p.UserId == model.UserId);
            var finance = await _db.FinanceProfiles.FirstOrDefaultAsync(p => p.UserId == model.UserId);
            var driver = await _db.DriverProfiles.FirstOrDefaultAsync(p => p.UserId == model.UserId);

            if (ceo != null)
            {
                ceo.FullName = model.FullName;
                ceo.Street = model.Street;
                ceo.Barangay = model.Barangay;
                ceo.City = model.City;
                ceo.Province = model.Province;
                ceo.ZipCode = model.ZipCode;
                ceo.Country = model.Country;
                ceo.ContactNumber = model.ContactNumber;
            }
            if (manager != null)
            {
                manager.FullName = model.FullName;
                manager.Street = model.Street;
                manager.Barangay = model.Barangay;
                manager.City = model.City;
                manager.Province = model.Province;
                manager.ZipCode = model.ZipCode;
                manager.Country = model.Country;
                manager.ContactNumber = model.ContactNumber;
            }
            if (finance != null)
            {
                finance.FullName = model.FullName;
                finance.Street = model.Street;
                finance.Barangay = model.Barangay;
                finance.City = model.City;
                finance.Province = model.Province;
                finance.ZipCode = model.ZipCode;
                finance.Country = model.Country;
                finance.ContactNumber = model.ContactNumber;
            }
            if (driver != null)
            {
                driver.FullName = model.FullName;
                driver.Street = model.Street;
                driver.Barangay = model.Barangay;
                driver.City = model.City;
                driver.Province = model.Province;
                driver.ZipCode = model.ZipCode;
                driver.Country = model.Country;
                driver.ContactNumber = model.ContactNumber;
            }

            await _db.SaveChangesAsync();

            // Handle password change if provided
            if (!string.IsNullOrWhiteSpace(model.Password))
            {
                var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
                var resetResult = await _userManager.ResetPasswordAsync(user, resetToken, model.Password!);
                if (!resetResult.Succeeded)
                {
                    // Return to the edit view with errors
                    foreach (var err in resetResult.Errors)
                    {
                        ModelState.AddModelError(string.Empty, err.Description);
                    }
                    return PartialView("Users/Edit", model);
                }
            }

            await _db.SaveChangesAsync();

            TempData["Success"] = "User updated successfully.";
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
                Module = "User Management",
                Role = "SuperAdmin",
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
                Module = "User Management",
                Role = "SuperAdmin",
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
                Module = "User Management",
                Role = "SuperAdmin",
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
                Module = "User Management",
                Role = "SuperAdmin",
                PerformedByUserId = currentUserId,
                TargetUserId = userId,
                Details = $"Deleted account '{email}'"
            });
            await _db.SaveChangesAsync();

            TempData["Success"] = $"Account '{email}' has been deleted.";
            return RedirectToAction("Users");
        }

        // ───────────── Audit Logs ─────────────
        public async Task<IActionResult> AuditLogs(string? actionType, string? module, string? role, string? user, DateTime? start, DateTime? end, int page = 1, int pageSize = 10)
        {
            var q = _db.AuditLogs.AsQueryable();
            if (!string.IsNullOrEmpty(actionType))
                q = q.Where(l => l.Action == actionType);
            if (!string.IsNullOrEmpty(module))
                q = q.Where(l => l.Module == module);
            if (!string.IsNullOrEmpty(role))
                q = q.Where(l => l.Role == role);
            if (start.HasValue)
                q = q.Where(l => l.Timestamp >= start.Value.Date);
            if (end.HasValue)
                q = q.Where(l => l.Timestamp < end.Value.Date.AddDays(1));

            // User search — resolve matching user IDs then filter
            if (!string.IsNullOrWhiteSpace(user))
            {
                var matchingUserIds = await _userManager.Users
                    .Where(u => u.Email!.Contains(user) || u.UserName!.Contains(user))
                    .Select(u => u.Id)
                    .ToListAsync();
                q = q.Where(l => matchingUserIds.Contains(l.PerformedByUserId));
            }

            var totalCount = await q.CountAsync();
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            page = Math.Max(1, Math.Min(page, Math.Max(1, totalPages)));

            var logs = await q
                .OrderByDescending(l => l.Timestamp)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // If no logs found and no filters provided, show recent logs as a friendly fallback
            var noFilters = string.IsNullOrWhiteSpace(actionType) && string.IsNullOrWhiteSpace(module) && string.IsNullOrWhiteSpace(role) && string.IsNullOrWhiteSpace(user) && !start.HasValue && !end.HasValue;
            if (logs.Count == 0 && noFilters)
            {
                logs = await _db.AuditLogs.OrderByDescending(l => l.Timestamp).Take(50).ToListAsync();
                totalCount = logs.Count;
                totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
                ViewBag.CurrentPage = 1;
            }

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
            ViewBag.FilterAction = actionType;
            ViewBag.FilterModule = module;
            ViewBag.FilterRole = role;
            ViewBag.FilterUser = user;
            ViewBag.FilterStart = start;
            ViewBag.FilterEnd = end;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalCount = totalCount;

            ViewBag.Actions = await _db.AuditLogs.Select(l => l.Action).Distinct().OrderBy(a => a).ToListAsync();
            ViewBag.Modules = await _db.AuditLogs.Where(l => l.Module != null).Select(l => l.Module!).Distinct().OrderBy(m => m).ToListAsync();
            ViewBag.Roles = new List<string> { "SuperAdmin", "CEO", "Manager", "Finance", "Driver" };

            return View("AuditLogs/Index");
        }

        // ───────────── Export Audit Logs as PDF ─────────────
        public async Task<IActionResult> ExportAuditLogsPdf(string? actionType, string? module, string? role, string? user, DateTime? start, DateTime? end)
        {
            var q = _db.AuditLogs.AsQueryable();
            if (!string.IsNullOrEmpty(actionType))
                q = q.Where(l => l.Action == actionType);
            if (!string.IsNullOrEmpty(module))
                q = q.Where(l => l.Module == module);
            if (!string.IsNullOrEmpty(role))
                q = q.Where(l => l.Role == role);
            if (start.HasValue)
                q = q.Where(l => l.Timestamp >= start.Value.Date);
            if (end.HasValue)
                q = q.Where(l => l.Timestamp < end.Value.Date.AddDays(1));

            // User search — resolve matching user IDs then filter
            if (!string.IsNullOrWhiteSpace(user))
            {
                var matchingUserIds = await _userManager.Users
                    .Where(u => u.Email!.Contains(user) || u.UserName!.Contains(user))
                    .Select(u => u.Id)
                    .ToListAsync();
                q = q.Where(l => matchingUserIds.Contains(l.PerformedByUserId));
            }

            var logs = await q.OrderByDescending(l => l.Timestamp).ToListAsync();

            var userIds = logs
                .SelectMany(l => new[] { l.PerformedByUserId, l.TargetUserId })
                .Where(id => id != null)
                .Distinct()
                .ToList();
            var users = await _userManager.Users
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.UserName);

            // Create PDF
            using (var memoryStream = new MemoryStream())
            {
                var writer = new PdfWriter(memoryStream);
                var pdf = new PdfDocument(writer);
                var pageSize = iText.Kernel.Geom.PageSize.A4.Rotate();
                var document = new Document(pdf, pageSize);
                document.SetMargins(20, 20, 20, 20);

                // Title
                var title = new Paragraph("Audit Logs Report")
                    .SetFontSize(18)
                    .SetBold()
                    .SetMarginBottom(10);
                document.Add(title);

                // Report info
                var reportInfo = new Paragraph()
                    .Add($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n")
                    .Add($"Total Records: {logs.Count}\n")
                    .SetFontSize(10)
                    .SetMarginBottom(15);
                if (!string.IsNullOrEmpty(actionType))
                    reportInfo.Add($"Action Filter: {actionType}\n");
                if (!string.IsNullOrEmpty(module))
                    reportInfo.Add($"Module Filter: {module}\n");
                if (!string.IsNullOrEmpty(role))
                    reportInfo.Add($"Role Filter: {role}\n");
                if (start.HasValue)
                    reportInfo.Add($"Date From: {start:yyyy-MM-dd}\n");
                if (end.HasValue)
                    reportInfo.Add($"Date To: {end:yyyy-MM-dd}\n");
                document.Add(reportInfo);

                // Table
                var table = new Table(UnitValue.CreatePercentArray(new[] { 15f, 12f, 15f, 12f, 20f, 26f }));
                table.SetWidth(UnitValue.CreatePercentValue(100));

                // Table headers
                var headers = new[] { "Timestamp", "Action", "Module", "Role", "User", "Details" };
                foreach (var header in headers)
                {
                    var cell = new Cell()
                        .Add(new Paragraph(header).SetBold())
                        .SetBackgroundColor(new iText.Kernel.Colors.DeviceRgb(240, 243, 247))
                        .SetPadding(8);
                    table.AddHeaderCell(cell);
                }

                // Table rows
                foreach (var log in logs)
                {
                    var userNamePerformed = log.PerformedByUserId != null && users.ContainsKey(log.PerformedByUserId)
                        ? users[log.PerformedByUserId] ?? "Unknown"
                        : "System";

                    table.AddCell(new Cell().Add(new Paragraph(log.Timestamp.ToString("yyyy-MM-dd HH:mm")).SetFontSize(9)));
                    table.AddCell(new Cell().Add(new Paragraph(log.Action).SetFontSize(9)));
                    table.AddCell(new Cell().Add(new Paragraph(log.Module ?? "-").SetFontSize(9)));
                    table.AddCell(new Cell().Add(new Paragraph(log.Role ?? "-").SetFontSize(9)));
                    table.AddCell(new Cell().Add(new Paragraph(userNamePerformed).SetFontSize(9)));
                    table.AddCell(new Cell().Add(new Paragraph(log.Details ?? "-").SetFontSize(9)));
                }

                document.Add(table);
                document.Close();

                var bytes = memoryStream.ToArray();
                return File(bytes, "application/pdf", $"AuditLogs_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
            }
        }

        public IActionResult Index()
        {
            return RedirectToAction("Dashboard");
        }

        // ───────────── Fuel Price Management ─────────────
        public async Task<IActionResult> FuelPrices()
        {
            var prices = await _db.FuelPrices.OrderBy(f => f.Name).ToListAsync();
            ViewBag.FuelPrices = prices;
            return View("FuelPrices/Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateFuelPrice(string name, string description, decimal price, string unit, string icon, string cssClass)
        {
            if (string.IsNullOrWhiteSpace(name) || price <= 0)
            {
                TempData["Error"] = "Fuel name and a valid price are required.";
                return RedirectToAction("FuelPrices");
            }

            var fuelPrice = new FuelPrice
            {
                Name = name.Trim(),
                Description = (description ?? "").Trim(),
                Price = price,
                Unit = string.IsNullOrWhiteSpace(unit) ? "/L" : unit.Trim(),
                Icon = string.IsNullOrWhiteSpace(icon) ? "bi-droplet-fill" : icon.Trim(),
                CssClass = string.IsNullOrWhiteSpace(cssClass) ? "gasoline" : cssClass.Trim(),
                UpdatedAt = DateTime.UtcNow,
                UpdatedByUserId = _userManager.GetUserId(User)
            };

            _db.FuelPrices.Add(fuelPrice);

            _db.AuditLogs.Add(new AuditLog
            {
                Action = "CreateFuelPrice",
                Module = "Fuel Prices",
                Role = "SuperAdmin",
                PerformedByUserId = _userManager.GetUserId(User),
                Details = $"Added fuel type '{name}' at ₱{price:N2}"
            });

            await _db.SaveChangesAsync();
            TempData["Success"] = $"Fuel type '{name}' added at ₱{price:N2}.";
            return RedirectToAction("FuelPrices");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateFuelPrice(int id, string name, string description, decimal price, string unit, string icon, string cssClass)
        {
            var fuelPrice = await _db.FuelPrices.FindAsync(id);
            if (fuelPrice == null) return NotFound();

            if (string.IsNullOrWhiteSpace(name) || price <= 0)
            {
                TempData["Error"] = "Fuel name and a valid price are required.";
                return RedirectToAction("FuelPrices");
            }

            var oldPrice = fuelPrice.Price;
            fuelPrice.Name = name.Trim();
            fuelPrice.Description = (description ?? "").Trim();
            fuelPrice.Price = price;
            fuelPrice.Unit = string.IsNullOrWhiteSpace(unit) ? "/L" : unit.Trim();
            fuelPrice.Icon = string.IsNullOrWhiteSpace(icon) ? "bi-droplet-fill" : icon.Trim();
            fuelPrice.CssClass = string.IsNullOrWhiteSpace(cssClass) ? "gasoline" : cssClass.Trim();
            fuelPrice.UpdatedAt = DateTime.UtcNow;
            fuelPrice.UpdatedByUserId = _userManager.GetUserId(User);

            _db.AuditLogs.Add(new AuditLog
            {
                Action = "UpdateFuelPrice",
                Module = "Fuel Prices",
                Role = "SuperAdmin",
                PerformedByUserId = _userManager.GetUserId(User),
                Details = $"Updated '{name}' price from ₱{oldPrice:N2} to ₱{price:N2}"
            });

            await _db.SaveChangesAsync();
            TempData["Success"] = $"Fuel type '{name}' updated to ₱{price:N2}.";
            return RedirectToAction("FuelPrices");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteFuelPrice(int id)
        {
            var fuelPrice = await _db.FuelPrices.FindAsync(id);
            if (fuelPrice == null) return NotFound();

            var name = fuelPrice.Name;
            _db.FuelPrices.Remove(fuelPrice);

            _db.AuditLogs.Add(new AuditLog
            {
                Action = "DeleteFuelPrice",
                Module = "Fuel Prices",
                Role = "SuperAdmin",
                PerformedByUserId = _userManager.GetUserId(User),
                Details = $"Deleted fuel type '{name}'"
            });

            await _db.SaveChangesAsync();
            TempData["Success"] = $"Fuel type '{name}' has been deleted.";
            return RedirectToAction("FuelPrices");
        }

        // ───────────── Database Backup & Recovery ─────────────
        public async Task<IActionResult> BackupRecovery()
        {
            var backupHistory = await _backupService.GetBackupHistoryAsync();
            ViewBag.BackupHistory = backupHistory;
            return View("Backup/Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateBackup()
        {
            try
            {
                var backupData = await _backupService.CreateFullBackupAsync();
                var fileName = $"CEMS_Backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}.zip";
                
                // Log backup creation
                _db.AuditLogs.Add(new AuditLog
                {
                    Action = "CreateBackup",
                    Module = "Database",
                    Role = "SuperAdmin",
                    PerformedByUserId = _userManager.GetUserId(User),
                    Details = $"Created database backup: {fileName} ({backupData.Length / 1024 / 1024} MB)"
                });
                await _db.SaveChangesAsync();

                TempData["Success"] = $"✅ Backup created successfully! File size: {backupData.Length / 1024 / 1024} MB";
                return File(backupData, "application/zip", fileName);
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"❌ Backup failed: {ex.Message}";
                return RedirectToAction("BackupRecovery");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreBackup(IFormFile backupFile)
        {
            if (backupFile == null || backupFile.Length == 0)
            {
                TempData["Error"] = "Please select a backup file to restore.";
                return RedirectToAction("BackupRecovery");
            }

            if (!backupFile.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "Only ZIP files are supported.";
                return RedirectToAction("BackupRecovery");
            }

            try
            {
                using (var stream = backupFile.OpenReadStream())
                {
                    var (success, message) = await _backupService.RestoreBackupAsync(stream);
                    
                    if (success)
                    {
                        _db.AuditLogs.Add(new AuditLog
                        {
                            Action = "RestoreBackup",
                            Module = "Database",
                            Role = "SuperAdmin",
                            PerformedByUserId = _userManager.GetUserId(User),
                            Details = $"Restored database from backup: {backupFile.FileName} ({backupFile.Length / 1024 / 1024} MB)"
                        });
                        await _db.SaveChangesAsync();

                        TempData["Success"] = $"✅ {message}";
                    }
                    else
                    {
                        TempData["Error"] = $"❌ {message}";
                    }
                }
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"❌ Restore failed: {ex.Message}";
            }

            return RedirectToAction("BackupRecovery");
        }
    }

    // DTO for user listing
    public class UserWithRolesDto
    {
        public string UserId { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string UserName { get; set; } = null!;
        public List<string> Roles { get; set; } = new List<string>();
        public bool IsLockedOut { get; set; }
    }
}
