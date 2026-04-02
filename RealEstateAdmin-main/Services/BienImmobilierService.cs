using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RealEstateAdmin.Data;
using RealEstateAdmin.Models;
using System.Globalization;
using System.Text.Json;
using System.Net.Http;

namespace RealEstateAdmin.Services
{
    public class BienImmobilierService : IBienImmobilierService
    {
        private static readonly string[] InternalPublicationStatuses = { "En attente", "Publié", "Refusé" };
        private static readonly string[] InternalCommercialStatuses = { "Disponible", "Réservé", "Vendu" };
        private static readonly string[] InternalTypeTransactions = { "A Vendre", "A Louer", "Acheté" };

        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly PdfExportService _pdfExportService;
        private readonly IAuditLogService _auditLogService;
        private readonly System.Net.Http.IHttpClientFactory _httpClientFactory;

        public BienImmobilierService(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            PdfExportService pdfExportService,
            IAuditLogService auditLogService,
            System.Net.Http.IHttpClientFactory httpClientFactory)
        {
            _context = context;
            _userManager = userManager;
            _pdfExportService = pdfExportService;
            _auditLogService = auditLogService;
            _httpClientFactory = httpClientFactory;
        }

        private async Task GeocodeIfNeededAsync(BienImmobilier bien)
        {
            if (string.IsNullOrWhiteSpace(bien.Adresse) || (bien.Latitude.HasValue && bien.Longitude.HasValue))
            {
                return;
            }

            // 1) Try extract coordinates directly from the Adresse string (supports decimal, @lat,lng and DMS)
            var coords = TryParseCoordinatesFromString(bien.Adresse);
            if (coords != null)
            {
                bien.Latitude = coords.Value.lat;
                bien.Longitude = coords.Value.lng;
                return;
            }

            // 2) Fallback to Nominatim geocoding
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("RealEstateAdmin/1.0 (contact@example.com)");
                var url = "https://nominatim.openstreetmap.org/search?format=json&limit=1&q=" + System.Net.WebUtility.UrlEncode(bien.Adresse);
                var resp = await client.GetStringAsync(url);
                var arr = JsonSerializer.Deserialize<List<NominatimResult>>(resp);
                if (arr != null && arr.Count > 0)
                {
                    if (double.TryParse(arr[0].lat, NumberStyles.Any, CultureInfo.InvariantCulture, out var la)
                        && double.TryParse(arr[0].lon, NumberStyles.Any, CultureInfo.InvariantCulture, out var lo))
                    {
                        bien.Latitude = la;
                        bien.Longitude = lo;
                    }
                }
            }
            catch
            {
                // ignore geocoding errors; coordinates remain null
            }
        }

