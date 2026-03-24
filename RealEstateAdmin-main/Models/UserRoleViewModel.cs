namespace RealEstateAdmin.Models
{
    public class UserRoleViewModel
    {
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string CurrentRole { get; set; } = string.Empty;
        public bool IsSuspended { get; set; }
        public bool EmailConfirmed { get; set; }
    }
}
