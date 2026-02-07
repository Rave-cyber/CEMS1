using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CEMS.Controllers
{
    [Authorize(Roles = "CEO")]
    public class CEOController : Controller
    {
        public IActionResult Dashboard()
        {
            return View("Dashboard/Index"); // Looks in Views/CEO/Dashboard/Index.cshtml
        }

        public IActionResult Budget()
        {
            return View("Budget/Index");
        }

        public IActionResult Approvals()
        {
            return View("Approvals/Index");
        }

        public IActionResult Reports()
        {
            return View("Reports/Index");
        }

        public IActionResult Users()
        {
            return View("Users/Index");
        }

        // Optional: Index redirects to Dashboard
        public IActionResult Index()
        {
            return RedirectToAction("Dashboard");
        }
    }
}