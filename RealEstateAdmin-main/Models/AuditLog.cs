using System.ComponentModel.DataAnnotations;

namespace RealEstateAdmin.Models
{
    public class AuditLog
    {
        public int Id { get; set; }

        [StringLength(450)]
        public string? UserId { get; set; }

        [StringLength(200)]
        public string Action { get; set; } = string.Empty;

        [StringLength(200)]
        public string EntityType { get; set; } = string.Empty;

        public int? EntityId { get; set; }

        [StringLength(2000)]
        public string? Details { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
