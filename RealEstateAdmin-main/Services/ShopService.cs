using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RealEstateAdmin.Data;
using RealEstateAdmin.Models;

namespace RealEstateAdmin.Services
{
    public class ShopService : IShopService
    {
        private static readonly string[] TypeOptions = { "A Vendre", "A Louer" };
        private static readonly string[] StatusOptions = { "Disponible", "Réservé", "Vendu" };
        private static readonly int[] VisitSlotHours = { 9, 11, 14, 16 };

        private const string VisitSubjectPrefix = "Réservation visite - Bien #";
        private const string InterestSubjectPrefix = "Intérêt bien - #";
        private const string MeetingSubjectPrefix = "Rendez-vous agent - Bien #";
        private const string AssignmentStatusKey = "ASSIGNMENT_STATUS";
        private const string AssignmentModeKey = "ASSIGNMENT_MODE";
        private const string AssignedToUserIdKey = "ASSIGNED_TO_USER_ID";
        private const string AssignedToNameKey = "ASSIGNED_TO_NAME";
        private const string AssignedToRoleKey = "ASSIGNED_TO_ROLE";
        private const string RoutingSuperAdminIdKey = "ROUTING_SUPERADMIN_ID";
        private const string RoutingSuperAdminNameKey = "ROUTING_SUPERADMIN_NAME";
        private const string TypeKey = "TYPE";
        private const string SlotLocalKey = "SLOT_LOCAL";
        private const string PendingSuperAdminStatus = "PENDING_SUPERADMIN";
        private const string AssignedStatus = "ASSIGNED";

        private readonly ApplicationDbContext _context;
        private readonly IAuditLogService _auditLogService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IVenteService _venteService;

        public ShopService(
            ApplicationDbContext context,
            IAuditLogService auditLogService,
            UserManager<ApplicationUser> userManager,
            IVenteService venteService)
        {
            _context = context;
            _auditLogService = auditLogService;
            _userManager = userManager;
            _venteService = venteService;
        }


        public async Task<ShopIndexData> GetSoldeDataAsync(ShopFilter filter)
        {
            return await GetIndexDataAsync(NormalizeFilter(filter, saleOnly: true));
        }

        public async Task<ShopIndexData> GetIndexDataAsync(ShopFilter filter)
        {
            filter = NormalizeFilter(filter);

            var biensQuery = _context.Biens
                .Include(b => b.User)
                .Include(b => b.Images)
                .Where(b => b.IsPublished || b.PublicationStatus == "Publié")
                .Where(b =>
                    b.DiscountPercent > 0
                    || string.IsNullOrEmpty(b.TypeTransaction)
                    || b.TypeTransaction == "A Vendre"
                    || b.TypeTransaction == "A Louer")
                .AsQueryable();

            if (!string.IsNullOrEmpty(filter.Titre))
            {
                biensQuery = biensQuery.Where(b => b.Titre.Contains(filter.Titre));
            }

            if (!string.IsNullOrEmpty(filter.Adresse))
            {
                biensQuery = biensQuery.Where(b => b.Adresse != null && b.Adresse.Contains(filter.Adresse));
            }

            if (filter.PrixMin.HasValue)
            {
                biensQuery = biensQuery.Where(b => b.Prix >= filter.PrixMin.Value);
            }

            if (filter.PrixMax.HasValue)
            {
                biensQuery = biensQuery.Where(b => b.Prix <= filter.PrixMax.Value);
            }

            if (filter.SurfaceMin.HasValue)
            {
                biensQuery = biensQuery.Where(b => b.Surface.HasValue && b.Surface.Value >= filter.SurfaceMin.Value);
            }

            if (filter.SurfaceMax.HasValue)
            {
                biensQuery = biensQuery.Where(b => b.Surface.HasValue && b.Surface.Value <= filter.SurfaceMax.Value);
            }

            if (!string.IsNullOrWhiteSpace(filter.Type) && TypeOptions.Contains(filter.Type))
            {
                biensQuery = biensQuery.Where(b => b.TypeTransaction == filter.Type);
            }

            if (!string.IsNullOrWhiteSpace(filter.Statut) && StatusOptions.Contains(filter.Statut))
            {
                biensQuery = biensQuery.Where(b => b.StatutCommercial == filter.Statut);
            }

            if (!string.IsNullOrWhiteSpace(filter.Solde) && filter.Solde.Equals("1"))
            {
                biensQuery = biensQuery.Where(b => b.DiscountPercent > 0m);
            }

            var biens = await biensQuery.ToListAsync();
            var routingByBien = await ResolveRoutingByBienAsync(biens);
            var visitSlotsByBien = await BuildAvailableVisitSlotsByBienAsync(routingByBien);
            var agentDisplayByBien = routingByBien.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.SchedulingAgentDisplayName);