        private static (double lat, double lng)? TryParseCoordinatesFromString(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;

            input = input.Trim();

            // Pattern 1: direct decimal pair: "lat,lng" (e.g. 36.8065,10.1815)
            var decMatch = System.Text.RegularExpressions.Regex.Match(input, @"([+-]?\d{1,3}\.\d+)\s*,\s*([+-]?\d{1,3}\.\d+)");
            if (decMatch.Success)
            {
                if (double.TryParse(decMatch.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var la)
                    && double.TryParse(decMatch.Groups[2].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var lo))
                {
                    return (la, lo);
                }
            }

            // Pattern 2: Google Maps URL style @lat,lng,zoom
            var atMatch = System.Text.RegularExpressions.Regex.Match(input, @"@([+-]?\d{1,3}\.\d+),([+-]?\d{1,3}\.\d+)");
            if (atMatch.Success)
            {
                if (double.TryParse(atMatch.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var la)
                    && double.TryParse(atMatch.Groups[2].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var lo))
                {
                    return (la, lo);
                }
            }

            // Pattern 3: DMS coordinates like 36°50'58.6"N 10°10'44.7"E or variations
            // Try to capture two DMS blocks
            // Use a single verbatim string and represent a double-quote by doubling it ("" in a verbatim string)
            var dmsPattern = @"([0-9]{1,3})°\s*([0-9]{1,2})'\s*([0-9]{1,2}(?:\.\d+)?)""?\s*([NnSs])";
            var dmsMatches = System.Text.RegularExpressions.Regex.Matches(input, dmsPattern);
            if (dmsMatches.Count >= 2)
            {
                try
                {
                    double ParseDms(System.Text.RegularExpressions.Match m)
                    {
                        var deg = int.Parse(m.Groups[1].Value);
                        var min = int.Parse(m.Groups[2].Value);
                        var sec = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
                        var hemi = m.Groups[4].Value.ToUpperInvariant();
                        var dec = deg + (min / 60.0) + (sec / 3600.0);
                        if (hemi == "S" || hemi == "W") dec = -dec;
                        return dec;
                    }

                    var la = ParseDms(dmsMatches[0]);
                    var lo = ParseDms(dmsMatches[1]);
                    return (la, lo);
                }
                catch
                {
                    // ignore parse errors
                }
            }

            // Pattern 4: alternative DMS with degree symbol and N/E letters without seconds
            var simpleDms = System.Text.RegularExpressions.Regex.Match(input, @"([0-9]{1,3})°\s*([0-9]{1,2})'\s*([NnSs])\D+([0-9]{1,3})°\s*([0-9]{1,2})'\s*([EeWw])");
            if (simpleDms.Success)
            {
                try
                {
                    double ParseSimple(System.Text.RegularExpressions.GroupCollection g, int startIndex)
                    {
                        var deg = int.Parse(g[startIndex].Value);
                        var min = int.Parse(g[startIndex + 1].Value);
                        var hemi = g[startIndex + 2].Value.ToUpperInvariant();
                        var dec = deg + (min / 60.0);
                        if (hemi == "S" || hemi == "W") dec = -dec;
                        return dec;
                    }

                    var la = ParseSimple(simpleDms.Groups, 1);
                    var lo = ParseSimple(simpleDms.Groups, 4);
                    return (la, lo);
                }
                catch
                {
                }
            }

            return null;
        }

        private record NominatimResult(string lat, string lon);

        public IReadOnlyList<string> TypeTransactions => InternalTypeTransactions;
        public IReadOnlyList<string> CommercialStatuses => InternalCommercialStatuses;
        public IReadOnlyList<string> PublicationStatuses => InternalPublicationStatuses;

        public async Task<BienIndexData> GetIndexDataAsync(BienFilter filter, string? currentUserId, bool hasAdminAccess)
        {
            var biens = _context.Biens.Include(b => b.User).Include(b => b.Images).AsQueryable();

            if (!hasAdminAccess && !string.IsNullOrWhiteSpace(currentUserId))
            {
                biens = biens.Where(b => b.UserId == currentUserId);
            }

            if (!string.IsNullOrEmpty(filter.Titre))
            {
                biens = biens.Where(b => b.Titre.Contains(filter.Titre));
            }

            if (filter.PrixMin.HasValue)
            {
                biens = biens.Where(b => b.Prix >= filter.PrixMin.Value);
            }

            if (filter.PrixMax.HasValue)
            {
                biens = biens.Where(b => b.Prix <= filter.PrixMax.Value);
            }

            if (filter.SurfaceMin.HasValue)
            {
                biens = biens.Where(b => b.Surface.HasValue && b.Surface >= filter.SurfaceMin.Value);
            }

            if (!string.IsNullOrWhiteSpace(filter.TypeTransaction) && InternalTypeTransactions.Contains(filter.TypeTransaction))
            {
                biens = biens.Where(b => b.TypeTransaction == filter.TypeTransaction);
            }

            if (!string.IsNullOrWhiteSpace(filter.StatutCommercial) && InternalCommercialStatuses.Contains(filter.StatutCommercial))
            {
                biens = biens.Where(b => b.StatutCommercial == filter.StatutCommercial);
            }

            if (!string.IsNullOrWhiteSpace(filter.PublicationStatus) && InternalPublicationStatuses.Contains(filter.PublicationStatus))
            {
                biens = biens.Where(b => b.PublicationStatus == filter.PublicationStatus);
            }

            // if filter indicates to show only soldes (en solde)
            if (!string.IsNullOrWhiteSpace(filter.Solde) && filter.Solde.Equals("1"))
            {
                biens = biens.Where(b => b.DiscountPercent > 0m);
            }

            return new BienIndexData
            {
                Biens = await biens.ToListAsync(),
                Filter = filter,
                TypeOptions = InternalTypeTransactions,
                CommercialStatusOptions = InternalCommercialStatuses,
                PublicationStatusOptions = InternalPublicationStatuses,
                IsAdmin = hasAdminAccess
            };
        }

