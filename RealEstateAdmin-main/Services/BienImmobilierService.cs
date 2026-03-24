using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RealEstateAdmin.Data;
using RealEstateAdmin.Models;

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

        public BienImmobilierService(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            PdfExportService pdfExportService,
            IAuditLogService auditLogService)
        {
            _context = context;
            _userManager = userManager;
            _pdfExportService = pdfExportService;
            _auditLogService = auditLogService;
        }

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
            sb.AppendLine("Id;Titre;Prix;Adresse;Surface;Pieces;Type;StatutCommercial;Publication;Proprietaire");
            foreach (var b in biens)
            {
                sb.AppendLine($"{b.Id};\"{b.Titre}\";{b.Prix};\"{b.Adresse}\";{b.Surface};{b.NombrePieces};{b.TypeTransaction};{b.StatutCommercial};{b.PublicationStatus};\"{b.User?.UserName}\"");
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

            _context.Add(bienImmobilier);
            await _context.SaveChangesAsync();
            await SaveImagesAsync(bienImmobilier);
            await _auditLogService.LogAsync(currentUserId, "Create", "BienImmobilier", bienImmobilier.Id, $"Création du bien '{bienImmobilier.Titre}'");

            return ServiceResult.Ok();
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

            try
            {
                bienImmobilier.UserId = existingBien.UserId;
                bienImmobilier.TypeTransaction = NormalizeTypeTransaction(bienImmobilier.TypeTransaction, existingBien.TypeTransaction);
                bienImmobilier.PublicationValidatedByAdminId = existingBien.PublicationValidatedByAdminId;
                bienImmobilier.PublicationValidatedAt = existingBien.PublicationValidatedAt;

                if (!hasAdminAccess)
                {
                    bienImmobilier.IsPublished = existingBien.IsPublished;
                    bienImmobilier.PublicationStatus = existingBien.PublicationStatus;
                    bienImmobilier.StatutCommercial = existingBien.StatutCommercial;
                }
                else
                {
                    bienImmobilier.PublicationStatus = NormalizePublicationStatus(bienImmobilier.PublicationStatus, existingBien.PublicationStatus);
                    bienImmobilier.StatutCommercial = NormalizeCommercialStatus(bienImmobilier.StatutCommercial, existingBien.StatutCommercial);
                    bienImmobilier.IsPublished = string.Equals(bienImmobilier.PublicationStatus, "Publié", StringComparison.OrdinalIgnoreCase);
                    await ApplyValidatorOnPublicationDecisionAsync(bienImmobilier, currentUserId);
                }

                _context.Update(bienImmobilier);
                await _context.SaveChangesAsync();
                await SaveImagesAsync(bienImmobilier, replace: true);
                await _auditLogService.LogAsync(currentUserId, "Edit", "BienImmobilier", bienImmobilier.Id, $"Modification du bien '{bienImmobilier.Titre}'");
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
