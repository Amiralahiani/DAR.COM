using Microsoft.EntityFrameworkCore;
using RealEstateAdmin.Data;
using RealEstateAdmin.Models;

namespace RealEstateAdmin.Services
{
    public class AgendaService : IAgendaService
    {
        private const string TypeKey = "TYPE";
        private const string SlotKey = "SLOT_LOCAL";
        private const string BienIdKey = "BIEN_ID";
        private const string BienTitreKey = "BIEN_TITRE";
        private const string AssignmentStatusKey = "ASSIGNMENT_STATUS";
        private const string AssignedToUserIdKey = "ASSIGNED_TO_USER_ID";
        private const string AssignedToNameKey = "ASSIGNED_TO_NAME";
        private const string PendingSuperAdminStatus = "PENDING_SUPERADMIN";

        private readonly ApplicationDbContext _context;
        private readonly IAuditLogService _auditLogService;

        public AgendaService(ApplicationDbContext context, IAuditLogService auditLogService)
        {
            _context = context;
            _auditLogService = auditLogService;
        }

        public async Task<AgendaIndexData> GetAgendaAsync(string actorUserId, bool actorIsSuperAdmin)
        {
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return new AgendaIndexData();
            }

            var messages = await _context.Messages
                .OrderBy(m => m.DateCreation)
                .ToListAsync();

            var events = new List<AgendaEvent>();

            foreach (var message in messages)
            {
                var projection = BuildProjection(message);
                if (projection == null)
                {
                    continue;
                }

                if (!CanActorSeeEvent(actorUserId, actorIsSuperAdmin, projection.AssignedToUserId, projection.AssignmentStatus))
                {
                    continue;
                }

                events.Add(projection.Event);
            }

            var now = DateTime.Now;
            var ordered = events.OrderBy(e => e.Slot).ToList();
            var upcoming = ordered.Where(e => e.Slot >= now).ToList();
            var today = upcoming.Where(e => e.Slot.Date == now.Date).ToList();
            var past = ordered.Where(e => e.Slot < now).OrderByDescending(e => e.Slot).Take(20).ToList();

            return new AgendaIndexData
            {
                UpcomingEvents = upcoming,
                TodayEvents = today,
                PastEvents = past,
                TotalUpcoming = upcoming.Count,
                TotalToday = today.Count
            };
        }

        public async Task<ServiceResult<AgendaEventDetails>> GetEventDetailsAsync(int messageId, string actorUserId, bool actorIsSuperAdmin)
        {
            var message = await _context.Messages.FirstOrDefaultAsync(m => m.Id == messageId);
            if (message == null)
            {
                return ServiceResult<AgendaEventDetails>.Fail(ServiceErrorCode.NotFound, "Événement introuvable.");
            }

            var projection = BuildProjection(message);
            if (projection == null)
            {
                return ServiceResult<AgendaEventDetails>.Fail(ServiceErrorCode.BadRequest, "Ce message n'est pas un événement d'agenda.");
            }

            if (!CanActorSeeEvent(actorUserId, actorIsSuperAdmin, projection.AssignedToUserId, projection.AssignmentStatus))
            {
                return ServiceResult<AgendaEventDetails>.Fail(ServiceErrorCode.Forbidden, "Accès refusé.");
            }

            return ServiceResult<AgendaEventDetails>.Ok(new AgendaEventDetails
            {
                MessageId = projection.Event.MessageId,
                EventType = projection.Event.EventType,
                Slot = projection.Event.Slot,
                BienId = projection.Event.BienId,
                BienTitre = projection.Event.BienTitre,
                ClientName = projection.Event.ClientName,
                ClientEmail = projection.Event.ClientEmail,
                AssigneeName = projection.Event.AssigneeName,
                AssignmentStatus = projection.Event.AssignmentStatus,
                Statut = projection.Event.Statut,
                DemandeTexte = ExtractUserText(message.Contenu),
                DateCreation = message.DateCreation,
                DateTraitement = message.DateTraitement
            });
        }

