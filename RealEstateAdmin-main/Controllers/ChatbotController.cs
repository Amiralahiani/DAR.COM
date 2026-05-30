using Microsoft.AspNetCore.Mvc;

namespace RealEstateAdmin.Controllers;

public class ChatbotController : Controller
{
    public IActionResult Index()
    {
        return View();
    }
}