            return new ShopIndexData
            {
                Biens = biens,
                Filter = filter,
                Types = TypeOptions,
                Statuses = StatusOptions,
                AvailableVisitSlotsByBien = visitSlotsByBien,
                AgentDisplayByBien = agentDisplayByBien
            };
        }

        public async Task<ServiceResult> ExpressInterestAsync(int bienId, string userId)
        {
            var contextResult = await GetActionContextAsync(bienId, userId, requireAvailableStatus: false);
            if (!contextResult.Success)
            {
                return contextResult.Error!;
            }

            var user = contextResult.User!;
            var bien = contextResult.Bien!;

            var subject = $"{InterestSubjectPrefix}{bien.Id}";
            var interestNeedle = $"user_id={user.Id}".ToLowerInvariant();
            var hasRecentInterest = await _context.Messages.AnyAsync(m =>
                m.Sujet == subject
                && m.DateCreation >= DateTime.Now.AddDays(-14)
                && (m.Contenu ?? string.Empty).ToLower().Contains(interestNeedle));

            if (hasRecentInterest)
            {
                return ServiceResult.Fail(ServiceErrorCode.Conflict, "Vous avez déjà signalé votre intérêt récemment pour ce bien.");
            }

            _context.Messages.Add(CreateShopMessage(
                user,
                subject,
                BuildMessageBody(
                    "INTERET",
                    bien,
                    user.Id,
                    null,
                    "Je suis intéressé par ce bien. Merci de me recontacter."),
                "Agent"));

            await _context.SaveChangesAsync();
            await _auditLogService.LogAsync(
                user.Id,
                "Interest",
                "BienImmobilier",
                bien.Id,
                $"Intérêt signalé pour '{bien.Titre}'");

            return ServiceResult.Ok("Votre intérêt a été enregistré. Un agent vous contactera bientôt.");
        }

        private static ShopFilter NormalizeFilter(ShopFilter? filter, bool saleOnly = false)
        {
            filter ??= new ShopFilter();

            if (saleOnly)
            {
                filter.Solde = "1";
            }

            return filter;
        }

        public async Task<ServiceResult> ReserveVisitAsync(int bienId, DateTime visitSlot, string userId)
        {
            var contextResult = await GetActionContextAsync(bienId, userId, requireAvailableStatus: true);
            if (!contextResult.Success)
            {
                return contextResult.Error!;
            }

            var user = contextResult.User!;
            var bien = contextResult.Bien!;

            var routing = await ResolveRoutingAsync(bien);
            var slotToken = ToSlotToken(visitSlot);
            var availableSlots = await GetAvailableVisitSlotsAsync(routing);
            if (!availableSlots.Any(s => ToSlotToken(s) == slotToken))
            {
                return ServiceResult.Fail(ServiceErrorCode.Conflict, "Le créneau choisi n'est plus disponible.");
            }

            _context.Messages.Add(CreateShopMessage(
                user,
                $"{VisitSubjectPrefix}{bien.Id}",
                BuildMessageBody(
                    "VISITE",
                    bien,
                    user.Id,
                    visitSlot,
                    $"Je souhaite réserver une visite pour le {visitSlot:dd/MM/yyyy à HH:mm}.",
                    routing.Metadata),
                routing.Destinataire));

            await _context.SaveChangesAsync();
            await _auditLogService.LogAsync(
                user.Id,
                "Visit",
                "BienImmobilier",
                bien.Id,
                $"Visite réservée pour '{bien.Titre}' le {visitSlot:dd/MM/yyyy HH:mm}");

            return ServiceResult.Ok("Votre demande de visite a été envoyée à l'agent responsable.");
        }

