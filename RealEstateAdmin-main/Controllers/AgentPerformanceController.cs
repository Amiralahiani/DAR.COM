using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealEstateAdmin.Services;
using RealEstateAdmin.Models;

namespace RealEstateAdmin.Controllers
{
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class AgentPerformanceController : Controller
    {
        private readonly IAgentPerformanceService _performanceService;

        public AgentPerformanceController(IAgentPerformanceService performanceService)
        {
            _performanceService = performanceService;
        }

        public async Task<IActionResult> Index()
        {
            var list = await _performanceService.GetAllViewModelsAsync();
            return View(list);
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
