using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using RealEstateAdmin.Models;
using RealEstateAdmin.Services;

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
                return RedirectToAction(nameof(Index));
            }

            if (result.ErrorCode is ServiceErrorCode.BadRequest or ServiceErrorCode.NotFound or ServiceErrorCode.Validation)
            {
                ModelState.AddModelError(string.Empty, result.Message ?? "Données invalides.");
                var data = await _venteService.GetCreateDataAsync(input);
                return View(data);
            }

            return HandleResult(result);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdatePayment(int id, string paymentMethod, string paymentStatus)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            var result = await _venteService.UpdatePaymentAsync(id, paymentMethod, paymentStatus, currentUser?.Id);
            if (result.Success)
            {
                TempData["SuccessMessage"] = result.Message;
                return RedirectToAction(nameof(Index));
            }

            return HandleResult(result);
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
