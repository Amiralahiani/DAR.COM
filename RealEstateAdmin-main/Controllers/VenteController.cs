using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using RealEstateAdmin.Models;
using RealEstateAdmin.Services;
using System.Globalization;

namespace RealEstateAdmin.Controllers
{
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class VenteController : Controller
    {
        private readonly IVenteService _venteService;
        private readonly UserManager<ApplicationUser> _userManager;

        public VenteController(IVenteService venteService, UserManager<ApplicationUser> userManager)
        {
            _venteService = venteService;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index(string? paymentMethod, string? paymentStatus)
        {
            var data = await _venteService.GetIndexDataAsync(new SalesFilter
            {
                PaymentMethod = paymentMethod,
                PaymentStatus = paymentStatus
            });

            ViewBag.PaymentMethods = data.PaymentMethods.ToArray();
            ViewBag.PaymentStatuses = data.PaymentStatuses.ToArray();
            ViewBag.PaymentMethod = data.Filter.PaymentMethod;
            ViewBag.PaymentStatus = data.Filter.PaymentStatus;
            ViewBag.TotalSales = data.TotalSales;
            ViewBag.TotalAmount = data.TotalAmount;
            ViewBag.PaidAmount = data.PaidAmount;

            return View(data.Sales);
        }

        public async Task<IActionResult> Create()
        {
            var data = await _venteService.GetCreateDataAsync();
            return View(data);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(SalesCreateInput input)
        {
            if (!ModelState.IsValid)
            {
                var invalidData = await _venteService.GetCreateDataAsync(input);
                return View(invalidData);
            }

            var currentUser = await _userManager.GetUserAsync(User);
            var result = await _venteService.CreateManualAsync(input, currentUser?.Id);
            if (result.Success)
            {
                TempData["SuccessMessage"] = result.Message;
                // Le contrat est généré automatiquement pendant la saisie.
                return RedirectToAction(nameof(Details), new { id = result.Data });
            }

            if (result.ErrorCode is ServiceErrorCode.BadRequest or ServiceErrorCode.NotFound or ServiceErrorCode.Validation)
            {
                ModelState.AddModelError(string.Empty, result.Message ?? "Données invalides.");
                var data = await _venteService.GetCreateDataAsync(input);
                return View(data);
            }

            return HandleResult(result);
        }

        // --- Nouvelles Actions ---

        [HttpGet]
        public async Task<IActionResult> GetBienDetails(int id)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            var details = await _venteService.GetBienDetailsAsync(id, currentUser?.Id);
            if (details == null) return NotFound();
            return Json(details);
        }

        public async Task<IActionResult> Details(int id)
        {
            var detail = await _venteService.GetTransactionDetailAsync(id);
            if (detail == null) return NotFound();
            return View(detail);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateContrat(int saleId, string? conditions)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            var result = await _venteService.CreateContratAsync(saleId, conditions, currentUser?.Id);
            
            if (result.Success)
                TempData["SuccessMessage"] = result.Message;
            else
                TempData["ErrorMessage"] = result.Message;

            return RedirectToAction(nameof(Details), new { id = saleId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ExecuteContrat(int contratId, int saleId)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            var result = await _venteService.ExecuteContratAsync(contratId, currentUser?.Id ?? "System");
            
            if (result.Success)
                TempData["SuccessMessage"] = result.Message;
            else
                TempData["ErrorMessage"] = result.Message;

            return RedirectToAction(nameof(Details), new { id = saleId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddVersement(int saleId, string? montant, string mode, string? note)
        {
            if (!TryParseMontant(montant, out var parsedMontant))
            {
                TempData["ErrorMessage"] = "Montant invalide. Utilisez un nombre valide (ex: 1500.50 ou 1500,50).";
                return RedirectToAction(nameof(Details), new { id = saleId });
            }

            var currentUser = await _userManager.GetUserAsync(User);
            var result = await _venteService.AddVersementAsync(saleId, parsedMontant, mode, note, currentUser?.Id ?? "System");
            
            if (result.Success)
                TempData["SuccessMessage"] = result.Message;
            else
                TempData["ErrorMessage"] = result.Message;

            return RedirectToAction(nameof(Details), new { id = saleId });
        }

        private static bool TryParseMontant(string? rawAmount, out decimal amount)
        {
            amount = 0m;
            if (string.IsNullOrWhiteSpace(rawAmount))
            {
                return false;
            }

            var normalized = rawAmount.Trim();

            if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.CurrentCulture, out amount))
            {
                return true;
            }

            if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out amount))
            {
                return true;
            }

            var fr = CultureInfo.GetCultureInfo("fr-FR");
            if (decimal.TryParse(normalized, NumberStyles.Number, fr, out amount))
            {
                return true;
            }

            normalized = normalized.Replace(',', '.');
            return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
        }

        public async Task<IActionResult> ExportCsv()
        {
            var csv = await _venteService.ExportCsvAsync();
            return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", "ventes.csv");
        }

        public async Task<IActionResult> ExportPdf()
        {
            var bytes = await _venteService.ExportPdfAsync();
            return File(bytes, "application/pdf", "ventes.pdf");
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
                    TempData["ErrorMessage"] = result.Message ?? "Une erreur est survenue.";
                    return RedirectToAction(nameof(Index));
            }
        }
    }
}
