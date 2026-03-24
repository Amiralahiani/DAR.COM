using RealEstateAdmin.Models;

namespace RealEstateAdmin.Services
{
    public interface IBienImmobilierService
    {
        Task<BienIndexData> GetIndexDataAsync(BienFilter filter, string? currentUserId, bool hasAdminAccess);
        Task<string> ExportCsvAsync();
        Task<byte[]> ExportPdfAsync();
        Task<BienImmobilier?> GetDetailsAsync(int id);
        Task<ServiceResult<BienImmobilier>> GetForEditAsync(int id, string? currentUserId, bool hasAdminAccess);
        Task<ServiceResult> CreateAsync(BienImmobilier bienImmobilier, string currentUserId, bool hasAdminAccess);
        Task<ServiceResult> UpdateAsync(int id, BienImmobilier bienImmobilier, string? currentUserId, bool hasAdminAccess);
        Task<ServiceResult<BienImmobilier>> GetForDeleteAsync(int id, string? currentUserId, bool hasAdminAccess);
        Task<ServiceResult> DeleteAsync(int id, string? currentUserId, bool hasAdminAccess);
        Task<ServiceResult> MettreEnVenteAsync(int id, string? currentUserId, bool hasAdminAccess);
        Task<ServiceResult> TogglePublishAsync(int id, string? actorUserId);
        Task<ServiceResult> SetPublicationStatusAsync(int id, string status, string? actorUserId);
        Task<ServiceResult> SetCommercialStatusAsync(int id, string commercialStatus, string? actorUserId);
        IReadOnlyList<string> TypeTransactions { get; }
        IReadOnlyList<string> CommercialStatuses { get; }
        IReadOnlyList<string> PublicationStatuses { get; }
    }
}
