using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateAdmin.Data;
using RealEstateAdmin.Models;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace RealEstateAdmin.Controllers
{
    [Authorize]
    public class AnnonceController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _env;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<AnnonceController> _logger;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public AnnonceController(
            ApplicationDbContext db,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            IWebHostEnvironment env,
            UserManager<ApplicationUser> userManager,
            ILogger<AnnonceController> logger)
        {
            _db = db;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _env = env;
            _userManager = userManager;
            _logger = logger;
        }

        // GET: Annonce/Wizard
        public IActionResult Wizard() => View();

        // GET: Annonce/MesAnnonces
        public async Task<IActionResult> MesAnnonces()
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null) return RedirectToAction("Login", "Account");

            var annonces = await _db.Annonces
                .Include(a => a.Photos)
                .Where(a => a.UserId == currentUser.Id)
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync();

            return View(annonces);
        }

        // ─────────────────────────────────────────────────────────────────────
        // ML PROXY ENDPOINTS
        // Each endpoint tries to forward to the Python ML engine. If the engine
        // is unavailable, a plausible mock response is returned so the wizard
        // keeps working during development.
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Reçoit une photo, la sauvegarde, puis appelle le microservice d'analyse d'images
        /// (POST {ImageAnalysisApi:BaseUrl}/analyze) pour obtenir :
        ///   - decision : "approved" | "review" | "rejected"
        ///   - reason   : message explicatif (flou, contenu adulte, hors-sujet, doublon…)
        /// L'URL du microservice est configurée dans appsettings.json → ImageAnalysisApi:BaseUrl
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadAndVerify(IFormFile photo)
        {
            if (photo == null || photo.Length == 0)
                return Json(new { valid = false, decision = "rejected", reason = "Aucun fichier reçu." });

            var allowedTypes = new[] { "image/jpeg", "image/jpg", "image/png", "image/webp" };
            if (!allowedTypes.Contains(photo.ContentType.ToLower()))
                return Json(new { valid = false, decision = "rejected", reason = "Format non supporté. Utilisez JPG, PNG ou WebP." });

            if (photo.Length > 10 * 1024 * 1024)
                return Json(new { valid = false, decision = "rejected", reason = "La photo ne doit pas dépasser 10 Mo." });

            // Sauvegarde dans wwwroot/uploads/annonces/
            var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", "annonces");
            Directory.CreateDirectory(uploadsDir);

            var ext      = Path.GetExtension(photo.FileName).ToLower();
            var fileName = $"{Guid.NewGuid()}{ext}";
            var filePath = Path.Combine(uploadsDir, fileName);

            await using (var stream = System.IO.File.Create(filePath))
                await photo.CopyToAsync(stream);

            var relativeUrl = $"/uploads/annonces/{fileName}";

            // Appel au microservice d'analyse d'images (dar.com analyse image v1.2)
            try
            {
                var imageApiUrl = _configuration["ImageAnalysisApi:BaseUrl"] ?? "http://localhost:8001";
                var client      = _httpClientFactory.CreateClient();
                client.Timeout  = TimeSpan.FromSeconds(20);

                using var content = new MultipartFormDataContent();
                await using var fileStream = System.IO.File.OpenRead(filePath);
                var fileContent = new StreamContent(fileStream);
                fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(photo.ContentType);
                content.Add(fileContent, "image", fileName);  // champ attendu par /analyze

                var response = await client.PostAsync($"{imageApiUrl}/analyze", content);

                if (response.IsSuccessStatusCode)
                {
                    var json     = await response.Content.ReadAsStringAsync();
                    var doc      = JsonSerializer.Deserialize<JsonElement>(json);
                    var decision = doc.TryGetProperty("decision", out var d) ? d.GetString() ?? "review" : "review";
                    var reason   = doc.TryGetProperty("reason",   out var r) ? r.GetString() : null;

                    // "approved" et "review" sont acceptés ; seul "rejected" bloque
                    var valid = decision != "rejected";
                    return Json(new { valid, decision, reason, url = relativeUrl });
                }

                _logger.LogWarning("Microservice analyse image a retourné {Status}.", response.StatusCode);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Microservice analyse image injoignable — fallback accepté.");
            }

            // Fallback : service indisponible → on accepte avec statut "review"
            return Json(new { valid = true, decision = "review", reason = "Service d'analyse indisponible — vérification manuelle.", url = relativeUrl });
        }

        /// <summary>
        /// Génère une description immobilière à partir des données de l'étape 1.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult GenerateDescription([FromBody] GenerateDescriptionRequest request)
        {
            var equipements = BuildEquipementsList(request);
            var description = BuildDescription(request.SurfaceM2, request.NbChambres,
                                               request.Gouvernorat, request.Delegation,
                                               request.NatureBien, request.EtatBien, equipements);
            return Json(new { description });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PredictPrice([FromBody] PredictPriceRequest request)
        {
            // ── Modèle ML de prix (dossier `estimation prix`) ────────────────
            // Configuration attendue :
            //   1. Mettre PricePredictionApi:Enabled = true dans appsettings.json
            //   2. Mettre PricePredictionApi:BaseUrl  = "http://localhost:PORT"
            //   3. Mettre PricePredictionApi:Endpoint = "/estimer"
            // Réponse principale de l'API Python :
            // { "prix_predit_tnd": number, "prediction_id": "...", ... }
            var mlEnabled = _configuration.GetValue<bool>("PricePredictionApi:Enabled");
            var mlBaseUrl = _configuration["PricePredictionApi:BaseUrl"];
            var mlEndpoint = _configuration["PricePredictionApi:Endpoint"] ?? "/estimer";

            if (mlEnabled && !string.IsNullOrWhiteSpace(mlBaseUrl))
            {
                try
                {
                    var client = _httpClientFactory.CreateClient();
                    client.Timeout = TimeSpan.FromSeconds(10);

                    var body = JsonSerializer.Serialize(new
                    {
                        nature_bien           = request.NatureBien ?? "Autre",
                        surface_m2            = request.SurfaceM2,
                        nb_chambres           = request.NbChambres,
                        gouvernorat           = request.Gouvernorat,
                        delegation            = request.Delegation,
                        has_ascenseur         = request.HasAscenseur ? 1 : 0,
                        has_balcon            = request.HasBalcon ? 1 : 0,
                        has_chauffage_central = request.HasChauffageCentral ? 1 : 0,
                        has_climatisation     = request.HasClimatisation ? 1 : 0,
                        has_garage            = request.HasGarage ? 1 : 0,
                        has_jardin            = request.HasJardin ? 1 : 0,
                        has_parking           = request.HasParking ? 1 : 0,
                        has_piscine           = request.HasPiscine ? 1 : 0,
                        has_terrasse          = request.HasTerrasse ? 1 : 0,
                        etat                  = NormalizeEtatForMl(request.EtatBien)
                    });

                    var response = await client.PostAsync(
                        $"{mlBaseUrl}{mlEndpoint}",
                        new StringContent(body, Encoding.UTF8, "application/json"));

                    if (response.IsSuccessStatusCode)
                    {
                        var json   = await response.Content.ReadAsStringAsync();
                        var doc    = JsonSerializer.Deserialize<JsonElement>(json);
                        if (TryExtractPrice(doc, out var predictedPrice))
                        {
                            int? low = null;
                            int? high = null;

                            if (TryExtractInt(doc, "prix_fourchette_basse", out var lowValue)) low = lowValue;
                            if (TryExtractInt(doc, "prix_fourchette_haute", out var highValue)) high = highValue;

                            return Json(new
                            {
                                prix_tnd = predictedPrice,
                                prediction_id = doc.TryGetProperty("prediction_id", out var id) ? id.GetString() : null,
                                prix_fourchette_basse = low,
                                prix_fourchette_haute = high,
                                confiance = doc.TryGetProperty("confiance", out var c) ? c.GetString() : null,
                                message = doc.TryGetProperty("message", out var m) ? m.GetString() : null
                            });
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Modèle ML prix a retourné {Status}.", response.StatusCode);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Modèle ML prix injoignable — fallback règles métier.");
                }
            }

            // ── Fallback : règles métier (prix/m² par gouvernorat) ────────────
            return Json(new { prix_tnd = CalculateMockPrice(request) });
        }

        // ─────────────────────────────────────────────────────────────────────
        // PUBLISH
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Enregistre l'annonce localement (fallback) ou la transfère vers l'API externe
        /// si ExternalBienApi:Enabled = true dans appsettings.json.
        /// L'API externe doit renvoyer { "success": true, "id": number }.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Publish([FromBody] PublishAnnonceRequest request)
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .ToList();
                return Json(new { success = false, errors });
            }

            var currentUser = await _userManager.GetUserAsync(User);

            // ── Option A : API externe ────────────────────────────────────────
            var externalEnabled = _configuration.GetValue<bool>("ExternalBienApi:Enabled");
            var externalBaseUrl = _configuration["ExternalBienApi:BaseUrl"];

            if (externalEnabled && !string.IsNullOrWhiteSpace(externalBaseUrl))
            {
                try
                {
                    var client = _httpClientFactory.CreateClient();
                    client.Timeout = TimeSpan.FromSeconds(15);

                    var payload = JsonSerializer.Serialize(new
                    {
                        userId              = currentUser?.Id,
                        titre               = request.Titre,
                        gouvernorat         = request.Gouvernorat,
                        delegation          = request.Delegation,
                        surfaceM2           = request.SurfaceM2,
                        nbChambres          = request.NbChambres,
                        natureBien          = request.NatureBien,
                        etatBien            = request.EtatBien,
                        prixTnd             = request.PrixTnd,
                        description         = request.Description,
                        photos              = request.Photos,
                        hasAscenseur        = request.HasAscenseur,
                        hasBalcon           = request.HasBalcon,
                        hasChauffageCentral = request.HasChauffageCentral,
                        hasClimatisation    = request.HasClimatisation,
                        hasGarage           = request.HasGarage,
                        hasJardin           = request.HasJardin,
                        hasParking          = request.HasParking,
                        hasPiscine          = request.HasPiscine,
                        hasTerrasse         = request.HasTerrasse
                    }, _jsonOptions);

                    var externalEndpoint = _configuration["ExternalBienApi:Endpoint"] ?? "/api/biens";
                    var response = await client.PostAsync(
                        $"{externalBaseUrl}{externalEndpoint}",
                        new StringContent(payload, Encoding.UTF8, "application/json"));

                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        return Content(json, "application/json");
                    }

                    _logger.LogWarning("API externe retourné {Status} — fallback local.", response.StatusCode);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "API externe injoignable — fallback local.");
                }
            }

            // ── Option B : sauvegarde locale (fallback) ───────────────────────
            var annonce = new Annonce
            {
                UserId              = currentUser?.Id,
                Gouvernorat         = request.Gouvernorat,
                Delegation          = request.Delegation,
                SurfaceM2           = request.SurfaceM2,
                NbChambres          = request.NbChambres,
                Titre               = request.Titre,
                NatureBien          = request.NatureBien,
                EtatBien            = request.EtatBien,
                HasAscenseur        = request.HasAscenseur,
                HasBalcon           = request.HasBalcon,
                HasChauffageCentral = request.HasChauffageCentral,
                HasClimatisation    = request.HasClimatisation,
                HasGarage           = request.HasGarage,
                HasJardin           = request.HasJardin,
                HasParking          = request.HasParking,
                HasPiscine          = request.HasPiscine,
                HasTerrasse         = request.HasTerrasse,
                Description         = request.Description,
                PrixTnd             = request.PrixTnd,
                CreatedAt           = DateTime.UtcNow,
                Statut              = "En attente"
            };

            foreach (var url in request.Photos.Where(u => !string.IsNullOrWhiteSpace(u)))
                annonce.Photos.Add(new AnnoncePhoto { Url = url });

            _db.Annonces.Add(annonce);
            await _db.SaveChangesAsync();

            return Json(new { success = true, id = annonce.Id });
        }

        // ─────────────────────────────────────────────────────────────────────
        // HELPERS
        // ─────────────────────────────────────────────────────────────────────

        private static List<string> BuildEquipementsList(GenerateDescriptionRequest r)
        {
            var list = new List<string>();
            if (r.HasAscenseur) list.Add("ascenseur");
            if (r.HasBalcon) list.Add("balcon");
            if (r.HasChauffageCentral) list.Add("chauffage central");
            if (r.HasClimatisation) list.Add("climatisation");
            if (r.HasGarage) list.Add("garage");
            if (r.HasJardin) list.Add("jardin");
            if (r.HasParking) list.Add("parking");
            if (r.HasPiscine) list.Add("piscine");
            if (r.HasTerrasse) list.Add("terrasse");
            return list;
        }

        private static string BuildDescription(int surface, int chambres, string gouvernorat,
            string delegation, string? natureBien, string? etatBien, List<string> equipements)
        {
            var localisation = !string.IsNullOrWhiteSpace(delegation)
                ? $"{delegation}, {gouvernorat}"
                : gouvernorat;

            var typeBien = !string.IsNullOrWhiteSpace(natureBien)
                ? natureBien.ToLower()
                : surface switch
                {
                    < 60  => "studio",
                    < 100 => "appartement",
                    < 250 => "appartement spacieux",
                    _     => "villa / maison"
                };

            var chambreStr = chambres switch
            {
                0 => string.Empty,
                1 => "1 chambre",
                _ => $"{chambres} chambres"
            };

            // Phrase 1 — présentation
            var p1 = $"{char.ToUpper(typeBien[0])}{typeBien[1..]} de {surface} m² " +
                     $"idéalement situé à {localisation}.";

            // Phrase 2 — composition
            var p2 = chambres > 0
                ? $"Ce logement lumineux et bien agencé comprend {chambreStr}, " +
                  $"offrant tout le confort nécessaire pour une vie quotidienne agréable."
                : "Ce logement lumineux et bien agencé offre tout le confort nécessaire pour une vie quotidienne agréable.";

            // Phrase 3 — état + équipements
            var etatStr = etatBien switch
            {
                "Rénové"     => "entièrement rénové",
                "Bon état"   => "en bon état",
                "Etat moyen" => "à rafraîchir",
                _            => null
            };
            var p3Parts = new List<string>();
            if (etatStr != null) p3Parts.Add($"Bien {etatStr}");
            if (equipements.Count > 0) p3Parts.Add($"il bénéficie des équipements suivants : {string.Join(", ", equipements)}");
            var p3 = p3Parts.Count > 0 ? string.Join(", ", p3Parts) + "." : string.Empty;

            // Phrase 4 — accroche
            var p4 = "Situé dans un quartier calme et bien desservi — " +
                     "une opportunité à saisir pour familles ou investisseurs.";

            return string.Join(" ", new[] { p1, p2, p3, p4 }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        private static readonly Dictionary<string, int> _pricePerM2 = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Tunis"] = 3800,
            ["Ariana"] = 3200,
            ["Ben Arous"] = 3000,
            ["Manouba"] = 2600,
            ["Sousse"] = 2800,
            ["Monastir"] = 2700,
            ["Nabeul"] = 2500,
            ["Sfax"] = 2200,
            ["Bizerte"] = 2000,
            ["Mahdia"] = 1900,
            ["Hammamet"] = 2400
        };

        private static int CalculateMockPrice(PredictPriceRequest r)
        {
            var pricePerM2 = _pricePerM2.GetValueOrDefault(r.Gouvernorat, 1600);
            var base_ = r.SurfaceM2 * pricePerM2;
            base_ += r.NbChambres * 15_000;

            if (r.HasPiscine) base_ += 80_000;
            if (r.HasJardin) base_ += 40_000;
            if (r.HasGarage) base_ += 25_000;
            if (r.HasParking) base_ += 15_000;
            if (r.HasTerrasse) base_ += 20_000;
            if (r.HasAscenseur) base_ += 10_000;
            if (r.HasClimatisation) base_ += 8_000;
            if (r.HasChauffageCentral) base_ += 8_000;
            if (r.HasBalcon) base_ += 5_000;

            // Round to nearest 1000
            return (int)Math.Round(base_ / 1000.0) * 1000;
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

        private static bool TryExtractPrice(JsonElement doc, out int value)
        {
            if (TryExtractInt(doc, "prix_predit_tnd", out value))
                return true;

            return TryExtractInt(doc, "prix_tnd", out value);
        }

        private static bool TryExtractInt(JsonElement doc, string propertyName, out int value)
        {
            value = 0;
            if (!doc.TryGetProperty(propertyName, out var prop))
                return false;

            if (prop.ValueKind == JsonValueKind.Number)
            {
                if (prop.TryGetInt32(out value))
                    return true;
                if (prop.TryGetDouble(out var doubleValue))
                {
                    value = (int)Math.Round(doubleValue);
                    return true;
                }
            }

            return false;
        }

        // DTOs for image analysis microservice responses
        private record VerifyPhotoResult(bool Valid, string? Reason);
        private record GenerateDescriptionResult(string? Description);
    }
}
