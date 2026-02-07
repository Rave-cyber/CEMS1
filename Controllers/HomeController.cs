using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace CEMS.Controllers
{
    public class HomeController : Controller
    {
        private readonly UserManager<IdentityUser> _userManager;

        public HomeController(UserManager<IdentityUser> userManager)
        {
            _userManager = userManager;
        }

        public async Task<IActionResult> Index()
        {
            // If user is authenticated, show dashboard option
            if (User.Identity.IsAuthenticated)
            {
                ViewBag.IsAuthenticated = true;
                
                var user = await _userManager.GetUserAsync(User);
                var roles = await _userManager.GetRolesAsync(user);
                ViewBag.UserRole = roles.FirstOrDefault();
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
    }
}