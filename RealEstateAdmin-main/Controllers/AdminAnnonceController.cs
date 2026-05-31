using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateAdmin.Data;
using RealEstateAdmin.Models;
using System.Text;
using System.Text.Json;

namespace RealEstateAdmin.Controllers
{
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class AdminAnnonceController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AdminAnnonceController> _logger;

        public AdminAnnonceController(
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<AdminAnnonceController> logger)
        {
            _db = db;
            _userManager = userManager;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
        }

        // GET: AdminAnnonce
        public async Task<IActionResult> Index(string? statut)
        {
            var query = _db.Annonces.Include(a => a.Photos).AsQueryable();

            if (!string.IsNullOrWhiteSpace(statut))
                query = query.Where(a => a.Statut == statut);

            var annonces = await query.OrderByDescending(a => a.CreatedAt).ToListAsync();

            ViewBag.StatutFiltre = statut;
            ViewBag.Counts = new
            {
                EnAttente  = await _db.Annonces.CountAsync(a => a.Statut == "En attente"),
                Approuvee  = await _db.Annonces.CountAsync(a => a.Statut == "Approuvée"),
                Refusee    = await _db.Annonces.CountAsync(a => a.Statut == "Refusée")
            };

            return View(annonces);
        }

        // POST: AdminAnnonce/Approuver/5
        // Convertit l'Annonce en BienImmobilier et met son statut à "Approuvée"
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Approuver(int id)
        {
            var annonce = await _db.Annonces.Include(a => a.Photos).FirstOrDefaultAsync(a => a.Id == id);
            if (annonce == null)
                return NotFound();

            if (annonce.Statut != "En attente")
            {
                TempData["Error"] = "Cette annonce a déjà été traitée.";
                return RedirectToAction(nameof(Index));
            }

            var admin = await _userManager.GetUserAsync(User);

            var bien = new BienImmobilier
            {
                UserId                       = annonce.UserId,
                Titre                        = BuildTitre(annonce),
                Description                  = annonce.Description,
                Prix                         = annonce.PrixTnd,
                Adresse                      = $"{annonce.Delegation}, {annonce.Gouvernorat}",
                Surface                      = annonce.SurfaceM2,
                NombrePieces                 = annonce.NbChambres,
                TypeTransaction               = "A Vendre",
                StatutCommercial             = "Disponible",
                IsPublished                  = true,
                PublicationStatus            = "Publié",
                PublicationValidatedByAdminId = admin?.Id,
                PublicationValidatedAt       = DateTime.UtcNow,
                NatureBien                   = annonce.NatureBien,
                EtatBien                     = annonce.EtatBien,
                HasAscenseur                 = annonce.HasAscenseur,
                HasBalcon                    = annonce.HasBalcon,
                HasChauffageCentral          = annonce.HasChauffageCentral,
                HasClimatisation             = annonce.HasClimatisation,
                HasGarage                    = annonce.HasGarage,
                HasJardin                    = annonce.HasJardin,
                HasParking                   = annonce.HasParking,
                HasPiscine                   = annonce.HasPiscine,
                HasTerrasse                  = annonce.HasTerrasse,
            };

            // Première photo comme image principale, reste dans la galerie
            var photos = annonce.Photos.Where(p => !string.IsNullOrWhiteSpace(p.Url)).ToList();
            if (photos.Count > 0)
            {
                bien.ImageUrl = photos[0].Url;
                foreach (var photo in photos)
                    bien.Images.Add(new BienImage { Url = photo.Url });
            }

            // Géocodage automatique depuis "Delegation, Gouvernorat"
            await GeocodeAsync(bien);

            _db.Biens.Add(bien);
            await _db.SaveChangesAsync();

            annonce.Statut = "Approuvée";
            annonce.BienImmobilierId = bien.Id;
            await _db.SaveChangesAsync();

            // Compteur ML: incrémenter à la publication shop (et non à la vente).
            await TrySendPublicationToPriceModelAsync(annonce);

            TempData["Success"] = $"Annonce #{annonce.Id} approuvée — Bien #{bien.Id} créé et publié sur le shop.";
            return RedirectToAction(nameof(Index));
        }

        // POST: AdminAnnonce/Refuser/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Refuser(int id)
        {
            var annonce = await _db.Annonces.FindAsync(id);
            if (annonce == null)
                return NotFound();

            if (annonce.Statut != "En attente")
            {
                TempData["Error"] = "Cette annonce a déjà été traitée.";
                return RedirectToAction(nameof(Index));
            }

            annonce.Statut = "Refusée";
            await _db.SaveChangesAsync();

            TempData["Success"] = $"Annonce #{annonce.Id} refusée.";
            return RedirectToAction(nameof(Index));
        }

        private static string BuildTitre(Annonce a)
        {
            if (!string.IsNullOrWhiteSpace(a.Titre))
                return a.Titre;

            var type = !string.IsNullOrWhiteSpace(a.NatureBien)
                ? a.NatureBien
                : a.SurfaceM2 switch
                {
                    < 60  => "Studio",
                    < 100 => "Appartement",
                    < 250 => "Appartement spacieux",
                    _     => "Villa"
                };
            return $"{type} {a.SurfaceM2} m² — {a.Delegation}, {a.Gouvernorat}";
        }

