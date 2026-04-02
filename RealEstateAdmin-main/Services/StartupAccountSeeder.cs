using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RealEstateAdmin.Data;
using RealEstateAdmin.Models;

namespace RealEstateAdmin.Services
{
    public interface IStartupAccountSeeder
    {
        Task SeedAsync(CancellationToken cancellationToken = default);
    }

    public class StartupAccountSeeder : IStartupAccountSeeder
    {
        private static readonly string[] RequiredRoles = { "Utilisateur", "Admin", "SuperAdmin" };

        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ApplicationDbContext _applicationDbContext;
        private readonly ApplicationIdentityDbContext _identityDbContext;
        private readonly IConfiguration _configuration;
        private readonly ILogger<StartupAccountSeeder> _logger;

        public StartupAccountSeeder(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            ApplicationDbContext applicationDbContext,
            ApplicationIdentityDbContext identityDbContext,
            IConfiguration configuration,
            ILogger<StartupAccountSeeder> logger)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _applicationDbContext = applicationDbContext;
            _identityDbContext = identityDbContext;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task SeedAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await ApplyApplicationMigrationsAsync(cancellationToken);
                await ApplyIdentityMigrationsAsync(cancellationToken);
                await EnsureRolesAsync();

                if (ShouldSeedDefaultAccounts())
                {
                    var accounts = BuildAccounts();
                    if (accounts.Count == 0)
                    {
                        _logger.LogWarning("Seed comptes activé, mais aucun compte n'est configuré dans Bootstrap:TeamAccounts.");
                    }
                    else
                    {
                        var forcePasswordReset = _configuration.GetValue<bool>("Bootstrap:ForcePasswordResetOnStartup");
                        foreach (var account in accounts)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            await UpsertAccountAsync(account, forcePasswordReset);
                        }
                    }
                }
                else
                {
                    _logger.LogInformation("Seed comptes ignoré: Bootstrap:SeedDefaultAccounts=false.");
                }

