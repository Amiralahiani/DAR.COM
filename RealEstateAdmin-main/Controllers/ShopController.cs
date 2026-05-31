using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateAdmin.Data;
using RealEstateAdmin.Models;
using RealEstateAdmin.Services;

namespace RealEstateAdmin.Controllers
{
    [Authorize]
    public class ShopController : Controller
    {
        private readonly IShopService _shopService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IMLService _mlService;
        private readonly ApplicationDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;

        public ShopController(
            IShopService shopService,
            UserManager<ApplicationUser> userManager,
            IMLService mlService,
            ApplicationDbContext context,
            IHttpClientFactory httpClientFactory)
        {
            _shopService = shopService;
            _userManager = userManager;
            _mlService = mlService;
            _context = context;
            _httpClientFactory = httpClientFactory;
        }

        // GET: Shop
        public async Task<IActionResult> Index(
            string? titre,
            decimal? prixMin,
            decimal? prixMax,
            string? adresse,
            int? surfaceMin,
            int? surfaceMax,
            string? natureBien,
            string? etatBien,
            int? chambresMin,
            string? solde)
        {
            var filter = BuildFilter(titre, prixMin, prixMax, adresse, surfaceMin, surfaceMax, natureBien, etatBien, chambresMin, solde);
            var data = await _shopService.GetIndexDataAsync(filter);

            var currentUser = await _userManager.GetUserAsync(User);
            var isClient = currentUser is not null
                && !User.IsInRole("Admin")
                && !User.IsInRole("SuperAdmin");

            if (isClient)
            {
                ViewBag.Recommendations = await _mlService.GetRecommendedBiensAsync(currentUser!.Id);
            }

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
            string? natureBien,
            string? etatBien,
            int? chambresMin)
        {
            var filter = BuildFilter(titre, prixMin, prixMax, adresse, surfaceMin, surfaceMax, natureBien, etatBien, chambresMin, "1");

            var data = await _shopService.GetSoldeDataAsync(filter);

            return View(data);
        }

        // GET: Shop/Map
        [HttpGet]
        public async Task<IActionResult> Map()
        {
            var biens = await _context.Biens
                .Where(b => b.IsPublished && b.PublicationStatus == "Publié")
                .ToListAsync();

            await GeocodesMissingAsync(biens);

            var vm = biens.Select(b => new {
                b.Id, b.Titre, b.Adresse,
                b.Prix, b.Surface, b.NombrePieces,
                b.ImageUrl, b.Latitude, b.Longitude,
                b.NatureBien, b.DiscountPercent
            }).ToList();

            return View(vm);
        }

        // GET: Shop/MapSolde
        [HttpGet]
        public async Task<IActionResult> MapSolde()
        {
            var biens = await _context.Biens
                .Where(b => b.IsPublished && b.PublicationStatus == "Publié" && b.DiscountPercent > 0)
                .ToListAsync();

            await GeocodesMissingAsync(biens);

            var vm = biens.Select(b => new {
                b.Id, b.Titre, b.Adresse,
                b.Prix, b.Surface, b.NombrePieces,
                b.ImageUrl, b.Latitude, b.Longitude,
                b.NatureBien, b.DiscountPercent
            }).ToList();

            ViewBag.SoldeOnly = true;
            return View("Map", vm);
        }

        private async Task GeocodesMissingAsync(List<RealEstateAdmin.Models.BienImmobilier> biens)
        {
            var sans = biens.Where(b => !b.Latitude.HasValue && !string.IsNullOrWhiteSpace(b.Adresse)).ToList();
            if (!sans.Any()) return;

            var http = _httpClientFactory.CreateClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("RealEstateAdmin/1.0");
            http.Timeout = TimeSpan.FromSeconds(5);

            bool anyUpdated = false;
            foreach (var b in sans)
            {
                try
                {
                    var url = "https://nominatim.openstreetmap.org/search?format=json&limit=1&q="
                              + System.Net.WebUtility.UrlEncode(b.Adresse + ", Tunisie");
                    var json = await http.GetStringAsync(url);
                    var arr  = System.Text.Json.JsonSerializer.Deserialize<List<NominatimGeo>>(json);
                    if (arr != null && arr.Count > 0
                        && double.TryParse(arr[0].lat, System.Globalization.NumberStyles.Any,
                                           System.Globalization.CultureInfo.InvariantCulture, out var lat)
                        && double.TryParse(arr[0].lon, System.Globalization.NumberStyles.Any,
                                           System.Globalization.CultureInfo.InvariantCulture, out var lon))
                    {
                        b.Latitude  = lat;
                        b.Longitude = lon;
                        anyUpdated  = true;
                    }
                    await Task.Delay(300); // respecter le rate-limit Nominatim
                }
                catch { /* non bloquant */ }
            }

            if (anyUpdated) await _context.SaveChangesAsync();
        }

