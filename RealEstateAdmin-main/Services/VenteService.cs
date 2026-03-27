using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RealEstateAdmin.Data;
using RealEstateAdmin.Models;

namespace RealEstateAdmin.Services
{
    public class VenteService : IVenteService
    {
        private static readonly string[] DefaultPaymentMethods = { "Virement", "Chèque", "Crédit", "Espèces" };
        private static readonly string[] PaymentStatuses = { "En attente", "Payé", "Refusé" };
        private static readonly string[] TransactionStatuses = { "En cours", "Finalisée", "Annulée" };

        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly PdfExportService _pdfExportService;
        private readonly IAuditLogService _auditLogService;

        public VenteService(
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

        // Payment methods are now configurable via the database settings table if present.
        // Fallback to default list defined above.
        private async Task<string[]> GetPaymentMethodsAsync()
        {
            try
            {
                // If there's a simple Settings table, try to read a semicolon separated value
                // Try reading from AppSettings table if present
                var setting = await _context.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == "PaymentMethods");
                if (setting != null && !string.IsNullOrWhiteSpace(setting.Value))
                {
                    return setting.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                }
            }
            catch
            {
                // ignore and fallback
            }

            return DefaultPaymentMethods;
        }

        public async Task<SalesIndexData> GetIndexDataAsync(SalesFilter filter)
        {
            var query = _context.Sales
                .Include(s => s.BienImmobilier)
                .Include(s => s.Buyer)
                .Include(s => s.Seller)
                .AsQueryable();

            var paymentMethods = await GetPaymentMethodsAsync();

            if (!string.IsNullOrWhiteSpace(filter.PaymentMethod) && paymentMethods.Contains(filter.PaymentMethod))
            {
                query = query.Where(s => s.PaymentMethod == filter.PaymentMethod);
            }

            if (!string.IsNullOrWhiteSpace(filter.PaymentStatus) && PaymentStatuses.Contains(filter.PaymentStatus))
            {
                query = query.Where(s => s.PaymentStatus == filter.PaymentStatus);
            }

            var sales = await query.OrderByDescending(s => s.CreatedAt).ToListAsync();

            return new SalesIndexData
            {
                Sales = sales,
                Filter = filter,
                PaymentMethods = paymentMethods,
                PaymentStatuses = PaymentStatuses,
                TotalSales = sales.Count,
                TotalAmount = sales.Sum(s => s.Amount).ToString("N2"),
                PaidAmount = sales.Where(s => s.PaymentStatus == "Payé").Sum(s => s.Amount).ToString("N2")
            };
        }

        public async Task<SalesCreateData> GetCreateDataAsync(SalesCreateInput? input = null)
        {
            var bienRows = await _context.Biens
                .OrderBy(b => b.Titre)
                .ToListAsync();

            var biens = bienRows
                .Select(b => new SalesLookupOption
                {
                    Value = b.Id.ToString(),
                    Label = $"{b.Titre} ({b.Prix:N2} DT)"
                })
                .ToList();

            var usersRows = await _userManager.Users
                .OrderBy(u => u.Email)
                .ToListAsync();

            var users = usersRows
                .Select(u => new SalesLookupOption
                {
                    Value = u.Id,
                    Label = BuildUserLabel(u)
                })
                .ToList();

            var paymentMethods = await GetPaymentMethodsAsync();

            return new SalesCreateData
            {
                Input = input ?? new SalesCreateInput(),
                Biens = biens,
                Users = users,
                PaymentMethods = paymentMethods,
                PaymentStatuses = PaymentStatuses,
                TransactionStatuses = TransactionStatuses
            };
        }

        public async Task<ServiceResult<int>> CreateManualAsync(SalesCreateInput input, string? actorUserId)
        {
            if (input.Amount <= 0)
            {
                return ServiceResult<int>.Fail(ServiceErrorCode.Validation, "Le montant doit être supérieur à 0.");
            }

            var paymentMethods = await GetPaymentMethodsAsync();

            if (!paymentMethods.Contains(input.PaymentMethod) || !PaymentStatuses.Contains(input.PaymentStatus))
            {
                return ServiceResult<int>.Fail(ServiceErrorCode.BadRequest, "Mode ou statut de paiement invalide.");
            }

            if (!TransactionStatuses.Contains(input.TransactionStatus))
            {
                return ServiceResult<int>.Fail(ServiceErrorCode.BadRequest, "Statut de transaction invalide.");
            }

            if (!string.IsNullOrWhiteSpace(input.BuyerId) && !string.IsNullOrWhiteSpace(input.SellerId)
                && string.Equals(input.BuyerId, input.SellerId, StringComparison.OrdinalIgnoreCase))
            {
                return ServiceResult<int>.Fail(ServiceErrorCode.BadRequest, "Acheteur et vendeur doivent être différents.");
            }

            var bien = await _context.Biens.FirstOrDefaultAsync(b => b.Id == input.BienImmobilierId);
            if (bien == null)
            {
                return ServiceResult<int>.Fail(ServiceErrorCode.NotFound, "Bien introuvable.");
            }

            if (!string.IsNullOrWhiteSpace(input.BuyerId))
            {
                var buyerExists = await _userManager.Users.AnyAsync(u => u.Id == input.BuyerId);
                if (!buyerExists)
                {
                    return ServiceResult<int>.Fail(ServiceErrorCode.BadRequest, "Acheteur invalide.");
                }
            }

            if (!string.IsNullOrWhiteSpace(input.SellerId))
            {
                var sellerExists = await _userManager.Users.AnyAsync(u => u.Id == input.SellerId);
                if (!sellerExists)
                {
                    return ServiceResult<int>.Fail(ServiceErrorCode.BadRequest, "Vendeur invalide.");
                }
            }

            var now = DateTime.Now;
            var sale = new SaleTransaction
            {
                BienImmobilierId = input.BienImmobilierId,
                BuyerId = string.IsNullOrWhiteSpace(input.BuyerId) ? null : input.BuyerId,
                SellerId = string.IsNullOrWhiteSpace(input.SellerId) ? null : input.SellerId,
                AgentId = string.IsNullOrWhiteSpace(input.AgentId) ? null : input.AgentId,
                Amount = input.Amount,
                PaymentMethod = input.PaymentMethod,
                PaymentStatus = input.PaymentStatus,
                TransactionStatus = input.TransactionStatus,
                CreatedAt = now,
                PaidAt = input.PaymentStatus == "Payé" ? now : null,
                Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim()
            };

            _context.Sales.Add(sale);
            await _context.SaveChangesAsync();

            await _auditLogService.LogAsync(
                actorUserId,
                "Create",
                "SaleTransaction",
                sale.Id,
                $"Saisie manuelle transaction pour '{bien.Titre}' ({sale.Amount:N2} DT).");

            return ServiceResult<int>.Ok(sale.Id, "Transaction créée avec succès.");
        }