        public async Task<string> ExportCsvAsync()
        {
            var biens = await _context.Biens.Include(b => b.User).ToListAsync();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Id;Titre;Prix;Adresse;Surface;Pieces;Type;StatutCommercial;Publication;DiscountPercent;Proprietaire");
            foreach (var b in biens)
            {
                sb.AppendLine($"{b.Id};\"{b.Titre}\";{b.Prix};\"{b.Adresse}\";{b.Surface};{b.NombrePieces};{b.TypeTransaction};{b.StatutCommercial};{b.PublicationStatus};{b.DiscountPercent};\"{b.User?.UserName}\"");
            }

            return sb.ToString();
        }

        public async Task<byte[]> ExportPdfAsync()
        {
            var biens = await _context.Biens.Include(b => b.User).ToListAsync();
            return _pdfExportService.GenerateBiensPdf(biens);
        }

        public async Task<BienImmobilier?> GetDetailsAsync(int id)
        {
            return await _context.Biens
                .Include(b => b.Images)
                .FirstOrDefaultAsync(m => m.Id == id);
        }

        public async Task<ServiceResult<BienImmobilier>> GetForEditAsync(int id, string? currentUserId, bool hasAdminAccess)
        {
            var bienImmobilier = await _context.Biens.FindAsync(id);
            if (bienImmobilier == null)
            {
                return ServiceResult<BienImmobilier>.Fail(ServiceErrorCode.NotFound, "Bien introuvable.");
            }

            if (!hasAdminAccess && (string.IsNullOrWhiteSpace(currentUserId) || bienImmobilier.UserId != currentUserId))
            {
                return ServiceResult<BienImmobilier>.Fail(ServiceErrorCode.Forbidden, "Accès refusé.");
            }

            return ServiceResult<BienImmobilier>.Ok(bienImmobilier);
        }

        public async Task<ServiceResult> CreateAsync(BienImmobilier bienImmobilier, string currentUserId, bool hasAdminAccess)
        {
            var dbUser = await _userManager.Users.FirstOrDefaultAsync(u => u.Id == currentUserId);
            if (dbUser == null)
            {
                return ServiceResult.Fail(ServiceErrorCode.Validation, "Erreur: Utilisateur introuvable dans la base de données.");
            }

            bienImmobilier.UserId = dbUser.Id;
            bienImmobilier.TypeTransaction = NormalizeTypeTransaction(bienImmobilier.TypeTransaction, "A Vendre");
            bienImmobilier.StatutCommercial = NormalizeCommercialStatus(bienImmobilier.StatutCommercial, "Disponible");

            if (!hasAdminAccess)
            {
                bienImmobilier.IsPublished = false;
                bienImmobilier.PublicationStatus = "En attente";
                bienImmobilier.StatutCommercial = "Disponible";
                bienImmobilier.PublicationValidatedByAdminId = null;
                bienImmobilier.PublicationValidatedAt = null;
            }
            else
            {
                bienImmobilier.PublicationStatus = NormalizePublicationStatus(bienImmobilier.PublicationStatus, "En attente");
                bienImmobilier.IsPublished = string.Equals(bienImmobilier.PublicationStatus, "Publié", StringComparison.OrdinalIgnoreCase);
                await ApplyValidatorOnPublicationDecisionAsync(bienImmobilier, currentUserId);
            }

            // attempt server-side geocoding if coordinates not provided
            await GeocodeIfNeededAsync(bienImmobilier);

            _context.Add(bienImmobilier);
            await _context.SaveChangesAsync();
            await SaveImagesAsync(bienImmobilier);
            await _auditLogService.LogAsync(currentUserId, "Create", "BienImmobilier", bienImmobilier.Id, $"Création du bien '{bienImmobilier.Titre}'");

            return ServiceResult.Ok();
        }

