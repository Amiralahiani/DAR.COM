using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using RealEstateAdmin.Models;
using RealEstateAdmin.Services;

namespace RealEstateAdmin.Controllers
{
    [Authorize]
    public class BienImmobilierController : Controller
    {
        private readonly IBienImmobilierService _bienService;
        private readonly UserManager<ApplicationUser> _userManager;

        public BienImmobilierController(
            IBienImmobilierService bienService,
            UserManager<ApplicationUser> userManager)
        {
            _bienService = bienService;
            _userManager = userManager;
        }

        // GET: BienImmobilier
        public async Task<IActionResult> Index(
            string? titre,
            decimal? prixMin,
            decimal? prixMax,
            int? surfaceMin,
            string? typeTransaction,
            string? statutCommercial,
            string? publicationStatus)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            var filter = new BienFilter
            {
                Titre = titre,
                PrixMin = prixMin,
                PrixMax = prixMax,
                SurfaceMin = surfaceMin,
                TypeTransaction = typeTransaction,
                StatutCommercial = statutCommercial,
                PublicationStatus = publicationStatus
            };

            var data = await _bienService.GetIndexDataAsync(filter, currentUser?.Id, HasAdminAccess());

            ViewBag.Titre = data.Filter.Titre;
            ViewBag.PrixMin = data.Filter.PrixMin;
            ViewBag.PrixMax = data.Filter.PrixMax;
            ViewBag.SurfaceMin = data.Filter.SurfaceMin;
            ViewBag.TypeTransaction = data.Filter.TypeTransaction;
            ViewBag.StatutCommercial = data.Filter.StatutCommercial;
            ViewBag.PublicationStatus = data.Filter.PublicationStatus;
            ViewBag.TypeOptions = data.TypeOptions.ToArray();
            ViewBag.CommercialStatusOptions = data.CommercialStatusOptions.ToArray();
            ViewBag.PublicationStatusOptions = data.PublicationStatusOptions.ToArray();
            ViewBag.IsAdmin = data.IsAdmin;

            return View(data.Biens);
        }

        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> ExportCsv()
        {
            var csv = await _bienService.ExportCsvAsync();
            return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", "biens.csv");
        }

        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> ExportPdf()
        {
            var bytes = await _bienService.ExportPdfAsync();
            return File(bytes, "application/pdf", "biens.pdf");
        }

        // GET: BienImmobilier/Details/5
        [AllowAnonymous]
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var bienImmobilier = await _bienService.GetDetailsAsync(id.Value);
            if (bienImmobilier == null)
            {
                return NotFound();
            }

            return View(bienImmobilier);
        }

        // GET: BienImmobilier/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: BienImmobilier/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Id,Titre,Description,Prix,Adresse,Surface,NombrePieces,ImageUrl,TypeTransaction,StatutCommercial,IsPublished,PublicationStatus,ImageUrlsInput")] BienImmobilier bienImmobilier)
        {
            if (!ModelState.IsValid)
            {
                return View(bienImmobilier);
            }

            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null)
            {
                ModelState.AddModelError(string.Empty, "Vous devez être connecté pour créer un bien immobilier.");
                return View(bienImmobilier);
            }

            var result = await _bienService.CreateAsync(bienImmobilier, currentUser.Id, HasAdminAccess());
            if (!result.Success)
            {
                if (!string.IsNullOrWhiteSpace(result.Message))
                {
                    ModelState.AddModelError(string.Empty, result.Message);
                }

                return View(bienImmobilier);
            }

            return RedirectToAction(nameof(Index));
        }

        // GET: BienImmobilier/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var currentUser = await _userManager.GetUserAsync(User);
            var result = await _bienService.GetForEditAsync(id.Value, currentUser?.Id, HasAdminAccess());

            if (result.Success)
            {
                return View(result.Data);
            }

            return HandleResult(result);
        }

        // POST: BienImmobilier/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Titre,Description,Prix,Adresse,Surface,NombrePieces,ImageUrl,TypeTransaction,StatutCommercial,IsPublished,PublicationStatus,ImageUrlsInput")] BienImmobilier bienImmobilier)
        {
            if (!ModelState.IsValid)
            {
                return View(bienImmobilier);
            }

            var currentUser = await _userManager.GetUserAsync(User);
            var result = await _bienService.UpdateAsync(id, bienImmobilier, currentUser?.Id, HasAdminAccess());
            if (result.Success)
            {
                return RedirectToAction(nameof(Index));
            }

            if (result.ErrorCode == ServiceErrorCode.Validation && !string.IsNullOrWhiteSpace(result.Message))
            {
                ModelState.AddModelError(string.Empty, result.Message);
                return View(bienImmobilier);
            }

            return HandleResult(result);
        }

        // GET: BienImmobilier/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var currentUser = await _userManager.GetUserAsync(User);
            var result = await _bienService.GetForDeleteAsync(id.Value, currentUser?.Id, HasAdminAccess());
            if (result.Success)
            {
                return View(result.Data);
            }

            return HandleResult(result);
        }

        // POST: BienImmobilier/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            var result = await _bienService.DeleteAsync(id, currentUser?.Id, HasAdminAccess());
            if (result.Success)
            {
                return RedirectToAction(nameof(Index));
            }

            return HandleResult(result);
        }

        // POST: BienImmobilier/MettreEnVente/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MettreEnVente(int id)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            var result = await _bienService.MettreEnVenteAsync(id, currentUser?.Id, HasAdminAccess());
            if (result.Success)
            {
                TempData["Success"] = result.Message;
                return RedirectToAction(nameof(Index));
            }

            return HandleResult(result);
        }

        // POST: BienImmobilier/TogglePublish/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> TogglePublish(int id)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            var result = await _bienService.TogglePublishAsync(id, currentUser?.Id);
            if (result.Success)
            {
                return RedirectToAction(nameof(Index));
            }

            return HandleResult(result);
        }

        // POST: BienImmobilier/SetStatus/5 (publication)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> SetStatus(int id, string status)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            var result = await _bienService.SetPublicationStatusAsync(id, status, currentUser?.Id);
            if (result.Success)
            {
                return RedirectToAction(nameof(Index));
            }

            return HandleResult(result);
        }

        // POST: BienImmobilier/SetCommercialStatus/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> SetCommercialStatus(int id, string commercialStatus)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            var result = await _bienService.SetCommercialStatusAsync(id, commercialStatus, currentUser?.Id);
            if (result.Success)
            {
                return RedirectToAction(nameof(Index));
            }

            return HandleResult(result);
        }

        private bool HasAdminAccess()
        {
            return User.IsInRole("Admin") || User.IsInRole("SuperAdmin");
        }

        private IActionResult HandleResult(ServiceResult result)
        {
            switch (result.ErrorCode)
            {
                case ServiceErrorCode.NotFound:
                    return NotFound();
                case ServiceErrorCode.Forbidden:
                    return Forbid();
                case ServiceErrorCode.BadRequest:
                    return BadRequest();
                default:
                    TempData["Error"] = result.Message ?? "Une erreur est survenue.";
                    return RedirectToAction(nameof(Index));
            }
        }
    }
}
