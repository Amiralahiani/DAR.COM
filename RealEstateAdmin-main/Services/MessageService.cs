using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using RealEstateAdmin.Data;
using RealEstateAdmin.Models;

namespace RealEstateAdmin.Services
{
    public class MessageService : IMessageService
    {
        private const string VisitSubjectPrefix = "Réservation visite - Bien #";
        private const string MeetingSubjectPrefix = "Rendez-vous agent - Bien #";
        private const string AssignmentStatusKey = "ASSIGNMENT_STATUS";
        private const string AssignedToUserIdKey = "ASSIGNED_TO_USER_ID";
        private const string AssignedToNameKey = "ASSIGNED_TO_NAME";
        private const string AssignedToRoleKey = "ASSIGNED_TO_ROLE";
        private const string AssignedByKey = "ASSIGNED_BY_SUPERADMIN_ID";
        private const string AssignedAtKey = "ASSIGNED_AT_LOCAL";
        private const string PendingSuperAdminStatus = "PENDING_SUPERADMIN";

        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEmailSender _emailSender;
        private readonly PdfExportService _pdfExportService;
        private readonly IAuditLogService _auditLogService;

        public MessageService(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IEmailSender emailSender,
            PdfExportService pdfExportService,
            IAuditLogService auditLogService)
        {
            _context = context;
            _userManager = userManager;
            _emailSender = emailSender;
            _pdfExportService = pdfExportService;
            _auditLogService = auditLogService;
        }

        public async Task<IReadOnlyList<Message>> GetMessagesAsync(string? actorUserId, bool actorIsSuperAdmin)
        {
            var messages = await _context.Messages
                .AsNoTracking()
                .OrderBy(m => m.Statut == "Nouveau" ? 0 : 1)
                .ThenByDescending(m => m.DateCreation)
                .ToListAsync();

            foreach (var message in messages)
            {
                message.Statut = NormalizeMessageStatus(message.Statut, IsVisitOrMeetingMessage(message));
            }

            if (actorIsSuperAdmin || string.IsNullOrWhiteSpace(actorUserId))
            {
                return messages;
            }

            return messages
                .Where(m => CanBeHandledByAdmin(m, actorUserId))
                .ToList();
        }

        public async Task<IReadOnlyList<UserRoleViewModel>> GetAssignableAdminsAsync()
        {
            var users = await _userManager.Users.OrderBy(u => u.UserName).ToListAsync();
            var admins = new List<UserRoleViewModel>();

            foreach (var user in users)
            {
                if (!await _userManager.IsInRoleAsync(user, "Admin"))
                {
                    continue;
                }

                if (await _userManager.IsInRoleAsync(user, "SuperAdmin"))
                {
                    continue;
                }

                admins.Add(new UserRoleViewModel
                {
                    UserId = user.Id,
                    UserName = user.UserName ?? user.Email ?? user.Id,
                    Email = user.Email ?? string.Empty,
                    CurrentRole = "Admin",
                    EmailConfirmed = user.EmailConfirmed,
                    IsSuspended = user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow
                });
            }

            return admins;
        }

