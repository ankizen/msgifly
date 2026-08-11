using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Msgifly.Web.Models;

namespace Msgifly.Web.Controllers;

public class HomeController : Controller
{
    /// <summary>Root "/" — matches the original's behavior of showing the login page here.</summary>
    public IActionResult Index()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Dashboard", new { area = "Admin" });
        }

        return RedirectToAction("Login", "Account", new { area = "Identity" });
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