        private record NominatimGeo(string lat, string lon);

        // GET: Shop/Details/5
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var bien = await _shopService.GetBienByIdAsync(id);
            if (bien is null) return NotFound();

            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser is not null)
            {
                // Enregistrer la vue pour le tracking ML (fire-and-forget)
                _ = Task.Run(async () =>
                {
                    _context.BienViews.Add(new BienView
                    {
                        UserId   = currentUser.Id,
                        BienId   = bien.Id,
                        Prix     = bien.Prix,
                        Surface  = bien.Surface ?? 0,
                        ViewedAt = DateTime.UtcNow,
                    });
                    await _context.SaveChangesAsync();
                });
            }

            return View(bien);
        }

        // POST: Shop/ExpressInterest/5
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ExpressInterest(int id)
        {
            if (IsAdminUser()) return Forbid();
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null) return RedirectToAction("Login", "Account");

            var result = await _shopService.ExpressInterestAsync(id, currentUser.Id);

            // Log du contact pour le tracking ML réel
            _context.AuditLogs.Add(new AuditLog
            {
                UserId     = currentUser.Id,
                Action     = "ContactEvent",
                EntityType = "BienImmobilier",
                EntityId   = id,
                Details    = "ExpressInterest",
                CreatedAt  = DateTime.UtcNow,
            });
            await _context.SaveChangesAsync();

            // Signal fort : l'utilisateur a exprimé un intérêt → collecter pour le modèle
            _ = Task.Run(async () =>
            {
                var behavior = await _mlService.EstimateBehaviorAsync(currentUser.Id);
                await _mlService.CollectAsync(currentUser.Id, behavior, targetLead: 1, targetBudget: 0);
            });

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
            if (IsAdminUser()) return Forbid();
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null) return RedirectToAction("Login", "Account");

            if (!DateTime.TryParse(visitSlot, out var parsedSlot))
            {
                TempData["Error"] = "Créneau invalide.";
                return RedirectToAction(nameof(Index));
            }

            var result = await _shopService.ReserveVisitAsync(id, parsedSlot, currentUser.Id);

            _context.AuditLogs.Add(new AuditLog
            {
                UserId     = currentUser.Id,
                Action     = "ContactEvent",
                EntityType = "BienImmobilier",
                EntityId   = id,
                Details    = "ReserveVisit",
                CreatedAt  = DateTime.UtcNow,
            });
            await _context.SaveChangesAsync();

            // Signal modéré : visite réservée → potentiel acheteur
            _ = Task.Run(async () =>
            {
                var behavior = await _mlService.EstimateBehaviorAsync(currentUser.Id);
                await _mlService.CollectAsync(currentUser.Id, behavior, targetLead: 1, targetBudget: 0);
            });

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
            if (IsAdminUser()) return Forbid();
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null) return RedirectToAction("Login", "Account");

            if (!DateTime.TryParse(meetingSlot, out var meetingDateTime))
            {
                TempData["Error"] = "Créneau invalide.";
                return RedirectToAction(nameof(Index));
            }

            var result = await _shopService.RequestAgentMeetingAsync(id, meetingDateTime, currentUser.Id);

            _context.AuditLogs.Add(new AuditLog
            {
                UserId     = currentUser.Id,
                Action     = "ContactEvent",
                EntityType = "BienImmobilier",
                EntityId   = id,
                Details    = "RequestMeeting",
                CreatedAt  = DateTime.UtcNow,
            });
            await _context.SaveChangesAsync();

            // Signal fort : demande de rendez-vous = acheteur sérieux
            _ = Task.Run(async () =>
            {
                var behavior = await _mlService.EstimateBehaviorAsync(currentUser.Id);
                await _mlService.CollectAsync(currentUser.Id, behavior, targetLead: 1, targetBudget: 0);
            });

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
            string? natureBien,
            string? etatBien,
            int? chambresMin,
            string? solde)
        {
            return new ShopFilter
            {
                Titre      = titre,
                PrixMin    = prixMin,
                PrixMax    = prixMax,
                Adresse    = adresse,
                SurfaceMin = surfaceMin,
                SurfaceMax = surfaceMax,
                NatureBien = natureBien,
                EtatBien   = etatBien,
                ChambresMin = chambresMin,
                Solde      = solde
            };
        }

        private bool IsAdminUser() =>
            User.IsInRole("Admin") || User.IsInRole("SuperAdmin");

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
