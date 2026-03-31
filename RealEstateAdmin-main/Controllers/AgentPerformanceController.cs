using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using RealEstateAdmin.Services;
using RealEstateAdmin.Models;

namespace RealEstateAdmin.Controllers
{
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class AgentPerformanceController : Controller
    {
        private readonly IAgentPerformanceService _performanceService;
        private readonly UserManager<ApplicationUser> _userManager;

        public AgentPerformanceController(IAgentPerformanceService performanceService, UserManager<ApplicationUser> userManager)
        {
            _performanceService = performanceService;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index()
        {
            var list = await _performanceService.GetAllViewModelsAsync();
            var sortedList = list.OrderByDescending(p => p.ScoreGlobal).ToList();
            ViewBag.CurrentUserId = _userManager.GetUserId(User);
            return View(sortedList);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Recompute()
        {
            await _performanceService.RecomputeAllAsync();
            TempData["Success"] = "Scores recalculés avec succès.";
            return RedirectToAction(nameof(Index));
        }
    }
}
