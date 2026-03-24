namespace RealEstateAdmin.Services
{
    public interface IAuditLogService
    {
        Task LogAsync(string? userId, string action, string entityType, int? entityId, string details);
        Task<IReadOnlyList<Models.AuditLog>> GetRecentAsync(int take = 200);
    }
}
