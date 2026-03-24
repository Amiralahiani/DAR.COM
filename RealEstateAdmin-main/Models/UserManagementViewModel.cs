using System.ComponentModel.DataAnnotations;

namespace RealEstateAdmin.Models
{
    public class UserManagementViewModel
    {
        public string? UserId { get; set; }

        [Required(ErrorMessage = "Le nom est obligatoire")]
        [Display(Name = "Nom")]
        [StringLength(100, ErrorMessage = "Le nom ne peut pas dépasser 100 caractères")]
        public string Nom { get; set; } = string.Empty;

        [Required(ErrorMessage = "L'email est obligatoire")]
        [Display(Name = "Email")]
        [EmailAddress(ErrorMessage = "L'email n'est pas valide")]
        public string Email { get; set; } = string.Empty;

        [Display(Name = "Mot de passe")]
        [DataType(DataType.Password)]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Le mot de passe doit contenir entre 6 et 100 caractères")]
        public string? Password { get; set; }

        [Display(Name = "Confirmer le mot de passe")]
        [DataType(DataType.Password)]
        [Compare("Password", ErrorMessage = "Le mot de passe et la confirmation ne correspondent pas.")]
        public string? ConfirmPassword { get; set; }

        [Display(Name = "Rôle")]
        [Required(ErrorMessage = "Le rôle est obligatoire")]
        public string RoleName { get; set; } = "Utilisateur";

        [Display(Name = "Compte suspendu")]
        public bool IsSuspended { get; set; }
    }
}
