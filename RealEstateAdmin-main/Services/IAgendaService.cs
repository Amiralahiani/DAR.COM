namespace RealEstateAdmin.Services
{
    public interface IAgendaService
    {
        Task<AgendaIndexData> GetAgendaAsync(string actorUserId, bool actorIsSuperAdmin);
        Task<ServiceResult<AgendaEventDetails>> GetEventDetailsAsync(int messageId, string actorUserId, bool actorIsSuperAdmin);
        Task<ServiceResult> AcceptEventAsync(int messageId, string actorUserId, bool actorIsSuperAdmin);
        Task<ServiceResult> RefuseEventAsync(int messageId, string actorUserId, bool actorIsSuperAdmin);
    }
}
