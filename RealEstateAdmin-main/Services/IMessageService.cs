using RealEstateAdmin.Models;

namespace RealEstateAdmin.Services
{
    public interface IMessageService
    {
        Task<IReadOnlyList<Message>> GetMessagesAsync(string? actorUserId, bool actorIsSuperAdmin);
        Task<IReadOnlyList<UserRoleViewModel>> GetAssignableAdminsAsync();
        Task<ServiceResult> AssignToAdminAsync(int messageId, string adminUserId, string? actorUserId, bool actorIsSuperAdmin);
        Task<string> ExportCsvAsync();
        Task<byte[]> ExportPdfAsync();
        Task<ServiceResult> CreateAsync(Message message, string? actorUserId);
        Task<Message?> GetByIdAsync(int id);
        Task<ServiceResult> DeleteAsync(int id, string? actorUserId);
        Task<ServiceResult> ReplyAsync(int messageId, string reponse, string? actorUserId);
        Task<ServiceResult> MarkTreatedAsync(int id, string? actorUserId);
    }
}
