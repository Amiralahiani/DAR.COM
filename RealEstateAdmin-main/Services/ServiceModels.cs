using System.ComponentModel.DataAnnotations;
using RealEstateAdmin.Models;

namespace RealEstateAdmin.Services
{
    public enum ServiceErrorCode
    {
        None = 0,
        NotFound = 1,
        Forbidden = 2,
        BadRequest = 3,
        Unauthorized = 4,
        Conflict = 5,
        Validation = 6
    }

    public class ServiceResult
    {
        public bool Success { get; init; }
        public ServiceErrorCode ErrorCode { get; init; } = ServiceErrorCode.None;
        public string? Message { get; init; }

        public static ServiceResult Ok(string? message = null)
        {
            return new ServiceResult
            {
                Success = true,
                Message = message
            };
        }

        public static ServiceResult Fail(ServiceErrorCode code, string? message = null)
        {
            return new ServiceResult
            {
                Success = false,
                ErrorCode = code,
                Message = message
            };
        }
    }

    public class ServiceResult<T> : ServiceResult
    {
        public T? Data { get; init; }

        public static ServiceResult<T> Ok(T data, string? message = null)
        {
            return new ServiceResult<T>
            {
                Success = true,
                Data = data,
                Message = message
            };
        }

        public new static ServiceResult<T> Fail(ServiceErrorCode code, string? message = null)
        {
            return new ServiceResult<T>
            {
                Success = false,
                ErrorCode = code,
                Message = message
            };
        }
    }

    public class ShopFilter
    {
        public string? Titre { get; set; }
        public decimal? PrixMin { get; set; }
        public decimal? PrixMax { get; set; }
        public string? Adresse { get; set; }
        public int? SurfaceMin { get; set; }
        public int? SurfaceMax { get; set; }
        public string? Statut { get; set; }
    }

    public class ShopIndexData
    {
        public IReadOnlyList<BienImmobilier> Biens { get; set; } = new List<BienImmobilier>();
        public ShopFilter Filter { get; set; } = new ShopFilter();
        public IReadOnlyList<string> Statuses { get; set; } = new List<string>();
        public IReadOnlyDictionary<int, IReadOnlyList<DateTime>> AvailableVisitSlotsByBien { get; set; }
            = new Dictionary<int, IReadOnlyList<DateTime>>();
        public IReadOnlyDictionary<int, string> AgentDisplayByBien { get; set; }
            = new Dictionary<int, string>();
    }

    public class AgendaEvent
    {
        public int MessageId { get; set; }
        public string EventType { get; set; } = string.Empty;
        public DateTime Slot { get; set; }
        public int? BienId { get; set; }
        public string BienTitre { get; set; } = string.Empty;
        public string ClientName { get; set; } = string.Empty;
        public string ClientEmail { get; set; } = string.Empty;
        public string AssigneeName { get; set; } = string.Empty;
        public string AssignmentStatus { get; set; } = string.Empty;
        public string Statut { get; set; } = string.Empty;
    }

    public class AgendaIndexData
    {
        public IReadOnlyList<AgendaEvent> UpcomingEvents { get; set; } = new List<AgendaEvent>();
        public IReadOnlyList<AgendaEvent> TodayEvents { get; set; } = new List<AgendaEvent>();
        public IReadOnlyList<AgendaEvent> PastEvents { get; set; } = new List<AgendaEvent>();
        public int TotalUpcoming { get; set; }
        public int TotalToday { get; set; }
    }

    public class AgendaEventDetails
    {
        public int MessageId { get; set; }
        public string EventType { get; set; } = string.Empty;
        public DateTime Slot { get; set; }
        public int? BienId { get; set; }
        public string BienTitre { get; set; } = string.Empty;
        public string ClientName { get; set; } = string.Empty;
        public string ClientEmail { get; set; } = string.Empty;
        public string AssigneeName { get; set; } = string.Empty;
        public string AssignmentStatus { get; set; } = string.Empty;
        public string Statut { get; set; } = string.Empty;
        public string DemandeTexte { get; set; } = string.Empty;
        public DateTime DateCreation { get; set; }
        public DateTime? DateTraitement { get; set; }
    }

    public class BienFilter
    {
        public string? Titre { get; set; }
        public decimal? PrixMin { get; set; }
        public decimal? PrixMax { get; set; }
        public int? SurfaceMin { get; set; }
        public string? TypeTransaction { get; set; }
        public string? StatutCommercial { get; set; }
        public string? PublicationStatus { get; set; }
    }

