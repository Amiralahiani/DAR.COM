using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RealEstateAdmin.Models
{
    public class BienImmobilier
    {
        public int Id { get; set; }
        
        [Display(Name = "Propriétaire")]
        public string? UserId { get; set; }
        
        [ForeignKey("UserId")]
        public UserReference? User { get; set; }

        [Required(ErrorMessage = "Le titre est obligatoire")]
        [Display(Name = "Titre")]
        [StringLength(200, ErrorMessage = "Le titre ne peut pas dépasser 200 caractères")]
        public string Titre { get; set; } = string.Empty;

        [Display(Name = "Description")]
        [DataType(DataType.MultilineText)]
        public string? Description { get; set; }

        [Required(ErrorMessage = "Le prix est obligatoire")]
        [Display(Name = "Prix (DT)")]
        [Range(0, double.MaxValue, ErrorMessage = "Le prix doit être positif")]
        public decimal Prix { get; set; }

        [Display(Name = "Adresse")]
        [StringLength(500, ErrorMessage = "L'adresse ne peut pas dépasser 500 caractères")]
        public string? Adresse { get; set; }

        [Display(Name = "Surface (m²)")]
        [Range(0, int.MaxValue, ErrorMessage = "La surface doit être positive")]
        public int? Surface { get; set; }

        [Display(Name = "Nombre de pièces")]
        [Range(0, int.MaxValue, ErrorMessage = "Le nombre de pièces doit être positif")]
        public int? NombrePieces { get; set; }

        [Display(Name = "Image URL")]
        [Url(ErrorMessage = "Veuillez entrer une URL valide")]
        [StringLength(1000, ErrorMessage = "L'URL ne peut pas dépasser 1000 caractères")]
        public string? ImageUrl { get; set; }

        [Required(ErrorMessage = "Le statut du bien est obligatoire")]
        [Display(Name = "Statut du bien")]
        public string TypeTransaction { get; set; } = "A Vendre"; // "A Vendre" ou "Acheté"

        [Display(Name = "Statut commercial")]
        [StringLength(50)]
        public string StatutCommercial { get; set; } = "Disponible"; // Disponible / Réservé / Vendu

        [Display(Name = "Publié")]
        public bool IsPublished { get; set; } = true;

        [Display(Name = "Statut de publication")]
        [StringLength(50)]
        public string PublicationStatus { get; set; } = "En attente"; // En attente / Publié / Refusé

        [Display(Name = "Validé par (Admin)")]
        [StringLength(450)]
        public string? PublicationValidatedByAdminId { get; set; }

        [Display(Name = "Date de validation")]
        [DataType(DataType.DateTime)]
        public DateTime? PublicationValidatedAt { get; set; }

        [NotMapped]
        [Display(Name = "Images (URLs multiples)")]
        public string? ImageUrlsInput { get; set; }

        public ICollection<BienImage> Images { get; set; } = new List<BienImage>();
    }
}