        public async Task<ServiceResult> MarkEventTreatedAsync(int messageId, string actorUserId, bool actorIsSuperAdmin)
        {
            var message = await _context.Messages.FirstOrDefaultAsync(m => m.Id == messageId);
            if (message == null)
            {
                return ServiceResult.Fail(ServiceErrorCode.NotFound, "Événement introuvable.");
            }

            var projection = BuildProjection(message);
            if (projection == null)
            {
                return ServiceResult.Fail(ServiceErrorCode.BadRequest, "Ce message n'est pas un événement d'agenda.");
            }

            if (!CanActorSeeEvent(actorUserId, actorIsSuperAdmin, projection.AssignedToUserId, projection.AssignmentStatus))
            {
                return ServiceResult.Fail(ServiceErrorCode.Forbidden, "Accès refusé.");
            }

            if (message.Statut == "Traité")
            {
                return ServiceResult.Ok("Cet événement est déjà traité.");
            }

            message.Statut = "Traité";
            message.DateTraitement = DateTime.Now;
            message.TraiteParId = actorUserId;
            _context.Update(message);
            await _context.SaveChangesAsync();
            await _auditLogService.LogAsync(actorUserId, "Update", "Message", message.Id, "Événement agenda marqué traité.");

            return ServiceResult.Ok("Événement marqué comme traité.");
        }

        private static bool CanActorSeeEvent(
            string actorUserId,
            bool actorIsSuperAdmin,
            string? assignedToUserId,
            string assignmentStatus)
        {
            if (actorIsSuperAdmin)
            {
                return true;
            }

            if (string.Equals(assignmentStatus, PendingSuperAdminStatus, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(assignedToUserId)
                && string.Equals(assignedToUserId, actorUserId, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAgendaEventType(string? type)
        {
            return string.Equals(type, "VISITE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "RDV_AGENT", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryParseSlot(string? slotToken, out DateTime slot)
        {
            if (!string.IsNullOrWhiteSpace(slotToken) && DateTime.TryParse(slotToken, out slot))
            {
                return true;
            }

            slot = default;
            return false;
        }

        private static AgendaProjection? BuildProjection(Message message)
        {
            var type = ParseMetadata(message.Contenu, TypeKey);
            if (!IsAgendaEventType(type))
            {
                return null;
            }

            var slotToken = ParseMetadata(message.Contenu, SlotKey);
            if (!TryParseSlot(slotToken, out var slot))
            {
                return null;
            }

            var assignmentStatus = ParseMetadata(message.Contenu, AssignmentStatusKey) ?? string.Empty;
            var assignedToUserId = ParseMetadata(message.Contenu, AssignedToUserIdKey);

            int? bienId = null;
            var bienIdRaw = ParseMetadata(message.Contenu, BienIdKey);
            if (int.TryParse(bienIdRaw, out var parsedBienId))
            {
                bienId = parsedBienId;
            }

            var assignee = ParseMetadata(message.Contenu, AssignedToNameKey);
            if (string.IsNullOrWhiteSpace(assignee) && string.Equals(assignmentStatus, PendingSuperAdminStatus, StringComparison.OrdinalIgnoreCase))
            {
                assignee = "SuperAdmin (en attente d'assignation)";
            }

            return new AgendaProjection(
                new AgendaEvent
                {
                    MessageId = message.Id,
                    EventType = string.Equals(type, "VISITE", StringComparison.OrdinalIgnoreCase) ? "Visite" : "Rendez-vous",
                    Slot = slot,
                    BienId = bienId,
                    BienTitre = ParseMetadata(message.Contenu, BienTitreKey) ?? "-",
                    ClientName = message.NomUtilisateur ?? "Client",
                    ClientEmail = message.Email ?? string.Empty,
                    AssigneeName = assignee ?? "-",
                    AssignmentStatus = assignmentStatus,
                    Statut = message.Statut
                },
                assignedToUserId,
                assignmentStatus);
        }

        private static string ExtractUserText(string? content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return string.Empty;
            }

            var separator = content.IndexOf("\n\n---", StringComparison.Ordinal);
            if (separator > -1)
            {
                return content[..separator].Trim();
            }

            return content.Trim();
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

        private sealed record AgendaProjection(AgendaEvent Event, string? AssignedToUserId, string AssignmentStatus);
    }
}