        public async Task<ServiceResult> UpdatePaymentAsync(int id, string paymentMethod, string paymentStatus, string? actorUserId)
        {
            var sale = await _context.Sales.Include(s => s.BienImmobilier).FirstOrDefaultAsync(s => s.Id == id);
            if (sale == null)
            {
                return ServiceResult.Fail(ServiceErrorCode.NotFound, "Transaction introuvable.");
            }

            var paymentMethods = await GetPaymentMethodsAsync();

            if (!paymentMethods.Contains(paymentMethod) || !PaymentStatuses.Contains(paymentStatus))
            {
                return ServiceResult.Fail(ServiceErrorCode.BadRequest, "Modalité ou statut de paiement invalide.");
            }

            sale.PaymentMethod = paymentMethod;
            sale.PaymentStatus = paymentStatus;
            sale.PaidAt = paymentStatus == "Payé" ? DateTime.Now : null;

            // If payment completed and we have a buyer, transfer ownership of the property
            // so it appears in the buyer's "Mes biens" and is removed from the seller.
            if (sale.PaymentStatus == "Payé" && !string.IsNullOrWhiteSpace(sale.BuyerId) && sale.BienImmobilier != null)
            {
                try
                {
                    // Change owner to buyer
                    sale.BienImmobilier.UserId = sale.BuyerId;
                    // Mark commercial status and type appropriately
                    sale.BienImmobilier.StatutCommercial = "Vendu";
                    sale.BienImmobilier.TypeTransaction = "Acheté";
                    // Optionally unpublish after sale
                    sale.BienImmobilier.IsPublished = false;
                    // Update both sale and bien in same transaction
                    _context.Update(sale);
                    _context.Update(sale.BienImmobilier);
                }
                catch
                {
                    // ignore transfer errors, still persist payment status
                    _context.Update(sale);
                }
            }
            else
            {
                _context.Update(sale);
            }

            await _context.SaveChangesAsync();

            await _auditLogService.LogAsync(
                actorUserId,
                "Update",
                "SaleTransaction",
                sale.Id,
                $"Paiement mis à jour pour '{sale.BienImmobilier?.Titre}': {paymentMethod} / {paymentStatus}");

            return ServiceResult.Ok("Paiement mis à jour avec succès.");
        }

        public async Task<string> ExportCsvAsync()
        {
            var sales = await _context.Sales
                .Include(s => s.BienImmobilier)
                .Include(s => s.Buyer)
                .Include(s => s.Seller)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Id;Bien;Acheteur;Vendeur;Montant;Paiement;StatutPaiement;Date");
            foreach (var s in sales)
            {
                sb.AppendLine($"{s.Id};\"{s.BienImmobilier?.Titre}\";\"{s.Buyer?.Email}\";\"{s.Seller?.Email}\";{s.Amount};\"{s.PaymentMethod}\";\"{s.PaymentStatus}\";{s.CreatedAt:dd/MM/yyyy HH:mm}");
            }

            return sb.ToString();
        }

        public async Task<byte[]> ExportPdfAsync()
        {
            var sales = await _context.Sales
                .Include(s => s.BienImmobilier)
                .Include(s => s.Buyer)
                .Include(s => s.Seller)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();

            return _pdfExportService.GenerateSalesPdf(sales);
        }

        private static string BuildUserLabel(ApplicationUser user)
        {
            if (!string.IsNullOrWhiteSpace(user.Nom) && !string.IsNullOrWhiteSpace(user.Email))
            {
                return $"{user.Nom} ({user.Email})";
            }

            if (!string.IsNullOrWhiteSpace(user.Email))
            {
                return user.Email;
            }

            if (!string.IsNullOrWhiteSpace(user.UserName))
            {
                return user.UserName;
            }

            return user.Id;
        }
    }
}
