using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using RealEstateAdmin.Models;
using RealEstateAdmin.Services;

namespace RealEstateAdmin.Controllers
{
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class AgendaController : Controller
    {
        private readonly IAgendaService _agendaService;
        private readonly UserManager<ApplicationUser> _userManager;

        public AgendaController(IAgendaService agendaService, UserManager<ApplicationUser> userManager)
        {
            _agendaService = agendaService;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index()
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var data = await _agendaService.GetAgendaAsync(currentUser.Id, User.IsInRole("SuperAdmin"));
            return View(data);
        }

        public async Task<IActionResult> Details(int id)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var result = await _agendaService.GetEventDetailsAsync(id, currentUser.Id, User.IsInRole("SuperAdmin"));
            if (result.Success)
            {
                return View(result.Data);
            }

            return HandleResult(result);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Accept(int id)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var result = await _agendaService.AcceptEventAsync(id, currentUser.Id, User.IsInRole("SuperAdmin"));
            if (result.Success)
            {
                TempData["Success"] = result.Message;
                return RedirectToAction(nameof(Index));
            }

            return HandleResult(result);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Refuse(int id)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var result = await _agendaService.RefuseEventAsync(id, currentUser.Id, User.IsInRole("SuperAdmin"));
            if (result.Success)
            {
                TempData["Success"] = result.Message;
                return RedirectToAction(nameof(Index));
            }

            return HandleResult(result);
        }

        private IActionResult HandleResult(ServiceResult result)
        {
            switch (result.ErrorCode)
            {
                case ServiceErrorCode.NotFound:
                    return NotFound();
                case ServiceErrorCode.BadRequest:
                    return BadRequest();
                case ServiceErrorCode.Forbidden:
                    return Forbid();
                default:
                    TempData["Error"] = result.Message ?? "Une erreur est survenue.";
                    return RedirectToAction(nameof(Index));
            }
        }
    }
}
