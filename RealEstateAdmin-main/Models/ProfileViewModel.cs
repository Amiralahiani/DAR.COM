using System.ComponentModel.DataAnnotations;

namespace RealEstateAdmin.Models
{
    public class ProfileViewModel
    {
        [Required(ErrorMessage = "Le nom est obligatoire.")]
        [Display(Name = "Nom complet")]
        public string Nom { get; set; } = string.Empty;

        [Required(ErrorMessage = "L'email est obligatoire.")]
        [EmailAddress(ErrorMessage = "L'email n'est pas valide.")]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [Display(Name = "Rôle")]
        public string Role { get; set; } = string.Empty;

        [Display(Name = "Date d'inscription")]
        public DateTime DateInscription { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "Ancien mot de passe")]
        public string? OldPassword { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "Nouveau mot de passe")]
        [StringLength(100, ErrorMessage = "Le {0} doit comporter au moins {2} caractères.", MinimumLength = 6)]
        public string? NewPassword { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "Confirmer le nouveau mot de passe")]
        [Compare("NewPassword", ErrorMessage = "Le nouveau mot de passe et son mot de passe de confirmation ne correspondent pas.")]
        public string? ConfirmPassword { get; set; }

        public AgentPerformanceViewModel? Performance { get; set; }
        public int? Rank { get; set; }
        public int? TotalAgents { get; set; }
    }
}
