using Microsoft.AspNetCore.Mvc;

namespace AxialFanMVC.Controllers
{
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToAction("Index", "Projects");
            return View();
        }
    }
}