                await SeedSharedBiensAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Initialisation de démarrage échouée. L'application continue sans bootstrap.");
            }
        }

        private bool ShouldSeedDefaultAccounts()
        {
            return _configuration.GetValue<bool>("Bootstrap:SeedDefaultAccounts");
        }

        private bool ShouldSeedSharedBiens()
        {
            return _configuration.GetValue<bool>("Bootstrap:SeedSharedBiens");
        }

        private async Task SeedSharedBiensAsync(CancellationToken cancellationToken)
        {
            if (!ShouldSeedSharedBiens())
            {
                _logger.LogInformation("Seed biens partagés ignoré: Bootstrap:SeedSharedBiens=false.");
                return;
            }

            var sharedBiens = BuildSharedBiens();
            if (sharedBiens.Count == 0)
            {
                _logger.LogWarning("Seed biens partagés activé, mais aucun bien n'est configuré dans Bootstrap:SharedBiens.");
                return;
            }

            foreach (var seedBien in sharedBiens)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await UpsertSharedBienAsync(seedBien, cancellationToken);
            }
        }

        private List<SeedBienOptions> BuildSharedBiens()
        {
            var configured = _configuration
                .GetSection("Bootstrap:SharedBiens")
                .Get<List<SeedBienOptions>>() ?? new List<SeedBienOptions>();

            var result = new List<SeedBienOptions>();
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var bien in configured)
            {
                var key = bien.Key?.Trim();
                var titre = bien.Titre?.Trim();
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(titre))
                {
                    continue;
                }

                var normalizedKey = NormalizeSeedKey(key);
                if (!seenKeys.Add(normalizedKey))
                {
                    continue;
                }

                bien.Key = normalizedKey;
                bien.Titre = titre;
                bien.Adresse = bien.Adresse?.Trim();
                bien.Description = bien.Description?.Trim();
                bien.OwnerEmail = bien.OwnerEmail?.Trim();
                bien.StatutCommercial = NormalizeStatutCommercial(bien.StatutCommercial);
                bien.PublicationStatus = NormalizePublicationStatus(bien.PublicationStatus, bien.IsPublished);
                bien.DiscountPercent = Math.Clamp(bien.DiscountPercent, 0, 100);

                result.Add(bien);
            }

            return result;
        }

        private async Task UpsertSharedBienAsync(SeedBienOptions seedBien, CancellationToken cancellationToken)
        {
            var mappingKey = BuildBienMappingKey(seedBien.Key);
            var mapping = await _applicationDbContext.AppSettings
                .FirstOrDefaultAsync(s => s.Key == mappingKey, cancellationToken);

            BienImmobilier? bien = null;
            if (mapping != null
                && !string.IsNullOrWhiteSpace(mapping.Value)
                && int.TryParse(mapping.Value, out var mappedBienId))
            {
                bien = await _applicationDbContext.Biens
                    .FirstOrDefaultAsync(b => b.Id == mappedBienId, cancellationToken);
            }

            if (bien == null)
            {
                bien = await _applicationDbContext.Biens.FirstOrDefaultAsync(
                    b => b.Titre == seedBien.Titre && b.Adresse == seedBien.Adresse,
                    cancellationToken);
            }

            var isNew = bien == null;
            if (isNew)
            {
                bien = new BienImmobilier();
                _applicationDbContext.Biens.Add(bien);
            }

            var ownerId = await ResolveUserIdByEmailAsync(seedBien.OwnerEmail);
            ApplySeedBienValues(bien!, seedBien, ownerId);

            await _applicationDbContext.SaveChangesAsync(cancellationToken);

            if (mapping == null)
            {
                mapping = new AppSetting
                {
                    Key = mappingKey,
                    Value = bien!.Id.ToString()
                };
                _applicationDbContext.AppSettings.Add(mapping);
            }
            else if (!string.Equals(mapping.Value, bien!.Id.ToString(), StringComparison.Ordinal))
            {
                mapping.Value = bien.Id.ToString();
            }

            await _applicationDbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Bien partagé {SeedKey} synchronisé: {Titre} (Id={BienId}).",
                seedBien.Key,
                bien!.Titre,
                bien.Id);
        }

        private async Task<string?> ResolveUserIdByEmailAsync(string? email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return null;
            }

            var user = await _userManager.FindByEmailAsync(email.Trim());
            if (user == null)
            {
                _logger.LogWarning(
                    "OwnerEmail {Email} introuvable pour un bien seedé. Le bien sera sans propriétaire.",
                    email);
            }

            return user?.Id;
        }

        private static void ApplySeedBienValues(BienImmobilier bien, SeedBienOptions seedBien, string? ownerId)
        {
            bien.UserId = ownerId;
            bien.Titre = seedBien.Titre;
            bien.Description = seedBien.Description;
            bien.Prix = seedBien.Prix < 0 ? 0 : seedBien.Prix;
            bien.Adresse = seedBien.Adresse;
            bien.Surface = seedBien.Surface;
            bien.NombrePieces = seedBien.NombrePieces;
            bien.ImageUrl = seedBien.ImageUrl;
            bien.TypeTransaction = "A Vendre";
            bien.StatutCommercial = NormalizeStatutCommercial(seedBien.StatutCommercial);
            bien.IsPublished = seedBien.IsPublished;
            bien.PublicationStatus = NormalizePublicationStatus(seedBien.PublicationStatus, seedBien.IsPublished);
            bien.DiscountPercent = Math.Clamp(seedBien.DiscountPercent, 0, 100);
            bien.Latitude = seedBien.Latitude;
            bien.Longitude = seedBien.Longitude;

            if (!string.Equals(bien.PublicationStatus, "Publié", StringComparison.OrdinalIgnoreCase))
            {
                bien.PublicationValidatedByAdminId = null;
                bien.PublicationValidatedAt = null;
            }
        }

        private static string BuildBienMappingKey(string seedKey)
        {
            return $"bootstrap:shared-bien:{NormalizeSeedKey(seedKey)}";
        }

        private static string NormalizeSeedKey(string? seedKey)
        {
            return (seedKey ?? string.Empty).Trim().ToUpperInvariant();
        }

        private static string NormalizeStatutCommercial(string? statutCommercial)
        {
            if (string.Equals(statutCommercial, "Réservé", StringComparison.OrdinalIgnoreCase))
            {
                return "Réservé";
            }

            if (string.Equals(statutCommercial, "Vendu", StringComparison.OrdinalIgnoreCase))
            {
                return "Vendu";
            }

            return "Disponible";
        }

        private static string NormalizePublicationStatus(string? publicationStatus, bool isPublished)
        {
            if (!isPublished)
            {
                return "Refusé";
            }

            if (string.Equals(publicationStatus, "En attente", StringComparison.OrdinalIgnoreCase))
            {
                return "En attente";
            }

            if (string.Equals(publicationStatus, "Refusé", StringComparison.OrdinalIgnoreCase))
            {
                return "Refusé";
            }

            return "Publié";
        }

        private async Task ApplyApplicationMigrationsAsync(CancellationToken cancellationToken)
        {
            var pendingMigrations = await _applicationDbContext.Database.GetPendingMigrationsAsync(cancellationToken);
            if (!pendingMigrations.Any())
            {
                return;
            }

            _logger.LogInformation(
                "Application des migrations métier en attente: {Count}.",
                pendingMigrations.Count());
            await _applicationDbContext.Database.MigrateAsync(cancellationToken);
        }

        private async Task ApplyIdentityMigrationsAsync(CancellationToken cancellationToken)
        {
            var pendingMigrations = await _identityDbContext.Database.GetPendingMigrationsAsync(cancellationToken);
            if (!pendingMigrations.Any())
            {
                return;
            }

            _logger.LogInformation(
                "Application des migrations Identity en attente: {Count}.",
                pendingMigrations.Count());
            await _identityDbContext.Database.MigrateAsync(cancellationToken);
        }

        private async Task EnsureRolesAsync()
        {
            foreach (var role in RequiredRoles)
            {
                if (await _roleManager.RoleExistsAsync(role))
                {
                    continue;
                }

                var result = await _roleManager.CreateAsync(new IdentityRole(role));
                if (!result.Succeeded)
                {
                    _logger.LogError(
                        "Impossible de créer le rôle {Role}: {Errors}",
                        role,
                        string.Join(" | ", result.Errors.Select(e => e.Description)));
                }
            }
        }

        private List<SeedAccountOptions> BuildAccounts()
        {
            var configured = _configuration
                .GetSection("Bootstrap:TeamAccounts")
                .Get<List<SeedAccountOptions>>() ?? new List<SeedAccountOptions>();

            var result = new List<SeedAccountOptions>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var account in configured)
            {
                var email = account.Email?.Trim();
                if (string.IsNullOrWhiteSpace(email))
                {
                    continue;
                }

                if (!seen.Add(email))
                {
                    continue;
                }

                account.Email = email;
                account.Role = NormalizeRole(account.Role);
                result.Add(account);
            }

            return result;
        }

        private async Task UpsertAccountAsync(SeedAccountOptions account, bool forcePasswordReset)
        {
            if (string.IsNullOrWhiteSpace(account.Email))
            {
                return;
            }

            var email = account.Email.Trim();
            var normalizedRole = NormalizeRole(account.Role);
            var user = await _userManager.FindByEmailAsync(email);

            if (user == null)
            {
                if (string.IsNullOrWhiteSpace(account.Password))
                {
                    _logger.LogWarning("Mot de passe manquant pour le compte {Email}; création ignorée.", email);
                    return;
                }

                user = new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    Nom = ResolveDisplayName(account),
                    EmailConfirmed = true
                };

                var createResult = await _userManager.CreateAsync(user, account.Password);
                if (!createResult.Succeeded)
                {
                    _logger.LogError(
                        "Erreur création compte {Email}: {Errors}",
                        email,
                        string.Join(" | ", createResult.Errors.Select(e => e.Description)));
                    return;
                }

                _logger.LogInformation("Compte seed créé: {Email} ({Role}).", email, normalizedRole);
            }
            else
            {
                var needsUpdate = false;
                var desiredName = ResolveDisplayName(account);

                if (!string.Equals(user.UserName, email, StringComparison.OrdinalIgnoreCase))
                {
                    user.UserName = email;
                    needsUpdate = true;
                }

                if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
                {
                    user.Email = email;
                    needsUpdate = true;
                }

                if (!string.IsNullOrWhiteSpace(desiredName)
                    && !string.Equals(user.Nom, desiredName, StringComparison.Ordinal))
                {
                    user.Nom = desiredName;
                    needsUpdate = true;
                }

                if (!user.EmailConfirmed)
                {
                    user.EmailConfirmed = true;
                    needsUpdate = true;
                }

                if (needsUpdate)
                {
                    var updateResult = await _userManager.UpdateAsync(user);
                    if (!updateResult.Succeeded)
                    {
                        _logger.LogError(
                            "Erreur mise à jour compte {Email}: {Errors}",
                            email,
                            string.Join(" | ", updateResult.Errors.Select(e => e.Description)));
                        return;
                    }
                }

                if (forcePasswordReset && !string.IsNullOrWhiteSpace(account.Password))
                {
                    var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
                    var resetResult = await _userManager.ResetPasswordAsync(user, resetToken, account.Password);
                    if (!resetResult.Succeeded)
                    {
                        _logger.LogError(
                            "Erreur reset mot de passe pour {Email}: {Errors}",
                            email,
                            string.Join(" | ", resetResult.Errors.Select(e => e.Description)));
                        return;
                    }
                }
            }

            await EnsureRoleAssignmentAsync(user, normalizedRole);
            await EnsureSuspensionStateAsync(user, account.IsSuspended);
        }

        private async Task EnsureRoleAssignmentAsync(ApplicationUser user, string normalizedRole)
        {
            var targetRoles = BuildTargetRoles(normalizedRole);
            var currentRoles = await _userManager.GetRolesAsync(user);

            var rolesToRemove = currentRoles.Except(targetRoles, StringComparer.OrdinalIgnoreCase).ToArray();
            if (rolesToRemove.Length > 0)
            {
                var removeResult = await _userManager.RemoveFromRolesAsync(user, rolesToRemove);
                if (!removeResult.Succeeded)
                {
                    _logger.LogError(
                        "Erreur suppression rôles pour {Email}: {Errors}",
                        user.Email,
                        string.Join(" | ", removeResult.Errors.Select(e => e.Description)));
                    return;
                }
            }

            var rolesToAdd = targetRoles.Except(currentRoles, StringComparer.OrdinalIgnoreCase).ToArray();
            if (rolesToAdd.Length > 0)
            {
                var addResult = await _userManager.AddToRolesAsync(user, rolesToAdd);
                if (!addResult.Succeeded)
                {
                    _logger.LogError(
                        "Erreur attribution rôles pour {Email}: {Errors}",
                        user.Email,
                        string.Join(" | ", addResult.Errors.Select(e => e.Description)));
                }
            }
        }

        private async Task EnsureSuspensionStateAsync(ApplicationUser user, bool isSuspended)
        {
            await _userManager.SetLockoutEnabledAsync(user, true);
            await _userManager.SetLockoutEndDateAsync(
                user,
                isSuspended ? DateTimeOffset.UtcNow.AddYears(100) : null);
        }

        private static string ResolveDisplayName(SeedAccountOptions account)
        {
            if (!string.IsNullOrWhiteSpace(account.Nom))
            {
                return account.Nom.Trim();
            }

            var atIndex = account.Email.IndexOf('@');
            return atIndex > 0 ? account.Email[..atIndex] : account.Email;
        }

        private static string NormalizeRole(string? role)
        {
            if (string.Equals(role, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
            {
                return "SuperAdmin";
            }

            if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                return "Admin";
            }

            return "Utilisateur";
        }

        private static string[] BuildTargetRoles(string normalizedRole)
        {
            if (string.Equals(normalizedRole, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
            {
                return new[] { "SuperAdmin", "Admin" };
            }

            if (string.Equals(normalizedRole, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                return new[] { "Admin" };
            }

            return new[] { "Utilisateur" };
        }

        private sealed class SeedAccountOptions
        {
            public string Email { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
            public string Role { get; set; } = "Utilisateur";
            public string? Nom { get; set; }
            public bool IsSuspended { get; set; }
        }

        private sealed class SeedBienOptions
        {
            public string Key { get; set; } = string.Empty;
            public string Titre { get; set; } = string.Empty;
            public string? Description { get; set; }
            public decimal Prix { get; set; }
            public string? Adresse { get; set; }
            public int? Surface { get; set; }
            public int? NombrePieces { get; set; }
            public string? ImageUrl { get; set; }
            public double? Latitude { get; set; }
            public double? Longitude { get; set; }
            public string StatutCommercial { get; set; } = "Disponible";
            public bool IsPublished { get; set; } = true;
            public string PublicationStatus { get; set; } = "Publié";
            public int DiscountPercent { get; set; }
            public string? OwnerEmail { get; set; }
        }
    }
}
