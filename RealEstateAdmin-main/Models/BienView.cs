using System.ComponentModel.DataAnnotations;

namespace RealEstateAdmin.Models
{
    public class BienView
    {
        public int Id { get; set; }

        [Required]
        [StringLength(450)]
        public string UserId { get; set; } = string.Empty;

        [Required]
        public int BienId { get; set; }

        public decimal Prix { get; set; }

        public int Surface { get; set; }

        public DateTime ViewedAt { get; set; } = DateTime.UtcNow;
    }
}
