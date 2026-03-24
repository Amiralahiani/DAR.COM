using Microsoft.AspNetCore.Identity;

namespace RealEstateAdmin.Models
{
    public class ApplicationUser : IdentityUser
    {
        public string? Nom { get; set; }
    }
}