        private async Task GeocodeAsync(BienImmobilier bien)
        {
            if (bien.Latitude.HasValue && bien.Longitude.HasValue) return;
            if (string.IsNullOrWhiteSpace(bien.Adresse)) return;

            try
            {
                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("RealEstateAdmin/1.0");
                client.Timeout = TimeSpan.FromSeconds(5);

                var url = "https://nominatim.openstreetmap.org/search?format=json&limit=1&q="
                          + System.Net.WebUtility.UrlEncode(bien.Adresse + ", Tunisie");

                var json = await client.GetStringAsync(url);
                var arr  = System.Text.Json.JsonSerializer.Deserialize<List<NominatimResult>>(json);

                if (arr != null && arr.Count > 0
                    && double.TryParse(arr[0].lat, System.Globalization.NumberStyles.Any,
                                       System.Globalization.CultureInfo.InvariantCulture, out var lat)
                    && double.TryParse(arr[0].lon, System.Globalization.NumberStyles.Any,
                                       System.Globalization.CultureInfo.InvariantCulture, out var lon))
                {
                    bien.Latitude  = lat;
                    bien.Longitude = lon;
                }
            }
            catch { /* géocodage non bloquant */ }
        }

        private record NominatimResult(string lat, string lon);

        private async Task TrySendPublicationToPriceModelAsync(Annonce annonce)
        {
            var mlEnabled = _configuration.GetValue<bool>("PricePredictionApi:Enabled");
            var mlBaseUrl = _configuration["PricePredictionApi:BaseUrl"];
            if (!mlEnabled || string.IsNullOrWhiteSpace(mlBaseUrl))
                return;

            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            try
            {
                // 1) Créer une prédiction pour obtenir prediction_id
                var estimatePayload = JsonSerializer.Serialize(new
                {
                    nature_bien = annonce.NatureBien ?? "Autre",
                    gouvernorat = annonce.Gouvernorat,
                    delegation = annonce.Delegation,
                    surface_m2 = annonce.SurfaceM2,
                    nb_chambres = annonce.NbChambres,
                    has_ascenseur = annonce.HasAscenseur ? 1 : 0,
                    has_balcon = annonce.HasBalcon ? 1 : 0,
                    has_chauffage_central = annonce.HasChauffageCentral ? 1 : 0,
                    has_climatisation = annonce.HasClimatisation ? 1 : 0,
                    has_garage = annonce.HasGarage ? 1 : 0,
                    has_jardin = annonce.HasJardin ? 1 : 0,
                    has_parking = annonce.HasParking ? 1 : 0,
                    has_piscine = annonce.HasPiscine ? 1 : 0,
                    has_terrasse = annonce.HasTerrasse ? 1 : 0,
                    etat = NormalizeEtatForMl(annonce.EtatBien)
                });

                var estimateResponse = await client.PostAsync(
                    $"{mlBaseUrl}/estimer",
                    new StringContent(estimatePayload, Encoding.UTF8, "application/json"));

                if (!estimateResponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Publication ML: /estimer a retourné {Status} pour annonce #{AnnonceId}.",
                        estimateResponse.StatusCode, annonce.Id);
                    return;
                }

                var estimateJson = await estimateResponse.Content.ReadAsStringAsync();
                var estimateDoc = JsonSerializer.Deserialize<JsonElement>(estimateJson);
                var predictionId = estimateDoc.TryGetProperty("prediction_id", out var idEl)
                    ? idEl.GetString()
                    : null;

                if (string.IsNullOrWhiteSpace(predictionId))
                {
                    _logger.LogWarning("Publication ML: prediction_id absent pour annonce #{AnnonceId}.", annonce.Id);
                    return;
                }

                // 2) Confirmer immédiatement avec le prix publié pour incrémenter le compteur
                var confirmPayload = JsonSerializer.Serialize(new
                {
                    prediction_id = predictionId,
                    prix_reel_tnd = annonce.PrixTnd
                });

                var confirmResponse = await client.PostAsync(
                    $"{mlBaseUrl}/confirmer-vente",
                    new StringContent(confirmPayload, Encoding.UTF8, "application/json"));

                if (!confirmResponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Publication ML: /confirmer-vente a retourné {Status} pour annonce #{AnnonceId}.",
                        confirmResponse.StatusCode, annonce.Id);
                    return;
                }

                _logger.LogInformation("Publication ML confirmée pour annonce #{AnnonceId}.", annonce.Id);
            }
            catch (Exception ex)
            {
                // Non bloquant : une erreur ML ne doit pas annuler la publication.
                _logger.LogWarning(ex, "Publication ML échouée pour annonce #{AnnonceId}.", annonce.Id);
            }
        }

        private static string? NormalizeEtatForMl(string? etatBien)
        {
            return etatBien switch
            {
                "Bon état" => "bon_etat",
                "Etat moyen" => "etat_moyen",
                "Rénové" => "retape",
                _ => etatBien
            };
        }
    }
}
