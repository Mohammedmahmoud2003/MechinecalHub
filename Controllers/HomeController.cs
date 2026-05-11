using System.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using HRSystem.Models;
using HRSystem.Infrastructure;

namespace HRSystem.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        return RedirectToPage("/Account/Login", new { area = "Identity" });
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
