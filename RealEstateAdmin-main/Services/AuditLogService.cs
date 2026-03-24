using RealEstateAdmin.Data;
using RealEstateAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace RealEstateAdmin.Services
{
    public class AuditLogService : IAuditLogService
    {
        private readonly ApplicationDbContext _context;

        public AuditLogService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task LogAsync(string? userId, string action, string entityType, int? entityId, string details)
        {
            _context.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                Details = details,
                CreatedAt = DateTime.Now
            });
            await _context.SaveChangesAsync();
        }

        public async Task<IReadOnlyList<AuditLog>> GetRecentAsync(int take = 200)
        {
            return await _context.AuditLogs
                .OrderByDescending(l => l.CreatedAt)
                .Take(take)
                .ToListAsync();
        }
    }
}
