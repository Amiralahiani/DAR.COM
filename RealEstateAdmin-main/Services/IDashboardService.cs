namespace RealEstateAdmin.Services
{
    public interface IDashboardService
    {
        Task<DashboardData> BuildAsync(string? currentUserId, bool isAdmin);
    }
}