        public async Task<ServiceResult> AssignToAdminAsync(int messageId, string adminUserId, string? actorUserId, bool actorIsSuperAdmin)
        {
            if (!actorIsSuperAdmin)
            {
                return ServiceResult.Fail(ServiceErrorCode.Forbidden, "Seul le SuperAdmin peut assigner ces demandes.");
            }

            if (string.IsNullOrWhiteSpace(adminUserId))
            {
                return ServiceResult.Fail(ServiceErrorCode.BadRequest, "Veuillez sélectionner un admin.");
            }

            var message = await _context.Messages.FirstOrDefaultAsync(m => m.Id == messageId);
            if (message == null)
            {
                return ServiceResult.Fail(ServiceErrorCode.NotFound, "Message introuvable.");
            }

            if (!IsVisitOrMeetingMessage(message))
            {
                return ServiceResult.Fail(ServiceErrorCode.BadRequest, "Ce message n'est pas une demande de visite ou de rendez-vous.");
            }

            var currentStatus = ParseMetadata(message.Contenu, AssignmentStatusKey);
            if (!string.Equals(currentStatus, PendingSuperAdminStatus, StringComparison.OrdinalIgnoreCase))
            {
                return ServiceResult.Fail(ServiceErrorCode.Conflict, "Cette demande n'est pas en attente d'assignation SuperAdmin.");
            }

            var targetAdmin = await _userManager.Users.FirstOrDefaultAsync(u => u.Id == adminUserId);
            if (targetAdmin == null)
            {
                return ServiceResult.Fail(ServiceErrorCode.NotFound, "Admin introuvable.");
            }

            if (!await _userManager.IsInRoleAsync(targetAdmin, "Admin"))
            {
                return ServiceResult.Fail(ServiceErrorCode.BadRequest, "Le compte sélectionné n'est pas un admin.");
            }

            if (await _userManager.IsInRoleAsync(targetAdmin, "SuperAdmin"))
            {
                return ServiceResult.Fail(ServiceErrorCode.BadRequest, "Cette action attend un compte Admin (hors SuperAdmin).");
            }

            if (targetAdmin.LockoutEnd.HasValue && targetAdmin.LockoutEnd.Value > DateTimeOffset.UtcNow)
            {
                return ServiceResult.Fail(ServiceErrorCode.Conflict, "Cet admin est suspendu et ne peut pas recevoir de nouvelles affectations.");
            }

            var slotToken = ParseMetadata(message.Contenu, "SLOT_LOCAL");
            if (!string.IsNullOrWhiteSpace(slotToken))
            {
                var candidates = await _context.Messages
                    .Where(m => m.Id != messageId
                                && m.Contenu != null
                                && m.Contenu.Contains($"SLOT_LOCAL={slotToken}")
                                && (m.Contenu.Contains("TYPE=VISITE") || m.Contenu.Contains("TYPE=RDV_AGENT")))
                    .Select(m => m.Contenu)
                    .ToListAsync();

                var hasConflict = candidates.Any(content =>
                    string.Equals(ParseMetadata(content, AssignedToUserIdKey), targetAdmin.Id, StringComparison.OrdinalIgnoreCase));
                if (hasConflict)
                {
                    return ServiceResult.Fail(ServiceErrorCode.Conflict, "Cet admin a déjà un rendez-vous sur ce créneau.");
                }
            }

            var now = DateTime.Now;
            message.Contenu = UpsertMetadata(message.Contenu, AssignmentStatusKey, "ASSIGNED");
            message.Contenu = UpsertMetadata(message.Contenu, AssignedToUserIdKey, targetAdmin.Id);
            message.Contenu = UpsertMetadata(message.Contenu, AssignedToNameKey, ResolveDisplayName(targetAdmin));
            message.Contenu = UpsertMetadata(message.Contenu, AssignedToRoleKey, "Admin");
            message.Contenu = UpsertMetadata(message.Contenu, AssignedByKey, actorUserId ?? string.Empty);
            message.Contenu = UpsertMetadata(message.Contenu, AssignedAtKey, now.ToString("yyyy-MM-ddTHH:mm"));
            message.Destinataire = "Admin";

            _context.Update(message);
            await _context.SaveChangesAsync();
            await _auditLogService.LogAsync(
                actorUserId,
                "Assign",
                "Message",
                message.Id,
                $"Demande assignée à l'admin '{ResolveDisplayName(targetAdmin)}'.");

            return ServiceResult.Ok("Demande assignée avec succès.");
        }

        public async Task<string> ExportCsvAsync()
        {
            var messages = await _context.Messages.OrderByDescending(m => m.DateCreation).ToListAsync();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Id;Nom;Email;Sujet;Destinataire;Statut;Date");
            foreach (var m in messages)
            {
                sb.AppendLine($"{m.Id};\"{m.NomUtilisateur}\";\"{m.Email}\";\"{m.Sujet}\";\"{m.Destinataire}\";{m.Statut};{m.DateCreation:dd/MM/yyyy HH:mm}");
            }

            return sb.ToString();
        }

        public async Task<byte[]> ExportPdfAsync()
        {
            var messages = await _context.Messages.OrderByDescending(m => m.DateCreation).ToListAsync();
            return _pdfExportService.GenerateMessagesPdf(messages);
        }

        public async Task<ServiceResult> CreateAsync(Message message, string? actorUserId)
        {
            if (message.Destinataire != "Administration" && message.Destinataire != "Agent")
            {
                message.Destinataire = "Administration";
            }

            message.DateCreation = DateTime.Now;
            message.Statut = "Nouveau";
            _context.Add(message);
            await _context.SaveChangesAsync();
            await _auditLogService.LogAsync(actorUserId, "Create", "Message", message.Id, $"Nouveau message ({message.Destinataire}): {message.Sujet}");

            return ServiceResult.Ok("Votre message a été envoyé avec succès. Nous vous répondrons bientôt.");
        }

