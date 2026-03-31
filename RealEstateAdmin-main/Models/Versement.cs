using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RealEstateAdmin.Models
{
    public class Versement
    {
        public int Id { get; set; }

        [Required]
        public int SaleTransactionId { get; set; }

        [ForeignKey("SaleTransactionId")]
        public SaleTransaction? SaleTransaction { get; set; }

        [Required]
        [Range(0.01, double.MaxValue, ErrorMessage = "Le montant doit être supérieur à 0")]
        [Display(Name = "Montant (DT)")]
        public decimal Montant { get; set; }

        [Required]
        [Display(Name = "Date du versement")]
        [DataType(DataType.Date)]
        public DateTime DateVersement { get; set; } = DateTime.Today;

        [Required]
        [StringLength(50)]
        [Display(Name = "Mode de paiement")]
        public string ModePaiement { get; set; } = "Virement";

        [StringLength(500)]
        [Display(Name = "Note")]
        public string? Note { get; set; }

        [Display(Name = "Enregistré le")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [StringLength(450)]
        [Display(Name = "Ajouté par")]
        public string? AjoutePar { get; set; }

        [ForeignKey("AjoutePar")]
        public UserReference? AjouteParUser { get; set; }
    }
}
