using RealEstateAdmin.Models;

namespace RealEstateAdmin.Services
{
    public interface IVenteService
    {
        Task<SalesIndexData> GetIndexDataAsync(SalesFilter filter);
        Task<SalesCreateData> GetCreateDataAsync(SalesCreateInput? input = null);
        Task<ServiceResult<int>> CreateManualAsync(SalesCreateInput input, string? actorUserId);
        Task<ServiceResult> UpdatePaymentAsync(int id, string paymentMethod, string paymentStatus, string? actorUserId);
        Task<string> ExportCsvAsync();
        Task<byte[]> ExportPdfAsync();

        // Nouveau : récupérer les détails d'un bien pour auto-fill AJAX
        Task<BienDetailsDto?> GetBienDetailsAsync(int bienId);

        // Nouveau : gestion des contrats
        Task<ServiceResult<int>> CreateContratAsync(int saleTransactionId, string? conditionsPaiement, string? actorUserId);
        Task<ServiceResult> ExecuteContratAsync(int contratId, string actorUserId);

        // Nouveau : gestion des versements
        Task<ServiceResult> AddVersementAsync(int saleId, decimal montant, string modePaiement, string? note, string actorUserId);

        // Nouveau : vue détail d'une transaction avec contrat et versements
        Task<SaleTransactionDetail?> GetTransactionDetailAsync(int saleId);
    }

    // DTO pour l'auto-fill AJAX
    public class BienDetailsDto
    {
        public int Id { get; set; }
        public string Titre { get; set; } = string.Empty;
        public string? Adresse { get; set; }
        public int? Surface { get; set; }
        public decimal Prix { get; set; }
        public string? VendeurNom { get; set; }
        public string? VendeurId { get; set; }
        public string? AgentId { get; set; }
        public string? StatutCommercial { get; set; }
    }

    // DTO pour la vue détail d'une transaction
    public class SaleTransactionDetail
    {
        public SaleTransaction Transaction { get; set; } = null!;
        public Contrat? Contrat { get; set; }
        public List<Versement> Versements { get; set; } = new();
        public decimal MontantTotal { get; set; }
        public decimal MontantPaye { get; set; }
        public decimal ResteAPayer { get; set; }
        public string StatutPaiement { get; set; } = "En attente";
        public string[] PaymentMethods { get; set; } = Array.Empty<string>();
    }
}
