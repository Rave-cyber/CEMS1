using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace CEMS.Controllers
{
    public class HomeController : Controller
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly SignInManager<IdentityUser> _signInManager;

        public HomeController(UserManager<IdentityUser> userManager, SignInManager<IdentityUser> signInManager)
        {
            _userManager = userManager;
            _signInManager = signInManager;
        }

        public async Task<IActionResult> Index()
        {
            // If user is authenticated, show dashboard option
            if (User.Identity.IsAuthenticated)
            {
                var user = await _userManager.GetUserAsync(User);
                
                // If user is null, their cookie is stale (e.g. database was reset).
                if (user != null)
                {
                    ViewBag.IsAuthenticated = true;
                    var roles = await _userManager.GetRolesAsync(user);
                    ViewBag.UserRole = roles.FirstOrDefault();
                }
                else
                {
                    // Cookie exists but user does not. Treat as unauthenticated.
                    ViewBag.IsAuthenticated = false;
                    await _signInManager.SignOutAsync();
                    return RedirectToAction("Index");
                }
            }
            else
            {
                ViewBag.IsAuthenticated = false;
            }
            
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }
        
        // Redirect to appropriate dashboard
        [Authorize]
        public async Task<IActionResult> GoToDashboard()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) 
            {
                // Stale cookie, sign them out
                await _signInManager.SignOutAsync();
                return RedirectToAction("Index");
            }
            
            var roles = await _userManager.GetRolesAsync(user);

            if (roles.Contains("CEO"))
                return RedirectToAction("Dashboard", "CEO");
            else if (roles.Contains("Manager"))
                return RedirectToAction("Dashboard", "Manager");
            else if (roles.Contains("Driver"))
                return RedirectToAction("Dashboard", "Driver");
            else if (roles.Contains("Finance"))
                return RedirectToAction("Dashboard", "Finance");

            return RedirectToAction("Index");
        }

        // Handle Access Denied
        public IActionResult AccessDenied()
        {
            return View();
        }

        // DEBUG: Show current user's roles and permissions
        [Authorize]
        public async Task<IActionResult> DebugRoles()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return NotFound("User not found");

            var roles = await _userManager.GetRolesAsync(user);

            return Ok(new
            {
                email = user.Email,
                userId = user.Id,
                userName = user.UserName,
                roles = roles,
                isAuthenticated = User.Identity.IsAuthenticated,
                timestamp = DateTime.UtcNow
            });
        }
    }
}