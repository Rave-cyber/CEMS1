using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CEMS.Controllers
{
    [Authorize(Roles = "Manager")]
    public class ManagerController : Controller
    {
        public IActionResult Dashboard()
        {
            return View("Dashboard/Index");
        }

        public IActionResult Expenses()
        {
            return View("Expenses/Index");
        }

        public IActionResult Team()
        {
            return View("Team/Index");
        }

        public IActionResult Budget()
        {
            return View("Budget/Index");
        }

        public IActionResult Index()
        {
            return RedirectToAction("Dashboard");
        }
    }
}