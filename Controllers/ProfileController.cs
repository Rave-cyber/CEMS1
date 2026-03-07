using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CEMS.Data;
using CEMS.Models;

namespace CEMS.Controllers
{
    [Authorize]
    public class ProfileController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IWebHostEnvironment _env;

        public ProfileController(ApplicationDbContext db, UserManager<IdentityUser> userManager, IWebHostEnvironment env)
        {
            _db = db;
            _userManager = userManager;
            _env = env;
        }

        [HttpGet]
        public async Task<IActionResult> GetProfile()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            var roles = await _userManager.GetRolesAsync(user);
            var role = roles.FirstOrDefault() ?? "User";

            var data = await GetProfileData(user.Id, role);

            return Json(new
            {
                email = user.Email,
                role,
                fullName = data.FullName,
                contactNumber = data.Contact,
                street = data.Street,
                barangay = data.Barangay,
                city = data.City,
                province = data.Province,
                zipCode = data.Zip,
                country = data.Country,
                profileImagePath = data.ImagePath
            });
        }

        [HttpGet]
        public async Task<IActionResult> IsAddressConfigured()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            var roles = await _userManager.GetRolesAsync(user);
            var role = roles.FirstOrDefault() ?? "User";

            var data = await GetProfileData(user.Id, role);

            bool isConfigured = !string.IsNullOrWhiteSpace(data.Street) &&
                                !string.IsNullOrWhiteSpace(data.Barangay) &&
                                !string.IsNullOrWhiteSpace(data.City) &&
                                !string.IsNullOrWhiteSpace(data.Province) &&
                                !string.IsNullOrWhiteSpace(data.Zip) &&
                                !string.IsNullOrWhiteSpace(data.Country);

            return Json(new
            {
                isConfigured,
                street = data.Street,
                barangay = data.Barangay,
                city = data.City,
                province = data.Province,
                zipCode = data.Zip,
                country = data.Country
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ValidateAddress(string? street, string? barangay, string? city, string? province, string? zipCode, string? country)
        {
            // Validate that all required fields are present
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(street))
                errors.Add("Street address is required");
            if (string.IsNullOrWhiteSpace(barangay))
                errors.Add("Barangay is required");
            if (string.IsNullOrWhiteSpace(city))
                errors.Add("City is required");
            if (string.IsNullOrWhiteSpace(province))
                errors.Add("Province is required");
            if (string.IsNullOrWhiteSpace(zipCode))
                errors.Add("Zip code is required");
            if (string.IsNullOrWhiteSpace(country))
                errors.Add("Country is required");

            bool isValid = errors.Count == 0;

            return Json(new
            {
                isValid,
                errors,
                message = isValid ? "Address is complete" : "Please fill in all required address fields"
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateProfile(string fullName, string? contactNumber, string? street, string? barangay, string? city, string? province, string? zipCode, string? country)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            var roles = await _userManager.GetRolesAsync(user);
            var role = roles.FirstOrDefault() ?? "User";

            switch (role)
            {
                case "CEO":
                    var ceo = await _db.CEOProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);
                    if (ceo != null) { ceo.FullName = fullName; ceo.ContactNumber = contactNumber; ceo.Street = street; ceo.Barangay = barangay; ceo.City = city; ceo.Province = province; ceo.ZipCode = zipCode; ceo.Country = country; }
                    break;
                case "Manager":
                    var mgr = await _db.ManagerProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);
                    if (mgr != null) { mgr.FullName = fullName; mgr.ContactNumber = contactNumber; mgr.Street = street; mgr.Barangay = barangay; mgr.City = city; mgr.Province = province; mgr.ZipCode = zipCode; mgr.Country = country; }
                    break;
                case "Finance":
                    var fin = await _db.FinanceProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);
                    if (fin != null) { fin.FullName = fullName; fin.ContactNumber = contactNumber; fin.Street = street; fin.Barangay = barangay; fin.City = city; fin.Province = province; fin.ZipCode = zipCode; fin.Country = country; }
                    break;
                case "Driver":
                    var drv = await _db.DriverProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);
                    if (drv != null) { drv.FullName = fullName; drv.ContactNumber = contactNumber; drv.Street = street; drv.Barangay = barangay; drv.City = city; drv.Province = province; drv.ZipCode = zipCode; drv.Country = country; }
                    break;
            }

            await _db.SaveChangesAsync();
            return Json(new { success = true });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadPhoto(IFormFile photo)
        {
            if (photo == null || photo.Length == 0)
                return Json(new { success = false, message = "No file uploaded." });

            var allowed = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
            var ext = Path.GetExtension(photo.FileName).ToLowerInvariant();
            if (!allowed.Contains(ext))
                return Json(new { success = false, message = "Invalid file type. Use JPG, PNG, GIF, or WebP." });

            if (photo.Length > 2 * 1024 * 1024)
                return Json(new { success = false, message = "File too large. Max 2MB." });

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            var roles = await _userManager.GetRolesAsync(user);
            var role = roles.FirstOrDefault() ?? "User";

            var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", "profiles");
            Directory.CreateDirectory(uploadsDir);

            var fileName = $"{user.Id}{ext}";
            var filePath = Path.Combine(uploadsDir, fileName);

            // Delete old files with different extensions
            foreach (var oldExt in allowed)
            {
                var oldFile = Path.Combine(uploadsDir, $"{user.Id}{oldExt}");
                if (System.IO.File.Exists(oldFile) && oldFile != filePath)
                    System.IO.File.Delete(oldFile);
            }

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await photo.CopyToAsync(stream);
            }

            var relativePath = $"/uploads/profiles/{fileName}";

            switch (role)
            {
                case "CEO":
                    var ceo = await _db.CEOProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);
                    if (ceo != null) ceo.ProfileImagePath = relativePath;
                    break;
                case "Manager":
                    var mgr = await _db.ManagerProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);
                    if (mgr != null) mgr.ProfileImagePath = relativePath;
                    break;
                case "Finance":
                    var fin = await _db.FinanceProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);
                    if (fin != null) fin.ProfileImagePath = relativePath;
                    break;
                case "Driver":
                    var drv = await _db.DriverProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);
                    if (drv != null) drv.ProfileImagePath = relativePath;
                    break;
            }

            await _db.SaveChangesAsync();
            return Json(new { success = true, profileImagePath = relativePath });
        }

        private async Task<ProfileData> GetProfileData(string userId, string role)
        {
            switch (role)
            {
                case "CEO":
                    var ceo = await _db.CEOProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
                    if (ceo != null) return new ProfileData(ceo.FullName, ceo.ContactNumber, ceo.Street, ceo.Barangay, ceo.City, ceo.Province, ceo.ZipCode, ceo.Country, ceo.ProfileImagePath);
                    break;
                case "Manager":
                    var mgr = await _db.ManagerProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
                    if (mgr != null) return new ProfileData(mgr.FullName, mgr.ContactNumber, mgr.Street, mgr.Barangay, mgr.City, mgr.Province, mgr.ZipCode, mgr.Country, mgr.ProfileImagePath);
                    break;
                case "Finance":
                    var fin = await _db.FinanceProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
                    if (fin != null) return new ProfileData(fin.FullName, fin.ContactNumber, fin.Street, fin.Barangay, fin.City, fin.Province, fin.ZipCode, fin.Country, fin.ProfileImagePath);
                    break;
                case "Driver":
                    var drv = await _db.DriverProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
                    if (drv != null) return new ProfileData(drv.FullName, drv.ContactNumber, drv.Street, drv.Barangay, drv.City, drv.Province, drv.ZipCode, drv.Country, drv.ProfileImagePath);
                    break;
            }
            return new ProfileData();
        }

        private record ProfileData(string? FullName = null, string? Contact = null, string? Street = null, string? Barangay = null, string? City = null, string? Province = null, string? Zip = null, string? Country = null, string? ImagePath = null);
    }
}
