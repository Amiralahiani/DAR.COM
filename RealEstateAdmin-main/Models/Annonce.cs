using System.ComponentModel.DataAnnotations;

namespace RealEstateAdmin.Models
{
    public class Annonce
    {
        public int Id { get; set; }

        [StringLength(450)]
        public string? UserId { get; set; }

        // Step 1 — Description du bien
        [Required(ErrorMessage = "Le gouvernorat est obligatoire")]
        [StringLength(100)]
        public string Gouvernorat { get; set; } = string.Empty;

        [Required(ErrorMessage = "La délégation est obligatoire")]
        [StringLength(100)]
        public string Delegation { get; set; } = string.Empty;

        [Range(1, int.MaxValue, ErrorMessage = "La surface doit être d'au moins 1 m²")]
        public int SurfaceM2 { get; set; }

        [Range(0, int.MaxValue, ErrorMessage = "Le nombre de chambres doit être positif")]
        public int NbChambres { get; set; }

        public bool HasAscenseur { get; set; }
        public bool HasBalcon { get; set; }
        public bool HasChauffageCentral { get; set; }
        public bool HasClimatisation { get; set; }
        public bool HasGarage { get; set; }
        public bool HasJardin { get; set; }
        public bool HasParking { get; set; }
        public bool HasPiscine { get; set; }
        public bool HasTerrasse { get; set; }

        // Step 2 — Photos + description
        [StringLength(5000)]
        public string? Description { get; set; }

        // Step 3 — Prix
        [Range(1, int.MaxValue, ErrorMessage = "Le prix doit être d'au moins 1 TND")]
        public int PrixTnd { get; set; }

        [StringLength(200)]
        public string? Titre { get; set; }

        [StringLength(50)]
        public string? NatureBien { get; set; }

        [StringLength(50)]
        public string? EtatBien { get; set; }

        // Meta
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [StringLength(50)]
        public string Statut { get; set; } = "En attente";

        // Lien vers le BienImmobilier créé lors de l'approbation
        public int? BienImmobilierId { get; set; }

        public ICollection<AnnoncePhoto> Photos { get; set; } = new List<AnnoncePhoto>();
    }
}
