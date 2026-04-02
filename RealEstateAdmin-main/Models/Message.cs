using System.ComponentModel.DataAnnotations;

namespace RealEstateAdmin.Models
{
    public class Message
    {
        public int Id { get; set; }

        [Display(Name = "Nom d'utilisateur")]
        [StringLength(100, ErrorMessage = "Le nom ne peut pas dépasser 100 caractères")]
        public string? NomUtilisateur { get; set; }

        [Display(Name = "Email")]
        [EmailAddress(ErrorMessage = "L'email n'est pas valide")]
        [StringLength(200, ErrorMessage = "L'email ne peut pas dépasser 200 caractères")]
        public string? Email { get; set; }

        [Display(Name = "Sujet")]
        [StringLength(200, ErrorMessage = "Le sujet ne peut pas dépasser 200 caractères")]
        public string? Sujet { get; set; }

        [Display(Name = "Contenu")]
        [DataType(DataType.MultilineText)]
        public string? Contenu { get; set; }

        [Display(Name = "Destinataire")]
        [StringLength(50)]
        public string Destinataire { get; set; } = "Administration";

        [Display(Name = "Date de création")]
        [DataType(DataType.DateTime)]
        public DateTime DateCreation { get; set; } = DateTime.Now;

        [Display(Name = "Statut")]
        [StringLength(50)]
        public string Statut { get; set; } = "Nouveau";

        [Display(Name = "Géré par")]
        [StringLength(450)]
        public string? TraiteParId { get; set; }

        [Display(Name = "Date de décision")]
        [DataType(DataType.DateTime)]
        public DateTime? DateTraitement { get; set; }
    }
}