        public async Task<IReadOnlyList<BienImmobilier>> GetMapDataAsync()
        {
            return await _context.Biens
                .Where(b => b.IsPublished)
                .ToListAsync();
        }

        public async Task<ServiceResult> UpdateAsync(int id, BienImmobilier bienImmobilier, string? currentUserId, bool hasAdminAccess)
        {
            if (id != bienImmobilier.Id)
            {
                return ServiceResult.Fail(ServiceErrorCode.BadRequest, "ID invalide.");
            }

            var existingBien = await _context.Biens.FindAsync(id);
            if (existingBien == null)
            {
                return ServiceResult.Fail(ServiceErrorCode.NotFound, "Bien introuvable.");
            }

            if (!hasAdminAccess && (string.IsNullOrWhiteSpace(currentUserId) || existingBien.UserId != currentUserId))
            {
                return ServiceResult.Fail(ServiceErrorCode.Forbidden, "Accès refusé.");
            }

            var ownerAssignmentResult = await EnsureOwnerAssignedAsync(existingBien, currentUserId);
            if (!ownerAssignmentResult.Success)
            {
                return ownerAssignmentResult;
            }

            try
            {
                // Map incoming values onto the tracked entity to avoid tracking conflicts
                existingBien.Titre = bienImmobilier.Titre;
                existingBien.Description = bienImmobilier.Description;
                existingBien.Prix = bienImmobilier.Prix;
                existingBien.Adresse = bienImmobilier.Adresse;
                existingBien.Surface = bienImmobilier.Surface;
                existingBien.NombrePieces = bienImmobilier.NombrePieces;
                existingBien.ImageUrl = bienImmobilier.ImageUrl;
                existingBien.ImageUrlsInput = bienImmobilier.ImageUrlsInput;
                existingBien.Latitude = bienImmobilier.Latitude;
                existingBien.Longitude = bienImmobilier.Longitude;
                existingBien.DiscountPercent = bienImmobilier.DiscountPercent;

                existingBien.TypeTransaction = NormalizeTypeTransaction(bienImmobilier.TypeTransaction, existingBien.TypeTransaction);
                existingBien.PublicationValidatedByAdminId = existingBien.PublicationValidatedByAdminId;
                existingBien.PublicationValidatedAt = existingBien.PublicationValidatedAt;

                if (!hasAdminAccess)
                {
                    // preserve existing publication/commercial state
                    existingBien.IsPublished = existingBien.IsPublished;
                    existingBien.PublicationStatus = existingBien.PublicationStatus;
                    existingBien.StatutCommercial = existingBien.StatutCommercial;
                }
                else
                {
                    existingBien.PublicationStatus = NormalizePublicationStatus(bienImmobilier.PublicationStatus, existingBien.PublicationStatus);
                    existingBien.StatutCommercial = NormalizeCommercialStatus(bienImmobilier.StatutCommercial, existingBien.StatutCommercial);
                    existingBien.IsPublished = string.Equals(existingBien.PublicationStatus, "Publié", StringComparison.OrdinalIgnoreCase);
                    await ApplyValidatorOnPublicationDecisionAsync(existingBien, currentUserId);
                }


                // attempt to geocode when address changed and coordinates missing
                await GeocodeIfNeededAsync(existingBien);

                _context.Update(existingBien);
                await _context.SaveChangesAsync();

                // Use the tracked entity for image operations and logging
                await SaveImagesAsync(existingBien, replace: true);
                await _auditLogService.LogAsync(currentUserId, "Edit", "BienImmobilier", existingBien.Id, $"Modification du bien '{existingBien.Titre}'");
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!_context.Biens.Any(e => e.Id == bienImmobilier.Id))
                {
                    return ServiceResult.Fail(ServiceErrorCode.NotFound, "Bien introuvable.");
                }

                throw;
            }

