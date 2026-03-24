namespace RealEstateAdmin.Services
{
    public interface IAgendaService
    {
        Task<AgendaIndexData> GetAgendaAsync(string actorUserId, bool actorIsSuperAdmin);
        Task<ServiceResult<AgendaEventDetails>> GetEventDetailsAsync(int messageId, string actorUserId, bool actorIsSuperAdmin);
        Task<ServiceResult> MarkEventTreatedAsync(int messageId, string actorUserId, bool actorIsSuperAdmin);
    }
}
