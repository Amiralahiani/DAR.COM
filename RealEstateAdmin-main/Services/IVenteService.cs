namespace RealEstateAdmin.Services
{
    public interface IVenteService
    {
        Task<SalesIndexData> GetIndexDataAsync(SalesFilter filter);
        Task<SalesCreateData> GetCreateDataAsync(SalesCreateInput? input = null);
        Task<ServiceResult<int>> CreateManualAsync(SalesCreateInput input, string? actorUserId);
        Task<ServiceResult> UpdatePaymentAsync(int id, string paymentMethod, string paymentStatus, string? actorUserId);
        Task<string> ExportCsvAsync();
        Task<byte[]> ExportPdfAsync();
    }
}
