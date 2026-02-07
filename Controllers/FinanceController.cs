using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CEMS.Controllers
{
    [Authorize(Roles = "Finance")]
    public class FinanceController : Controller
    {
        public IActionResult Dashboard()
        {
            return View("Dashboard/Index");
        }

        public IActionResult Reimbursements()
        {
            return View("Reimbursements/Index");
        }

        public IActionResult Payments()
        {
            return View("Payments/Index");
        }

        public IActionResult Reports()
        {
            return View("Reports/Index");
        }

        public IActionResult Index()
        {
            return RedirectToAction("Dashboard");
        }
    }
}