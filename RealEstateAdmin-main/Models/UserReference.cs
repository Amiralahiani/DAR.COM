namespace RealEstateAdmin.Models
{
    // Lecture-only representation of Identity users for domain joins.
    public class UserReference
    {
        public string Id { get; set; } = string.Empty;
        public string? Nom { get; set; }
        public string? UserName { get; set; }
        public string? Email { get; set; }
        public DateTime DateInscription { get; set; }
        public DateTimeOffset? LockoutEnd { get; set; }
    }
}
