using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace RealEstateAdmin.Models
{
    public class SaleTransaction
    {
        public int Id { get; set; }

        [Required]
        public int BienImmobilierId { get; set; }

        [ForeignKey("BienImmobilierId")]
        public BienImmobilier? BienImmobilier { get; set; }

        [StringLength(450)]
        public string? BuyerId { get; set; }

        [ForeignKey("BuyerId")]
        public UserReference? Buyer { get; set; }

        [StringLength(450)]
        public string? SellerId { get; set; }

        [ForeignKey("SellerId")]
        public UserReference? Seller { get; set; }

        [Range(0, double.MaxValue)]
        public decimal Amount { get; set; }

        [Required]
        [StringLength(50)]
        public string PaymentMethod { get; set; } = "Virement";

        [StringLength(50)]
        public string PaymentStatus { get; set; } = "En attente";

        [StringLength(50)]
        public string TransactionStatus { get; set; } = "Finalisée";

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime? PaidAt { get; set; }

        // Agent assigned to this transaction (optional)
        [StringLength(450)]
        public string? AgentId { get; set; }

        [ForeignKey("AgentId")]
        public UserReference? Agent { get; set; }

        [Display(Name = "Nombre de visites")]
        public int NbVisites { get; set; } = 0;

        [StringLength(1000)]
        public string? Notes { get; set; }

        // Statut détaillé du paiement : En attente / Partiel / Complet
        [StringLength(50)]
        [Display(Name = "Statut paiement détaillé")]
        public string StatutPaiementDetaille { get; set; } = "En attente";

        // Navigation : contrat formel associé
        public Contrat? Contrat { get; set; }

        // Navigation : historique des versements
        public ICollection<Versement> Versements { get; set; } = new List<Versement>();

        // Montant déjà payé (calculé dynamiquement depuis la somme des versements)
        [NotMapped]
        public decimal MontantPaye => Versements?.Sum(v => v.Montant) ?? 0;

        // Reste à payer
        [NotMapped]
        public decimal ResteAPayer => Amount - MontantPaye;
    }
}
