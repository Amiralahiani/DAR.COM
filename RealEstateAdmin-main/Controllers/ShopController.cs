using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using RealEstateAdmin.Models;
using RealEstateAdmin.Services;

namespace RealEstateAdmin.Controllers
{
    [Authorize]
    public class ShopController : Controller
    {
        private readonly IShopService _shopService;
        private readonly UserManager<ApplicationUser> _userManager;

        public ShopController(IShopService shopService, UserManager<ApplicationUser> userManager)
        {
            _shopService = shopService;
            _userManager = userManager;
        }

        // GET: Shop
        public async Task<IActionResult> Index(
            string? titre,
            decimal? prixMin,
            decimal? prixMax,
            string? adresse,
            int? surfaceMin,
            int? surfaceMax,
            string? statut,
            string? solde)
        {
            var filter = BuildFilter(titre, prixMin, prixMax, adresse, surfaceMin, surfaceMax, statut, solde);

            var data = await _shopService.GetIndexDataAsync(filter);

            return View(data);
        }

        [HttpGet("/Solde")]
        [HttpGet("/Shop/Solde")]
        public async Task<IActionResult> Solde(
            string? titre,
            decimal? prixMin,
            decimal? prixMax,
            string? adresse,
            int? surfaceMin,
            int? surfaceMax,
            string? statut)
        {
            var filter = BuildFilter(titre, prixMin, prixMax, adresse, surfaceMin, surfaceMax, statut, "1");

            var data = await _shopService.GetSoldeDataAsync(filter);

            return View(data);
        }

        // POST: Shop/ExpressInterest/5
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ExpressInterest(int id)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var result = await _shopService.ExpressInterestAsync(id, currentUser.Id);
            if (result.Success)
            {
                TempData["Success"] = result.Message;
                return RedirectToAction(nameof(Index));
            }

            return HandleResult(result);
        }

        // POST: Shop/ReserveVisit/5
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReserveVisit(int id, string visitSlot)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null)
            {
                return RedirectToAction("Login", "Account");
            }

            if (!DateTime.TryParse(visitSlot, out var parsedSlot))
            {
                TempData["Error"] = "Créneau invalide.";
                return RedirectToAction(nameof(Index));
            }

            var result = await _shopService.ReserveVisitAsync(id, parsedSlot, currentUser.Id);
            if (result.Success)
            {
                TempData["Success"] = result.Message;
                return RedirectToAction(nameof(Index));
            }

            return HandleResult(result);
        }

        // POST: Shop/RequestMeeting/5
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestMeeting(int id, string meetingSlot)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null)
            {
                return RedirectToAction("Login", "Account");
            }

            if (!DateTime.TryParse(meetingSlot, out var meetingDateTime))
            {
                TempData["Error"] = "Créneau invalide.";
                return RedirectToAction(nameof(Index));
            }

            var result = await _shopService.RequestAgentMeetingAsync(id, meetingDateTime, currentUser.Id);
            if (result.Success)
            {
                TempData["Success"] = result.Message;
                return RedirectToAction(nameof(Index));
            }

            return HandleResult(result);
        }

        private static ShopFilter BuildFilter(
            string? titre,
            decimal? prixMin,
            decimal? prixMax,
            string? adresse,
            int? surfaceMin,
            int? surfaceMax,
            string? statut,
            string? solde)
        {
            return new ShopFilter
            {
                Titre = titre,
                PrixMin = prixMin,
                PrixMax = prixMax,
                Adresse = adresse,
                SurfaceMin = surfaceMin,
                SurfaceMax = surfaceMax,
                Statut = statut,
                Solde = solde
            };
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
