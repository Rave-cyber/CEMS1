using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CEMS.Controllers
{
    [Authorize(Roles = "Driver")]
    public class DriverController : Controller
    {
        public IActionResult Dashboard()
        {
            return View("Dashboard/Index");
        }

        public IActionResult Expenses()
        {
            return View("Expenses/Index");
        }

        public IActionResult Submit()
        {
            return View("Submit/Index");
        }

        public IActionResult History()
        {
            return View("History/Index");
        }

        public IActionResult Index()
        {
            return RedirectToAction("Dashboard");
        }
    }
}