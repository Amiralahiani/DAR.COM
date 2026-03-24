using System.ComponentModel.DataAnnotations;

namespace RealEstateAdmin.Models
{
    public class BienImage
    {
        public int Id { get; set; }

        public int BienImmobilierId { get; set; }

        [Required]
        [StringLength(1000)]
        public string Url { get; set; } = string.Empty;

        public BienImmobilier? BienImmobilier { get; set; }
    }
}
