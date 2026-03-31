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

        // ─── Nouvelles méthodes ───────────────────────────────────────────────────

        public async Task<BienDetailsDto?> GetBienDetailsAsync(int bienId)
        {
            var bien = await _context.Biens
                .Include(b => b.User)
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == bienId);

            if (bien == null) return null;

            return new BienDetailsDto
            {
                Id = bien.Id,
                Titre = bien.Titre,
                Adresse = bien.Adresse,
                Surface = bien.Surface,
                Prix = bien.Prix,
                VendeurNom = bien.User?.Nom ?? bien.User?.Email,
                VendeurId = bien.UserId,
                AgentId = bien.PublicationValidatedByAdminId,
                StatutCommercial = bien.StatutCommercial
            };
        }

        public async Task<ServiceResult<int>> CreateContratAsync(int saleTransactionId, string? conditionsPaiement, string? actorUserId)
        {
            var sale = await _context.Sales
                .Include(s => s.BienImmobilier)
                    .ThenInclude(b => b!.User)
                .Include(s => s.Buyer)
                .Include(s => s.Seller)
                .Include(s => s.Agent)
                .Include(s => s.Contrat)
                .FirstOrDefaultAsync(s => s.Id == saleTransactionId);

            if (sale == null)
                return ServiceResult<int>.Fail(ServiceErrorCode.NotFound, "Transaction introuvable.");

            if (sale.Contrat != null)
                return ServiceResult<int>.Fail(ServiceErrorCode.BadRequest, "Un contrat existe déjà pour cette transaction.");

            var numero = $"CONTRAT-{DateTime.Now:yyyyMMdd}-{saleTransactionId:D4}";

            var contrat = new Contrat
            {
                SaleTransactionId = saleTransactionId,
                NumeroContrat = numero,
                DateSignature = DateTime.Today,
                ContractStatus = "Signé",
                NomAcheteur = sale.Buyer?.Nom ?? sale.Buyer?.Email,
                NomVendeur = sale.Seller?.Nom ?? sale.Seller?.Email ?? sale.BienImmobilier?.User?.Nom,
                NomAgent = sale.Agent?.Nom ?? sale.Agent?.Email,
                TitreBien = sale.BienImmobilier?.Titre,
                AdresseBien = sale.BienImmobilier?.Adresse,
                SurfaceBien = sale.BienImmobilier?.Surface,
                PrixContrat = sale.Amount,
                ConditionsPaiement = conditionsPaiement,
                DateCreation = DateTime.Now
            };

            _context.Contrats.Add(contrat);

            // Mettre le bien en cours de vente (retrait du shop)
            if (sale.BienImmobilier != null)
            {
                sale.BienImmobilier.StatutCommercial = "En cours de vente";
                sale.BienImmobilier.IsPublished = false;
                _context.Update(sale.BienImmobilier);
            }

            await _context.SaveChangesAsync();

            await _auditLogService.LogAsync(actorUserId, "Create", "Contrat", contrat.Id,
                $"Contrat {numero} créé pour la transaction #{saleTransactionId} — '{contrat.TitreBien}'.");

            return ServiceResult<int>.Ok(contrat.Id, $"Contrat {numero} généré avec succès. Le bien est maintenant En cours de vente.");
        }

        public async Task<ServiceResult> ExecuteContratAsync(int contratId, string actorUserId)
        {
            var contrat = await _context.Contrats
                .Include(c => c.SaleTransaction)
                    .ThenInclude(s => s!.BienImmobilier)
                .FirstOrDefaultAsync(c => c.Id == contratId);

            if (contrat == null)
                return ServiceResult.Fail(ServiceErrorCode.NotFound, "Contrat introuvable.");

            if (contrat.ContractStatus == "Exécuté")
                return ServiceResult.Fail(ServiceErrorCode.BadRequest, "Ce contrat est déjà exécuté.");

            if (contrat.ContractStatus == "Annulé")
                return ServiceResult.Fail(ServiceErrorCode.BadRequest, "Impossible d'exécuter un contrat annulé.");

            contrat.ContractStatus = "Exécuté";
            contrat.ExecutePar = actorUserId;
            _context.Update(contrat);

            // Marquer le bien comme vendu
            var bien = contrat.SaleTransaction?.BienImmobilier;
            if (bien != null)
            {
                bien.StatutCommercial = "Vendu";
                bien.IsPublished = false;
                _context.Update(bien);
            }

            // Mettre à jour le statut de transaction
            if (contrat.SaleTransaction != null)
            {
                contrat.SaleTransaction.TransactionStatus = "Finalisée";
                _context.Update(contrat.SaleTransaction);
            }

            await _context.SaveChangesAsync();

            await _auditLogService.LogAsync(actorUserId, "Execute", "Contrat", contratId,
                $"Contrat {contrat.NumeroContrat} exécuté — bien '{bien?.Titre}' marqué comme Vendu.");

            return ServiceResult.Ok("Contrat exécuté. Le bien est maintenant marqué comme Vendu.");
        }

        public async Task<ServiceResult> AddVersementAsync(int saleId, decimal montant, string modePaiement, string? note, string actorUserId)
        {
            var sale = await _context.Sales
                .Include(s => s.Versements)
                .Include(s => s.Contrat)
                .Include(s => s.BienImmobilier)
                .FirstOrDefaultAsync(s => s.Id == saleId);

            if (sale == null)
                return ServiceResult.Fail(ServiceErrorCode.NotFound, "Transaction introuvable.");

            // Règles métier
            if (sale.StatutPaiementDetaille == "Complet")
                return ServiceResult.Fail(ServiceErrorCode.BadRequest, "Paiement déjà soldé. Aucun versement supplémentaire n'est possible.");

            if (sale.Contrat?.ContractStatus == "Annulé")
                return ServiceResult.Fail(ServiceErrorCode.BadRequest, "Contrat annulé. Impossible d'ajouter un versement.");

            var dejaPayé = sale.Versements.Sum(v => v.Montant);
            var total = sale.Amount;

            if (dejaPayé + montant > total)
                return ServiceResult.Fail(ServiceErrorCode.Validation,
                    $"Le versement ({montant:N2} DT) dépasse le montant restant à payer ({(total - dejaPayé):N2} DT). Versement rejeté.");

            var versement = new Versement
            {
                SaleTransactionId = saleId,
                Montant = montant,
                DateVersement = DateTime.Today,
                ModePaiement = modePaiement,
                Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
                CreatedAt = DateTime.Now,
                AjoutePar = actorUserId
            };

            _context.Versements.Add(versement);

            // Recalculer le statut
            var nouveauTotal = dejaPayé + montant;
            if (nouveauTotal >= total)
            {
                sale.StatutPaiementDetaille = "Complet";
                sale.PaymentStatus = "Payé";
                sale.PaidAt = DateTime.Now;

                // Bien vendu si paiement complet
                if (sale.BienImmobilier != null)
                {
                    sale.BienImmobilier.StatutCommercial = "Vendu";
                    sale.BienImmobilier.IsPublished = false;
                    _context.Update(sale.BienImmobilier);
                }
            }
            else
            {
                sale.StatutPaiementDetaille = "Partiel";
            }

            _context.Update(sale);
            await _context.SaveChangesAsync();

            await _auditLogService.LogAsync(actorUserId, "AddVersement", "SaleTransaction", saleId,
                $"Versement {montant:N2} DT ({modePaiement}) ajouté — statut paiement : {sale.StatutPaiementDetaille}.");

            return ServiceResult.Ok($"Versement de {montant:N2} DT enregistré. Statut : {sale.StatutPaiementDetaille}.");
        }

        public async Task<SaleTransactionDetail?> GetTransactionDetailAsync(int saleId)
        {
            var sale = await _context.Sales
                .Include(s => s.BienImmobilier)
                .Include(s => s.Buyer)
                .Include(s => s.Seller)
                .Include(s => s.Agent)
                .Include(s => s.Contrat)
                    .ThenInclude(c => c!.ExecuteParUser)
                .Include(s => s.Versements)
                    .ThenInclude(v => v.AjouteParUser)
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == saleId);

            if (sale == null) return null;

            var montantPaye = sale.Versements.Sum(v => v.Montant);
            var paymentMethods = await GetPaymentMethodsAsync();

            return new SaleTransactionDetail
            {
                Transaction = sale,
                Contrat = sale.Contrat,
                Versements = sale.Versements.OrderBy(v => v.DateVersement).ToList(),
                MontantTotal = sale.Amount,
                MontantPaye = montantPaye,
                ResteAPayer = sale.Amount - montantPaye,
                StatutPaiement = sale.StatutPaiementDetaille,
                PaymentMethods = paymentMethods
            };
        }
    }
}
