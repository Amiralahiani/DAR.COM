using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RealEstateAdmin.Models
{
    public class Contrat
    {
        public int Id { get; set; }

        [Required]
        public int SaleTransactionId { get; set; }

        [ForeignKey("SaleTransactionId")]
        public SaleTransaction? SaleTransaction { get; set; }

        [Required]
        [StringLength(50)]
        [Display(Name = "Numéro de contrat")]
        public string NumeroContrat { get; set; } = string.Empty;

        [Display(Name = "Date de signature")]
        [DataType(DataType.Date)]
        public DateTime? DateSignature { get; set; }

        [Required]
        [StringLength(50)]
        [Display(Name = "Statut du contrat")]
        public string ContractStatus { get; set; } = "Brouillon"; // Brouillon / Signé / Exécuté / Annulé

        [StringLength(200)]
        [Display(Name = "Nom de l'acheteur")]
        public string? NomAcheteur { get; set; }

        [StringLength(200)]
        [Display(Name = "Nom du vendeur")]
        public string? NomVendeur { get; set; }

        [StringLength(200)]
        [Display(Name = "Nom de l'agent")]
        public string? NomAgent { get; set; }

        [StringLength(200)]
        [Display(Name = "Titre du bien")]
        public string? TitreBien { get; set; }

        [StringLength(500)]
        [Display(Name = "Adresse du bien")]
        public string? AdresseBien { get; set; }

        [Display(Name = "Surface (m²)")]
        public int? SurfaceBien { get; set; }

        [Display(Name = "Prix du contrat (DT)")]
        [Range(0, double.MaxValue)]
        public decimal PrixContrat { get; set; }

        [StringLength(2000)]
        [DataType(DataType.MultilineText)]
        [Display(Name = "Conditions de paiement")]
        public string? ConditionsPaiement { get; set; }

        [Display(Name = "Date de création")]
        public DateTime DateCreation { get; set; } = DateTime.Now;

        [StringLength(450)]
        [Display(Name = "Exécuté par (Admin)")]
        public string? ExecutePar { get; set; }

        [ForeignKey("ExecutePar")]
        public UserReference? ExecuteParUser { get; set; }
    }
}
