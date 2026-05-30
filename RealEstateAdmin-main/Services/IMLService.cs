using RealEstateAdmin.Models;

namespace RealEstateAdmin.Services
{
    public interface IMLService
    {
        Task<ClientProfile?> GetClientProfileAsync(string userId, UserBehaviorData behavior);
        Task<UserBehaviorData> EstimateBehaviorAsync(string userId);
        Task CollectAsync(string userId, UserBehaviorData behavior, int targetLead, double targetBudget);
        Task<bool> IsAvailableAsync();

        /// <summary>
        /// Retourne les biens publiés qui correspondent le mieux au profil ML du client
        /// (budget, intention d'achat/location, surface).
        /// </summary>
        Task<List<BienImmobilier>> GetRecommendedBiensAsync(string userId);
    }

    public sealed record UserBehaviorData(
        double AvgPriceViewed,
        int PropertyViewsCount,
        int FavoritesCount,
        int ContactAgentClicks,
        double AvgSurfaceViewed,
        double SessionDurationMins
    );
}