        public async Task<ServiceResult> RequestAgentMeetingAsync(int bienId, DateTime meetingDateTime, string userId)
        {
            var contextResult = await GetActionContextAsync(bienId, userId, requireAvailableStatus: false);
            if (!contextResult.Success)
            {
                return contextResult.Error!;
            }

            var user = contextResult.User!;
            var bien = contextResult.Bien!;

            if (meetingDateTime <= DateTime.Now.AddMinutes(30))
            {
                return ServiceResult.Fail(ServiceErrorCode.BadRequest, "Veuillez sélectionner une date de rendez-vous future.");
            }

            var routing = await ResolveRoutingAsync(bien);
            var availableSlots = await GetAvailableVisitSlotsAsync(routing);
            var meetingToken = ToSlotToken(meetingDateTime);
            if (!availableSlots.Any(s => ToSlotToken(s) == meetingToken))
            {
                return ServiceResult.Fail(ServiceErrorCode.Conflict, "Le créneau choisi n'est plus disponible pour cet agent.");
            }

            var subject = $"{MeetingSubjectPrefix}{bien.Id}";
            var userNeedle = $"user_id={user.Id}".ToLowerInvariant();
            var slotNeedle = $"slot_local={meetingToken}".ToLowerInvariant();
            var duplicate = await _context.Messages.AnyAsync(m =>
                m.Sujet == subject
                && (m.Contenu ?? string.Empty).ToLower().Contains(userNeedle)
                && (m.Contenu ?? string.Empty).ToLower().Contains(slotNeedle));

            if (duplicate)
            {
                return ServiceResult.Fail(ServiceErrorCode.Conflict, "Vous avez déjà demandé ce rendez-vous.");
            }

            _context.Messages.Add(CreateShopMessage(
                user,
                subject,
                BuildMessageBody(
                    "RDV_AGENT",
                    bien,
                    user.Id,
                    meetingDateTime,
                    $"Je souhaite un rendez-vous avec un agent le {meetingDateTime:dd/MM/yyyy à HH:mm}.",
                    routing.Metadata),
                routing.Destinataire));

            await _context.SaveChangesAsync();
            await _auditLogService.LogAsync(
                user.Id,
                "Meeting",
                "BienImmobilier",
                bien.Id,
                $"Demande de rendez-vous agent pour '{bien.Titre}' le {meetingDateTime:dd/MM/yyyy HH:mm}");

            return ServiceResult.Ok("Votre demande de rendez-vous avec un agent a été envoyée à l'agent responsable.");
        }

        private async Task<IReadOnlyDictionary<int, RoutingDecision>> ResolveRoutingByBienAsync(IEnumerable<BienImmobilier> biens)
        {
            var result = new Dictionary<int, RoutingDecision>();
            foreach (var bien in biens)
            {
                result[bien.Id] = await ResolveRoutingAsync(bien);
            }

            return result;
        }

        private async Task<IReadOnlyDictionary<int, IReadOnlyList<DateTime>>> BuildAvailableVisitSlotsByBienAsync(
            IReadOnlyDictionary<int, RoutingDecision> routingByBien)
        {
            var result = new Dictionary<int, IReadOnlyList<DateTime>>();
            if (routingByBien.Count == 0)
            {
                return result;
            }

            var schedulingAgentIds = routingByBien.Values
                .Select(v => v.SchedulingAgentUserId)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var bookedByAgent = await GetBookedSlotsByAgentAsync(schedulingAgentIds);

            foreach (var pair in routingByBien)
            {
                var routing = pair.Value;
                HashSet<string>? bookedTokens = null;
                if (!string.IsNullOrWhiteSpace(routing.SchedulingAgentUserId))
                {
                    bookedByAgent.TryGetValue(routing.SchedulingAgentUserId, out bookedTokens);
                }

                bookedTokens ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                result[pair.Key] = GenerateAvailableVisitSlots(bookedTokens);
            }

            return result;
        }

