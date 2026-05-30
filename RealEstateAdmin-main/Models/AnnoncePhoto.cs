using System.ComponentModel.DataAnnotations;

namespace RealEstateAdmin.Models
{
    public class AnnoncePhoto
    {
        public int Id { get; set; }

        public int AnnonceId { get; set; }

        [Required]
        [StringLength(1000)]
        public string Url { get; set; } = string.Empty;

        public Annonce Annonce { get; set; } = null!;
    }
}
