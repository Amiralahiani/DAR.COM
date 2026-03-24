using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RealEstateAdmin.Models;

namespace RealEstateAdmin.Services
{
    public class UserManagementService : IUserManagementService
    {
        private static readonly string[] InternalAllowedRoles = { "Utilisateur", "Admin", "SuperAdmin" };

        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IAuditLogService _auditLogService;

        public UserManagementService(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IAuditLogService auditLogService)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _auditLogService = auditLogService;
        }

        public IReadOnlyList<string> AllowedRoles => InternalAllowedRoles;

        public async Task<IReadOnlyList<UserRoleViewModel>> GetUsersAsync()
        {
            var users = await _userManager.Users.ToListAsync();
            var userList = new List<UserRoleViewModel>();

            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user);
                userList.Add(ToUserRoleVm(user, ResolvePrimaryRole(roles)));
            }

            return userList.OrderByDescending(u => u.CurrentRole == "SuperAdmin").ThenBy(u => u.UserName).ToList();
        }

        public async Task<UserRoleViewModel?> GetUserDetailsAsync(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return null;
            }

            var roles = await _userManager.GetRolesAsync(user);
            return ToUserRoleVm(user, ResolvePrimaryRole(roles));
        }

        public async Task<ServiceResult> CreateAsync(UserManagementViewModel model, string? actorUserId)
        {
            if (!InternalAllowedRoles.Contains(model.RoleName))
            {
                return ServiceResult.Fail(ServiceErrorCode.Validation, "Rôle invalide.");
            }

            if (!string.Equals(model.RoleName, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                return ServiceResult.Fail(ServiceErrorCode.Forbidden, "Depuis la gestion des utilisateurs, seul un compte Admin peut être créé.");
            }

            if (string.IsNullOrWhiteSpace(model.Password))
            {
                return ServiceResult.Fail(ServiceErrorCode.Validation, "Le mot de passe est obligatoire.");
            }

            var user = new ApplicationUser
            {
                UserName = model.Email,
                Email = model.Email,
                Nom = model.Nom,
                EmailConfirmed = true
            };

            var result = await _userManager.CreateAsync(user, model.Password);
            if (!result.Succeeded)
            {
                return ServiceResult.Fail(ServiceErrorCode.Validation, string.Join(" | ", result.Errors.Select(e => e.Description)));
            }

            if (!await _roleManager.RoleExistsAsync(model.RoleName))
            {
                return ServiceResult.Fail(ServiceErrorCode.Validation, "Rôle introuvable.");
            }

            await _userManager.AddToRoleAsync(user, model.RoleName);
            if (model.RoleName == "SuperAdmin" && !await _userManager.IsInRoleAsync(user, "Admin"))
            {
                await _userManager.AddToRoleAsync(user, "Admin");
            }

            if (model.IsSuspended)
            {
                await _userManager.SetLockoutEnabledAsync(user, true);
                await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(100));
            }

            await _auditLogService.LogAsync(actorUserId, "Create", "Utilisateur", null, $"Création du compte {model.Email} ({model.RoleName})");
            return ServiceResult.Ok("Utilisateur créé avec succès.");
        }

        public async Task<ServiceResult<UserManagementViewModel>> GetEditModelAsync(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return ServiceResult<UserManagementViewModel>.Fail(ServiceErrorCode.NotFound, "Utilisateur introuvable.");
            }

            var roles = await _userManager.GetRolesAsync(user);
            var vm = new UserManagementViewModel
            {
                UserId = user.Id,
                Nom = user.Nom ?? user.UserName ?? "",
                Email = user.Email ?? "",
                RoleName = ResolvePrimaryRole(roles),
                IsSuspended = user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.UtcNow
            };

            return ServiceResult<UserManagementViewModel>.Ok(vm);
        }

        public async Task<ServiceResult> EditAsync(string id, UserManagementViewModel model, string? actorUserId)
        {
            if (id != model.UserId)
            {
                return ServiceResult.Fail(ServiceErrorCode.BadRequest, "ID invalide.");
            }

            if (!InternalAllowedRoles.Contains(model.RoleName))
            {
                return ServiceResult.Fail(ServiceErrorCode.Validation, "Rôle invalide.");
            }

            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return ServiceResult.Fail(ServiceErrorCode.NotFound, "Utilisateur introuvable.");
            }

            user.Nom = model.Nom;
            user.Email = model.Email;
            user.UserName = model.Email;

            var updateResult = await _userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                return ServiceResult.Fail(ServiceErrorCode.Validation, string.Join(" | ", updateResult.Errors.Select(e => e.Description)));
            }

            if (!string.IsNullOrWhiteSpace(model.Password))
            {
                var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
                var resetResult = await _userManager.ResetPasswordAsync(user, resetToken, model.Password);
                if (!resetResult.Succeeded)
                {
                    return ServiceResult.Fail(ServiceErrorCode.Validation, string.Join(" | ", resetResult.Errors.Select(e => e.Description)));
                }
            }

            var roles = await _userManager.GetRolesAsync(user);
            var currentRole = ResolvePrimaryRole(roles);

            if (!IsRoleTransitionAllowed(currentRole, model.RoleName))
            {
                return ServiceResult.Fail(ServiceErrorCode.Forbidden, BuildTransitionErrorMessage(currentRole, model.RoleName));
            }

            if (!string.IsNullOrWhiteSpace(actorUserId)
                && actorUserId == user.Id
                && string.Equals(currentRole, "SuperAdmin", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(model.RoleName, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
            {
                return ServiceResult.Fail(ServiceErrorCode.Forbidden, "Vous ne pouvez pas retirer votre propre rôle SuperAdmin.");
            }

            await _userManager.RemoveFromRolesAsync(user, roles);
            await _userManager.AddToRoleAsync(user, model.RoleName);
            if (model.RoleName == "SuperAdmin" && !await _userManager.IsInRoleAsync(user, "Admin"))
            {
                await _userManager.AddToRoleAsync(user, "Admin");
            }

            await _userManager.SetLockoutEnabledAsync(user, true);
            await _userManager.SetLockoutEndDateAsync(user, model.IsSuspended
                ? DateTimeOffset.UtcNow.AddYears(100)
                : null);

            await _auditLogService.LogAsync(actorUserId, "Edit", "Utilisateur", null, $"Modification du compte {model.Email} ({model.RoleName})");
            return ServiceResult.Ok("Utilisateur modifié avec succès.");
        }

        public async Task<UserRoleViewModel?> GetDeleteModelAsync(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return null;
            }

            var roles = await _userManager.GetRolesAsync(user);
            return ToUserRoleVm(user, ResolvePrimaryRole(roles));
        }

        public async Task<ServiceResult> DeleteAsync(string id, string? actorUserId)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return ServiceResult.Fail(ServiceErrorCode.NotFound, "Utilisateur introuvable.");
            }

            if (!string.IsNullOrWhiteSpace(actorUserId) && actorUserId == user.Id)
            {
                return ServiceResult.Fail(ServiceErrorCode.Forbidden, "Vous ne pouvez pas supprimer votre propre compte.");
            }

            var email = user.Email ?? user.UserName ?? user.Id;
            var result = await _userManager.DeleteAsync(user);
            if (!result.Succeeded)
            {
                return ServiceResult.Fail(ServiceErrorCode.Validation, string.Join(" | ", result.Errors.Select(e => e.Description)));
            }

            await _auditLogService.LogAsync(actorUserId, "Delete", "Utilisateur", null, $"Suppression du compte {email}");
            return ServiceResult.Ok("Utilisateur supprimé avec succès.");
        }

        public async Task<ServiceResult> ToggleSuspensionAsync(string id, string? actorUserId)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return ServiceResult.Fail(ServiceErrorCode.NotFound, "Utilisateur introuvable.");
            }

            if (!string.IsNullOrWhiteSpace(actorUserId) && actorUserId == user.Id)
            {
                return ServiceResult.Fail(ServiceErrorCode.Forbidden, "Vous ne pouvez pas suspendre votre propre compte.");
            }

            var roles = await _userManager.GetRolesAsync(user);
            var currentRole = ResolvePrimaryRole(roles);
            if (string.Equals(currentRole, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
            {
                return ServiceResult.Fail(ServiceErrorCode.Forbidden, "Le compte SuperAdmin ne peut pas être suspendu.");
            }

            var isSuspended = user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.UtcNow;
            await _userManager.SetLockoutEnabledAsync(user, true);
            await _userManager.SetLockoutEndDateAsync(user, isSuspended ? null : DateTimeOffset.UtcNow.AddYears(100));

            var actionLabel = isSuspended ? "Réactivation" : "Suspension";
            await _auditLogService.LogAsync(actorUserId, "Status", "Utilisateur", null, $"{actionLabel} du compte {user.Email}");

            return ServiceResult.Ok(isSuspended
                ? "Compte réactivé avec succès."
                : "Compte suspendu avec succès.");
        }

        public async Task<ServiceResult> AssignRoleAsync(string targetUserId, string roleName, string? actorUserId, bool actorIsSuperAdmin)
        {
            var user = await _userManager.FindByIdAsync(targetUserId);
            if (user == null)
            {
                return ServiceResult.Fail(ServiceErrorCode.NotFound, "Utilisateur introuvable.");
            }

            if (!await _roleManager.RoleExistsAsync(roleName))
            {
                return ServiceResult.Fail(ServiceErrorCode.Validation, "Rôle invalide.");
            }

            var roles = await _userManager.GetRolesAsync(user);
            var currentRole = ResolvePrimaryRole(roles);

            if (!IsRoleTransitionAllowed(currentRole, roleName))
            {
                return ServiceResult.Fail(ServiceErrorCode.Forbidden, BuildTransitionErrorMessage(currentRole, roleName));
            }

            if (!string.IsNullOrWhiteSpace(actorUserId)
                && actorUserId == user.Id
                && actorIsSuperAdmin
                && roleName != "SuperAdmin")
            {
                return ServiceResult.Fail(ServiceErrorCode.Forbidden, "Vous ne pouvez pas retirer votre propre rôle SuperAdmin.");
            }

            await _userManager.RemoveFromRolesAsync(user, roles);
            await _userManager.AddToRoleAsync(user, roleName);
            if (roleName == "SuperAdmin" && !await _userManager.IsInRoleAsync(user, "Admin"))
            {
                await _userManager.AddToRoleAsync(user, "Admin");
            }

            await _auditLogService.LogAsync(actorUserId, "Role", "Utilisateur", null, $"Attribution du rôle {roleName} à {user.Email}");
            return ServiceResult.Ok("Rôle assigné avec succès.");
        }

        private static UserRoleViewModel ToUserRoleVm(ApplicationUser user, string role)
        {
            return new UserRoleViewModel
            {
                UserId = user.Id,
                UserName = user.Nom ?? user.UserName ?? "",
                Email = user.Email ?? "",
                CurrentRole = role,
                IsSuspended = user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.UtcNow,
                EmailConfirmed = user.EmailConfirmed
            };
        }

        private static string ResolvePrimaryRole(IEnumerable<string> roles)
        {
            var roleSet = new HashSet<string>(roles, StringComparer.OrdinalIgnoreCase);
            if (roleSet.Contains("SuperAdmin"))
            {
                return "SuperAdmin";
            }

            if (roleSet.Contains("Admin"))
            {
                return "Admin";
            }

            if (roleSet.Contains("Utilisateur"))
            {
                return "Utilisateur";
            }

            return "Aucun";
        }

        private static bool IsRoleTransitionAllowed(string currentRole, string targetRole)
        {
            if (string.Equals(currentRole, targetRole, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(currentRole, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.Equals(currentRole, "Admin", StringComparison.OrdinalIgnoreCase)
                && string.Equals(targetRole, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(currentRole, "Admin", StringComparison.OrdinalIgnoreCase)
                && string.Equals(targetRole, "Utilisateur", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.Equals(currentRole, "Utilisateur", StringComparison.OrdinalIgnoreCase)
                && (string.Equals(targetRole, "Admin", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(targetRole, "SuperAdmin", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            return false;
        }

        private static string BuildTransitionErrorMessage(string currentRole, string targetRole)
        {
            if (string.Equals(currentRole, "SuperAdmin", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(targetRole, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
            {
                return "Le rôle SuperAdmin n'est pas modifiable depuis la gestion des rôles.";
            }

            if (string.Equals(currentRole, "Admin", StringComparison.OrdinalIgnoreCase)
                && string.Equals(targetRole, "Utilisateur", StringComparison.OrdinalIgnoreCase))
            {
                return "Un Admin ne peut pas devenir Utilisateur.";
            }

            if (string.Equals(currentRole, "Utilisateur", StringComparison.OrdinalIgnoreCase)
                && string.Equals(targetRole, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                return "Un utilisateur ne peut pas être promu Admin. Les comptes Admin doivent être créés directement par un SuperAdmin.";
            }

            if (string.Equals(targetRole, "SuperAdmin", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(currentRole, "Admin", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(currentRole, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
            {
                return "Seul un compte Admin peut devenir SuperAdmin.";
            }

            return "Transition de rôle non autorisée.";
        }
    }
}