            return ServiceResult.Ok();
        }

        public async Task<ServiceResult<BienImmobilier>> GetForDeleteAsync(int id, string? currentUserId, bool hasAdminAccess)
        {
            var bienImmobilier = await _context.Biens.FirstOrDefaultAsync(m => m.Id == id);
            if (bienImmobilier == null)
            {
                return ServiceResult<BienImmobilier>.Fail(ServiceErrorCode.NotFound, "Bien introuvable.");
            }

            if (!hasAdminAccess && (string.IsNullOrWhiteSpace(currentUserId) || bienImmobilier.UserId != currentUserId))
            {
                return ServiceResult<BienImmobilier>.Fail(ServiceErrorCode.Forbidden, "Accès refusé.");
            }

            return ServiceResult<BienImmobilier>.Ok(bienImmobilier);
        }

        public async Task<ServiceResult> DeleteAsync(int id, string? currentUserId, bool hasAdminAccess)
        {
            var bienImmobilier = await _context.Biens.FindAsync(id);
            if (bienImmobilier == null)
            {
                return ServiceResult.Fail(ServiceErrorCode.NotFound, "Bien introuvable.");
            }

            if (!hasAdminAccess && (string.IsNullOrWhiteSpace(currentUserId) || bienImmobilier.UserId != currentUserId))
            {
                return ServiceResult.Fail(ServiceErrorCode.Forbidden, "Accès refusé.");
            }

            _context.Biens.Remove(bienImmobilier);
            await _context.SaveChangesAsync();
            await _auditLogService.LogAsync(currentUserId, "Delete", "BienImmobilier", bienImmobilier.Id, $"Suppression du bien '{bienImmobilier.Titre}'");
            return ServiceResult.Ok();
        }

        public async Task<ServiceResult> MettreEnVenteAsync(int id, string? currentUserId, bool hasAdminAccess)
        {
            var bienImmobilier = await _context.Biens.FindAsync(id);
            if (bienImmobilier == null)
            {
                return ServiceResult.Fail(ServiceErrorCode.NotFound, "Bien introuvable.");
            }

            if (!hasAdminAccess && (string.IsNullOrWhiteSpace(currentUserId) || bienImmobilier.UserId != currentUserId))
            {
                return ServiceResult.Fail(ServiceErrorCode.Forbidden, "Accès refusé.");
            }

            var ownerAssignmentResult = await EnsureOwnerAssignedAsync(bienImmobilier, currentUserId);
            if (!ownerAssignmentResult.Success)
            {
                return ownerAssignmentResult;
            }

            bienImmobilier.TypeTransaction = "A Vendre";
            bienImmobilier.StatutCommercial = "Disponible";
            _context.Update(bienImmobilier);
            await _context.SaveChangesAsync();
            await _auditLogService.LogAsync(currentUserId, "Status", "BienImmobilier", bienImmobilier.Id, "Remis en vente");
            return ServiceResult.Ok("Le bien a été remis en vente.");
        }

        public async Task<ServiceResult> TogglePublishAsync(int id, string? actorUserId)
        {
            var bienImmobilier = await _context.Biens.FindAsync(id);
            if (bienImmobilier == null)
            {
                return ServiceResult.Fail(ServiceErrorCode.NotFound, "Bien introuvable.");
            }

            var ownerAssignmentResult = await EnsureOwnerAssignedAsync(bienImmobilier, actorUserId);
            if (!ownerAssignmentResult.Success)
            {
                return ownerAssignmentResult;
            }

            bienImmobilier.IsPublished = !bienImmobilier.IsPublished;
            bienImmobilier.PublicationStatus = bienImmobilier.IsPublished ? "Publié" : "Refusé";
            await ApplyValidatorOnPublicationDecisionAsync(bienImmobilier, actorUserId);
            _context.Update(bienImmobilier);
            await _context.SaveChangesAsync();

            var status = bienImmobilier.IsPublished ? "Publié" : "Dépublié";
            await _auditLogService.LogAsync(actorUserId, "Publish", "BienImmobilier", bienImmobilier.Id, $"{status} : '{bienImmobilier.Titre}'");
            return ServiceResult.Ok();
        }

        public async Task<ServiceResult> SetPublicationStatusAsync(int id, string status, string? actorUserId)
        {
            var bienImmobilier = await _context.Biens.FindAsync(id);
            if (bienImmobilier == null)
            {
                return ServiceResult.Fail(ServiceErrorCode.NotFound, "Bien introuvable.");
            }

            if (!InternalPublicationStatuses.Contains(status))
            {
                return ServiceResult.Fail(ServiceErrorCode.BadRequest, "Statut de publication invalide.");
            }

            var ownerAssignmentResult = await EnsureOwnerAssignedAsync(bienImmobilier, actorUserId);
            if (!ownerAssignmentResult.Success)
            {
                return ownerAssignmentResult;
            }

            bienImmobilier.PublicationStatus = status;
            bienImmobilier.IsPublished = status == "Publié";
            await ApplyValidatorOnPublicationDecisionAsync(bienImmobilier, actorUserId);
            _context.Update(bienImmobilier);
            await _context.SaveChangesAsync();
            await _auditLogService.LogAsync(actorUserId, "Status", "BienImmobilier", bienImmobilier.Id, $"Publication: {status} - '{bienImmobilier.Titre}'");
            return ServiceResult.Ok();
        }

        public async Task<ServiceResult> SetCommercialStatusAsync(int id, string commercialStatus, string? actorUserId)
        {
            var bienImmobilier = await _context.Biens.FindAsync(id);
            if (bienImmobilier == null)
            {
                return ServiceResult.Fail(ServiceErrorCode.NotFound, "Bien introuvable.");
            }

            if (!InternalCommercialStatuses.Contains(commercialStatus))
            {
                return ServiceResult.Fail(ServiceErrorCode.BadRequest, "Statut commercial invalide.");
            }

            var ownerAssignmentResult = await EnsureOwnerAssignedAsync(bienImmobilier, actorUserId);
            if (!ownerAssignmentResult.Success)
            {
                return ownerAssignmentResult;
            }

            bienImmobilier.StatutCommercial = commercialStatus;
            if (commercialStatus == "Vendu")
            {
                bienImmobilier.TypeTransaction = "Acheté";
            }
            else if (bienImmobilier.TypeTransaction == "Acheté")
            {
                bienImmobilier.TypeTransaction = "A Vendre";
            }

            _context.Update(bienImmobilier);
            await _context.SaveChangesAsync();
            await _auditLogService.LogAsync(actorUserId, "Status", "BienImmobilier", bienImmobilier.Id, $"Statut commercial: {commercialStatus} - '{bienImmobilier.Titre}'");
            return ServiceResult.Ok();
        }

        private async Task<ServiceResult> EnsureOwnerAssignedAsync(BienImmobilier bienImmobilier, string? fallbackOwnerUserId)
        {
            if (!string.IsNullOrWhiteSpace(bienImmobilier.UserId))
            {
                var ownerExists = await _userManager.Users.AnyAsync(u => u.Id == bienImmobilier.UserId);
                if (ownerExists)
                {
                    return ServiceResult.Ok();
                }
            }

            if (!string.IsNullOrWhiteSpace(bienImmobilier.PublicationValidatedByAdminId))
            {
                var validatorExists = await _userManager.Users.AnyAsync(u => u.Id == bienImmobilier.PublicationValidatedByAdminId);
                if (validatorExists)
                {
                    bienImmobilier.UserId = bienImmobilier.PublicationValidatedByAdminId;
                    return ServiceResult.Ok();
                }
            }

            if (!string.IsNullOrWhiteSpace(fallbackOwnerUserId))
            {
                var fallbackExists = await _userManager.Users.AnyAsync(u => u.Id == fallbackOwnerUserId);
                if (fallbackExists)
                {
                    bienImmobilier.UserId = fallbackOwnerUserId;
                    return ServiceResult.Ok();
                }
            }

            return ServiceResult.Fail(
                ServiceErrorCode.Validation,
                "Ce bien doit avoir un vendeur (propriétaire) valide avant enregistrement.");
        }

        private async Task ApplyValidatorOnPublicationDecisionAsync(BienImmobilier bienImmobilier, string? actorUserId)
        {
            if (string.Equals(bienImmobilier.PublicationStatus, "En attente", StringComparison.OrdinalIgnoreCase))
            {
                bienImmobilier.PublicationValidatedByAdminId = null;
                bienImmobilier.PublicationValidatedAt = null;
                return;
            }

            if (!string.Equals(bienImmobilier.PublicationStatus, "Publié", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(bienImmobilier.PublicationStatus, "Refusé", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return;
            }

            if (!await IsStandardUserOwnerAsync(bienImmobilier.UserId))
            {
                return;
            }

            bienImmobilier.PublicationValidatedByAdminId = actorUserId;
            bienImmobilier.PublicationValidatedAt = DateTime.Now;
        }

        private async Task<bool> IsStandardUserOwnerAsync(string? ownerUserId)
        {
            if (string.IsNullOrWhiteSpace(ownerUserId))
            {
                return false;
            }

            var owner = await _userManager.Users.FirstOrDefaultAsync(u => u.Id == ownerUserId);
            if (owner == null)
            {
                return false;
            }

            var isSuperAdmin = await _userManager.IsInRoleAsync(owner, "SuperAdmin");
            if (isSuperAdmin)
            {
                return false;
            }

            var isAdmin = await _userManager.IsInRoleAsync(owner, "Admin");
            return !isAdmin;
        }

        private static string NormalizeTypeTransaction(string? type, string? defaultType)
        {
            var normalized = type?.Trim();
            if (string.Equals(normalized, "Achete", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "Acheté";
            }

            return InternalTypeTransactions.Contains(normalized ?? string.Empty) ? normalized! : (defaultType ?? "A Vendre");
        }

        private static string NormalizePublicationStatus(string? status, string? defaultStatus)
        {
            var normalized = status?.Trim();
            return InternalPublicationStatuses.Contains(normalized ?? string.Empty) ? normalized! : (defaultStatus ?? "En attente");
        }

        private static string NormalizeCommercialStatus(string? status, string? defaultStatus)
        {
            var normalized = status?.Trim();
            return InternalCommercialStatuses.Contains(normalized ?? string.Empty) ? normalized! : (defaultStatus ?? "Disponible");
        }

        private async Task SaveImagesAsync(BienImmobilier bienImmobilier, bool replace = false)
        {
            var urls = ParseImageUrls(bienImmobilier.ImageUrlsInput);
            if (urls.Count == 0)
            {
                return;
            }

            if (replace)
            {
                var existing = _context.BienImages.Where(i => i.BienImmobilierId == bienImmobilier.Id);
                _context.BienImages.RemoveRange(existing);
                await _context.SaveChangesAsync();
            }

            foreach (var url in urls)
            {
                _context.BienImages.Add(new BienImage
                {
                    BienImmobilierId = bienImmobilier.Id,
                    Url = url
                });
            }

            if (string.IsNullOrWhiteSpace(bienImmobilier.ImageUrl))
            {
                bienImmobilier.ImageUrl = urls.FirstOrDefault();
                _context.Update(bienImmobilier);
            }

            await _context.SaveChangesAsync();
        }

        private static List<string> ParseImageUrls(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return new List<string>();
            }

            return input
                .Split(new[] { '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(u => u.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToList();
        }
    }
}