        public async Task<Message?> GetByIdAsync(int id)
        {
            var message = await _context.Messages.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id);
            if (message == null)
            {
                return null;
            }

            message.Statut = NormalizeMessageStatus(message.Statut, IsVisitOrMeetingMessage(message));
            return message;
        }

        public async Task<ServiceResult> DeleteAsync(int id, string? actorUserId)
        {
            var message = await _context.Messages.FindAsync(id);
            if (message == null)
            {
                return ServiceResult.Fail(ServiceErrorCode.NotFound, "Message introuvable.");
            }

            _context.Messages.Remove(message);
            await _context.SaveChangesAsync();
            await _auditLogService.LogAsync(actorUserId, "Delete", "Message", message.Id, $"Suppression du message: {message.Sujet}");
            return ServiceResult.Ok();
        }

        public async Task<ServiceResult> ReplyAsync(int messageId, string reponse, string? actorUserId)
        {
            var message = await _context.Messages.FindAsync(messageId);
            if (message == null)
            {
                return ServiceResult.Fail(ServiceErrorCode.NotFound, "Message introuvable.");
            }

            if (!string.IsNullOrWhiteSpace(message.Email) && !string.IsNullOrWhiteSpace(reponse))
            {
                var subject = string.IsNullOrWhiteSpace(message.Sujet) ? "Réponse à votre message" : $"Re: {message.Sujet}";
                await _emailSender.SendEmailAsync(message.Email, subject, reponse.Replace("\n", "<br/>"));
            }

            if (!IsVisitOrMeetingMessage(message))
            {
                message.Statut = "Répondu";
                message.DateTraitement = DateTime.Now;
                message.TraiteParId = actorUserId;
            }

            _context.Update(message);
            await _context.SaveChangesAsync();
            await _auditLogService.LogAsync(actorUserId, "Reply", "Message", message.Id, $"Réponse envoyée: {message.Sujet}");

            return ServiceResult.Ok("Réponse envoyée avec succès.");
        }

        private static bool CanBeHandledByAdmin(Message message, string actorUserId)
        {
            if (!IsVisitOrMeetingMessage(message))
            {
                return true;
            }

            var status = ParseMetadata(message.Contenu, AssignmentStatusKey);
            if (string.Equals(status, PendingSuperAdminStatus, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var assignedUserId = ParseMetadata(message.Contenu, AssignedToUserIdKey);
            if (string.IsNullOrWhiteSpace(assignedUserId))
            {
                return true;
            }

            return string.Equals(assignedUserId, actorUserId, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsVisitOrMeetingMessage(Message message)
        {
            if (!string.IsNullOrWhiteSpace(message.Sujet))
            {
                if (message.Sujet.StartsWith(VisitSubjectPrefix, StringComparison.OrdinalIgnoreCase)
                    || message.Sujet.StartsWith(MeetingSubjectPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            var type = ParseMetadata(message.Contenu, "TYPE");
            return string.Equals(type, "VISITE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "RDV_AGENT", StringComparison.OrdinalIgnoreCase);
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

        private static string UpsertMetadata(string? content, string key, string value)
        {
            var normalized = content ?? string.Empty;
            var line = $"{key}={NormalizeMetadataValue(value)}";
            var pattern = $"(?im)^{Regex.Escape(key)}=.*$";
            if (Regex.IsMatch(normalized, pattern))
            {
                return Regex.Replace(normalized, pattern, line);
            }

            if (string.IsNullOrWhiteSpace(normalized))
            {
                return line;
            }

            return normalized.EndsWith('\n') ? $"{normalized}{line}" : $"{normalized}\n{line}";
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

        private static string NormalizeMessageStatus(string? status, bool isVisitOrMeeting)
        {
            if (string.Equals(status, "Nouveau", StringComparison.OrdinalIgnoreCase))
            {
                return "Nouveau";
            }

            if (string.Equals(status, "Refusé", StringComparison.OrdinalIgnoreCase))
            {
                return "Refusé";
            }

            if (string.Equals(status, "Accepté", StringComparison.OrdinalIgnoreCase))
            {
                return "Accepté";
            }

            if (string.Equals(status, "Répondu", StringComparison.OrdinalIgnoreCase))
            {
                return "Répondu";
            }

            return isVisitOrMeeting ? "Accepté" : "Répondu";
        }
    }
}
