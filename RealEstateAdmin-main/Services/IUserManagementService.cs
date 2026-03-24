using RealEstateAdmin.Models;

namespace RealEstateAdmin.Services
{
    public interface IUserManagementService
    {
        IReadOnlyList<string> AllowedRoles { get; }
        Task<IReadOnlyList<UserRoleViewModel>> GetUsersAsync();
        Task<UserRoleViewModel?> GetUserDetailsAsync(string id);
        Task<ServiceResult> CreateAsync(UserManagementViewModel model, string? actorUserId);
        Task<ServiceResult<UserManagementViewModel>> GetEditModelAsync(string id);
        Task<ServiceResult> EditAsync(string id, UserManagementViewModel model, string? actorUserId);
        Task<UserRoleViewModel?> GetDeleteModelAsync(string id);
        Task<ServiceResult> DeleteAsync(string id, string? actorUserId);
        Task<ServiceResult> ToggleSuspensionAsync(string id, string? actorUserId);
        Task<ServiceResult> AssignRoleAsync(string targetUserId, string roleName, string? actorUserId, bool actorIsSuperAdmin);
    }
}