        private async Task<IReadOnlyList<DateTime>> GetAvailableVisitSlotsAsync(RoutingDecision routing)
        {
            HashSet<string>? bookedTokens = null;
            if (!string.IsNullOrWhiteSpace(routing.SchedulingAgentUserId))
            {
                var bookedByAgent = await GetBookedSlotsByAgentAsync(new[] { routing.SchedulingAgentUserId });
                bookedByAgent.TryGetValue(routing.SchedulingAgentUserId, out bookedTokens);
            }

            bookedTokens ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return GenerateAvailableVisitSlots(bookedTokens);
        }

        private async Task<Dictionary<string, HashSet<string>>> GetBookedSlotsByAgentAsync(IReadOnlyCollection<string> agentIds)
        {
            var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            if (agentIds.Count == 0)
            {
                return result;
            }

            var targetIds = new HashSet<string>(agentIds, StringComparer.OrdinalIgnoreCase);
            var messages = await _context.Messages
                .Where(m => m.Contenu != null && (m.Contenu.Contains("TYPE=VISITE") || m.Contenu.Contains("TYPE=RDV_AGENT")))
                .Select(m => new { m.Contenu })
                .ToListAsync();

            foreach (var message in messages)
            {
                var type = ParseMetadata(message.Contenu, TypeKey);
                if (!string.Equals(type, "VISITE", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(type, "RDV_AGENT", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var slotToken = ParseMetadata(message.Contenu, SlotLocalKey);
                if (string.IsNullOrWhiteSpace(slotToken))
                {
                    continue;
                }

                var responsibleAgentId = ParseResponsibleAgentId(message.Contenu);
                if (string.IsNullOrWhiteSpace(responsibleAgentId) || !targetIds.Contains(responsibleAgentId))
                {
                    continue;
                }

                if (!result.TryGetValue(responsibleAgentId, out var bookedSlots))
                {
                    bookedSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    result[responsibleAgentId] = bookedSlots;
                }

                bookedSlots.Add(slotToken);
            }

            return result;
        }

        private static List<DateTime> GenerateAvailableVisitSlots(HashSet<string> bookedSlotTokens)
        {
            var now = DateTime.Now;
            var available = new List<DateTime>();

            for (var dayOffset = 0; dayOffset < 10; dayOffset++)
            {
                var day = now.Date.AddDays(dayOffset);
                if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                {
                    continue;
                }

                foreach (var hour in VisitSlotHours)
                {
                    var slot = day.AddHours(hour);
                    if (slot <= now.AddHours(2))
                    {
                        continue;
                    }

                    if (!bookedSlotTokens.Contains(ToSlotToken(slot)))
                    {
                        available.Add(slot);
                    }
                }
            }

            return available;
        }

        private async Task<(bool Success, ServiceResult? Error, ApplicationUser? User, BienImmobilier? Bien)> GetActionContextAsync(
            int bienId,
            string userId,
            bool requireAvailableStatus)
        {
            var user = await _userManager.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
            {
                return (false, ServiceResult.Fail(ServiceErrorCode.Validation, "Utilisateur introuvable."), null, null);
            }

            var bien = await _context.Biens.Include(b => b.User).FirstOrDefaultAsync(b => b.Id == bienId);
            if (bien == null)
            {
                return (false, ServiceResult.Fail(ServiceErrorCode.NotFound, "Bien introuvable."), null, null);
            }

            if (bien.UserId == userId)
            {
                return (false, ServiceResult.Fail(ServiceErrorCode.Conflict, "Vous êtes déjà le propriétaire de ce bien."), null, null);
            }

            if (bien.PublicationStatus != "Publié")
            {
                return (false, ServiceResult.Fail(ServiceErrorCode.Conflict, "Ce bien n'est pas publié."), null, null);
            }

            if (bien.StatutCommercial == "Vendu")
            {
                return (false, ServiceResult.Fail(ServiceErrorCode.Conflict, "Ce bien n'est plus disponible."), null, null);
            }

            if (requireAvailableStatus && bien.StatutCommercial != "Disponible")
            {
                return (false, ServiceResult.Fail(ServiceErrorCode.Conflict, "Ce bien n'est pas disponible pour une visite."), null, null);
            }

            return (true, null, user, bien);
        }

        private async Task<RoutingDecision> ResolveRoutingAsync(BienImmobilier bien)
        {
            if (!string.IsNullOrWhiteSpace(bien.UserId))
            {
                var owner = await _userManager.Users.FirstOrDefaultAsync(u => u.Id == bien.UserId);
                if (owner != null)
                {
                    if (await _userManager.IsInRoleAsync(owner, "SuperAdmin"))
                    {
                        return BuildDirectOwnerDecision(owner, "SuperAdmin");
                    }

                    if (await _userManager.IsInRoleAsync(owner, "Admin"))
                    {
                        return BuildDirectOwnerDecision(owner, "Admin");
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(bien.PublicationValidatedByAdminId))
            {
                var validatorAdmin = await _userManager.Users
                    .FirstOrDefaultAsync(u => u.Id == bien.PublicationValidatedByAdminId);
                if (validatorAdmin != null)
                {
                    if (await _userManager.IsInRoleAsync(validatorAdmin, "Admin"))
                    {
                        return BuildDirectOwnerDecision(validatorAdmin, "Admin", "VALIDATOR_ADMIN");
                    }

                    if (await _userManager.IsInRoleAsync(validatorAdmin, "SuperAdmin"))
                    {
                        return BuildDirectOwnerDecision(validatorAdmin, "SuperAdmin", "VALIDATOR_ADMIN");
                    }
                }
            }

            var fallbackAdmin = await FindFallbackAdminAsync();
            if (fallbackAdmin != null)
            {
                var fallbackRole = await _userManager.IsInRoleAsync(fallbackAdmin, "SuperAdmin")
                    ? "SuperAdmin"
                    : "Admin";

                return BuildDirectOwnerDecision(fallbackAdmin, fallbackRole, "FALLBACK_ADMIN");
            }

            var superAdmin = await FindSuperAdminAsync();
            if (superAdmin != null)
            {
                return BuildDirectOwnerDecision(superAdmin, "SuperAdmin", "FALLBACK_SUPERADMIN");
            }

            return new RoutingDecision
            {
                Destinataire = "Administration",
                NeedsSuperAdminDispatch = false,
                SchedulingAgentDisplayName = "Administration",
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [AssignmentModeKey] = "NO_AGENT_AVAILABLE",
                    [AssignmentStatusKey] = AssignedStatus
                }
            };
        }

        private async Task<ApplicationUser?> FindSuperAdminAsync()
        {
            var users = await _userManager.Users.OrderBy(u => u.UserName).ToListAsync();
            foreach (var user in users)
            {
                if (await _userManager.IsInRoleAsync(user, "SuperAdmin"))
                {
                    return user;
                }
            }

            return null;
        }

        private async Task<ApplicationUser?> FindFallbackAdminAsync()
        {
            var users = await _userManager.Users.OrderBy(u => u.UserName).ToListAsync();
            ApplicationUser? firstAdmin = null;

            foreach (var user in users)
            {
                var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
                if (!isAdmin)
                {
                    continue;
                }

                var isSuperAdmin = await _userManager.IsInRoleAsync(user, "SuperAdmin");
                if (!isSuperAdmin)
                {
                    return user;
                }

                firstAdmin ??= user;
            }

            return firstAdmin;
        }

        private static RoutingDecision BuildDirectOwnerDecision(ApplicationUser owner, string ownerRole, string assignmentMode = "DIRECT_OWNER")
        {
            return new RoutingDecision
            {
                Destinataire = ownerRole,
                NeedsSuperAdminDispatch = false,
                SchedulingAgentUserId = owner.Id,
                SchedulingAgentDisplayName = ResolveDisplayName(owner),
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [AssignmentModeKey] = assignmentMode,
                    [AssignmentStatusKey] = AssignedStatus,
                    [AssignedToUserIdKey] = owner.Id,
                    [AssignedToNameKey] = ResolveDisplayName(owner),
                    [AssignedToRoleKey] = ownerRole
                }
            };
        }

        private static Message CreateShopMessage(ApplicationUser user, string subject, string body, string destinataire)
        {
            return new Message
            {
                NomUtilisateur = string.IsNullOrWhiteSpace(user.Nom) ? user.UserName : user.Nom,
                Email = user.Email,
                Sujet = subject,
                Contenu = body,
                Destinataire = destinataire,
                Statut = "Nouveau",
                DateCreation = DateTime.Now
            };
        }

        private static string BuildMessageBody(
            string type,
            BienImmobilier bien,
            string userId,
            DateTime? slot,
            string userText,
            IReadOnlyDictionary<string, string>? additionalMetadata = null)
        {
            var slotLine = slot.HasValue ? $"SLOT_LOCAL={ToSlotToken(slot.Value)}" : "SLOT_LOCAL=";
            var metadataLines = new List<string>
            {
                $"TYPE={type}",
                $"BIEN_ID={bien.Id}",
                $"USER_ID={userId}",
                slotLine,
                $"BIEN_TITRE={NormalizeMetadataValue(bien.Titre)}"
            };

            if (additionalMetadata != null)
            {
                foreach (var metadata in additionalMetadata)
                {
                    metadataLines.Add($"{metadata.Key}={NormalizeMetadataValue(metadata.Value)}");
                }
            }

            return $"{userText}\n\n---\n{string.Join("\n", metadataLines)}";
        }

        private static string? ParseMetadata(string? content, string key)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return null;
            }

            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var line in lines)
            {
                if (line.StartsWith($"{key}=", StringComparison.OrdinalIgnoreCase))
                {
                    return line[(key.Length + 1)..].Trim();
                }
            }

            return null;
        }

