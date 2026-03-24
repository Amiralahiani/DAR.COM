namespace RealEstateAdmin.Services
{
    public interface IShopService
    {
        Task<ShopIndexData> GetIndexDataAsync(ShopFilter filter);
        Task<ServiceResult> ExpressInterestAsync(int bienId, string userId);
        Task<ServiceResult> ReserveVisitAsync(int bienId, DateTime visitSlot, string userId);
        Task<ServiceResult> RequestAgentMeetingAsync(int bienId, DateTime meetingDateTime, string userId);
    }
}
