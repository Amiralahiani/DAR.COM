using RealEstateAdmin.Models;

namespace RealEstateAdmin.Services
{
    public interface IAgentPerformanceService
    {
        Task RecomputeAllAsync();
        Task<IReadOnlyList<AgentPerformance>> GetAllAsync();
        Task<IReadOnlyList<AgentPerformanceViewModel>> GetAllViewModelsAsync();
    }
}