    public class BienIndexData
    {
        public IReadOnlyList<BienImmobilier> Biens { get; set; } = new List<BienImmobilier>();
        public BienFilter Filter { get; set; } = new BienFilter();
        public IReadOnlyList<string> TypeOptions { get; set; } = new List<string>();
        public IReadOnlyList<string> CommercialStatusOptions { get; set; } = new List<string>();
        public IReadOnlyList<string> PublicationStatusOptions { get; set; } = new List<string>();
        public bool IsAdmin { get; set; }
    }

    public class SalesFilter
    {
        public string? PaymentMethod { get; set; }
        public string? PaymentStatus { get; set; }
    }

    public class SalesLookupOption
    {
        public string Value { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
    }

    public class SalesCreateInput
    {
        [Display(Name = "Bien immobilier")]
        [Range(1, int.MaxValue, ErrorMessage = "Veuillez sélectionner un bien.")]
        public int BienImmobilierId { get; set; }

        [Display(Name = "Acheteur")]
        [Required(ErrorMessage = "Veuillez sélectionner l'acheteur.")]
        public string? BuyerId { get; set; }

        [Display(Name = "Vendeur")]
        public string? SellerId { get; set; }

        [Display(Name = "Montant")]
        [Range(typeof(decimal), "0.01", "79228162514264337593543950335", ErrorMessage = "Le montant doit être supérieur à 0.")]
        public decimal Amount { get; set; }

        [Display(Name = "Mode de paiement")]
        [Required(ErrorMessage = "Le mode de paiement est obligatoire.")]
        public string PaymentMethod { get; set; } = "Virement";

        [Display(Name = "Statut paiement")]
        [Required(ErrorMessage = "Le statut de paiement est obligatoire.")]
        public string PaymentStatus { get; set; } = "En attente";

        [Display(Name = "Statut transaction")]
        [Required(ErrorMessage = "Le statut de transaction est obligatoire.")]
        public string TransactionStatus { get; set; } = "Finalisée";

        [Display(Name = "Conditions du contrat")]
        [Required(ErrorMessage = "Les conditions du contrat sont obligatoires.")]
        [StringLength(2000, ErrorMessage = "Les conditions du contrat ne peuvent pas dépasser 2000 caractères.")]
        public string? ConditionsPaiement { get; set; }

        [Display(Name = "Notes")]
        [StringLength(1000, ErrorMessage = "Les notes ne peuvent pas dépasser 1000 caractères.")]
        public string? Notes { get; set; }
    }

    public class SalesCreateData
    {
        public SalesCreateInput Input { get; set; } = new SalesCreateInput();
        public IReadOnlyList<SalesLookupOption> Biens { get; set; } = new List<SalesLookupOption>();
        public IReadOnlyList<SalesLookupOption> Users { get; set; } = new List<SalesLookupOption>();
        public IReadOnlyList<string> PaymentMethods { get; set; } = new List<string>();
        public IReadOnlyList<string> PaymentStatuses { get; set; } = new List<string>();
        public IReadOnlyList<string> TransactionStatuses { get; set; } = new List<string>();
    }

    public class SalesIndexData
    {
        public IReadOnlyList<SaleTransaction> Sales { get; set; } = new List<SaleTransaction>();
        public SalesFilter Filter { get; set; } = new SalesFilter();
        public IReadOnlyList<string> PaymentMethods { get; set; } = new List<string>();
        public IReadOnlyList<string> PaymentStatuses { get; set; } = new List<string>();
        public int TotalSales { get; set; }
        public string TotalAmount { get; set; } = "0.00";
        public string PaidAmount { get; set; } = "0.00";
    }

    public class DashboardZoneStat
    {
        public string Zone { get; set; } = string.Empty;
        public int Count { get; set; }
        public decimal AvgPrice { get; set; }
        public double AvgPricePerM2 { get; set; }
    }

    public class DashboardRevenueByMethod
    {
        public string Method { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }

    public class DashboardData
    {
        public bool IsAdmin { get; set; }
        public int TotalBiens { get; set; }
        public int TotalUtilisateurs { get; set; }
        public int TotalMessages { get; set; }
        public IReadOnlyList<object> BiensParPrix { get; set; } = new List<object>();
        public IReadOnlyList<DashboardZoneStat> ZoneStats { get; set; } = new List<DashboardZoneStat>();
        public string TopZone { get; set; } = "-";
        public string AvgPrice { get; set; } = "-";
        public string AvgPricePerM2 { get; set; } = "-";
        public int ZoneCount { get; set; }

        public int TotalTransactions { get; set; }
        public int PaidTransactions { get; set; }
        public string TotalRevenue { get; set; } = "-";
        public string ConversionRate { get; set; } = "-";
        public IReadOnlyList<DashboardRevenueByMethod> RevenueByMethod { get; set; } = new List<DashboardRevenueByMethod>();

        public int MySales { get; set; }
        public int MyPurchases { get; set; }
        public string MyTransactionAmount { get; set; } = "-";
    }
}