        private static string ToSlotToken(DateTime value)
        {
            return value.ToString("yyyy-MM-ddTHH:mm");
        }

        private static string? ParseResponsibleAgentId(string? content)
        {
            var assignedUserId = ParseMetadata(content, AssignedToUserIdKey);
            if (!string.IsNullOrWhiteSpace(assignedUserId))
            {
                return assignedUserId;
            }

            var assignmentStatus = ParseMetadata(content, AssignmentStatusKey);
            if (string.Equals(assignmentStatus, PendingSuperAdminStatus, StringComparison.OrdinalIgnoreCase))
            {
                return ParseMetadata(content, RoutingSuperAdminIdKey);
            }

            return null;
        }

        private static string ResolveDisplayName(ApplicationUser user)
        {
            if (!string.IsNullOrWhiteSpace(user.Nom))
            {
                return user.Nom;
            }

            if (!string.IsNullOrWhiteSpace(user.UserName))
            {
                return user.UserName;
            }

            if (!string.IsNullOrWhiteSpace(user.Email))
            {
                return user.Email;
            }

            return user.Id;
        }

        private static string NormalizeMetadataValue(string? value)
        {
            return (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        }

        private sealed class RoutingDecision
        {
            public string Destinataire { get; init; } = "Administration";
            public bool NeedsSuperAdminDispatch { get; init; }
            public string? SchedulingAgentUserId { get; init; }
            public string SchedulingAgentDisplayName { get; init; } = "Agent";
            public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
        }
    }
}
