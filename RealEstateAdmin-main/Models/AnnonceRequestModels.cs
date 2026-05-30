using System.ComponentModel.DataAnnotations;

namespace RealEstateAdmin.Models
{
    public class GenerateDescriptionRequest
    {
        public int SurfaceM2 { get; set; }
        public int NbChambres { get; set; }
        public string Gouvernorat { get; set; } = string.Empty;
        public string Delegation { get; set; } = string.Empty;
        public bool HasAscenseur { get; set; }
        public bool HasBalcon { get; set; }
        public bool HasChauffageCentral { get; set; }
        public bool HasClimatisation { get; set; }
        public bool HasGarage { get; set; }
        public bool HasJardin { get; set; }
        public bool HasParking { get; set; }
        public bool HasPiscine { get; set; }
        public bool HasTerrasse { get; set; }
        // URLs relatives des photos déjà uploadées (/uploads/annonces/xxx.jpg)
        public List<string> Photos { get; set; } = new();
    }

    public class PredictPriceRequest
    {
        public int SurfaceM2 { get; set; }
        public int NbChambres { get; set; }
        public string Gouvernorat { get; set; } = string.Empty;
        public string Delegation { get; set; } = string.Empty;
        public bool HasAscenseur { get; set; }
        public bool HasBalcon { get; set; }
        public bool HasChauffageCentral { get; set; }
        public bool HasClimatisation { get; set; }
        public bool HasGarage { get; set; }
        public bool HasJardin { get; set; }
        public bool HasParking { get; set; }
        public bool HasPiscine { get; set; }
        public bool HasTerrasse { get; set; }
    }

    public class PublishAnnonceRequest
    {
        [Range(1, int.MaxValue, ErrorMessage = "Le prix doit être d'au moins 1 TND")]
        public int PrixTnd { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "La surface doit être d'au moins 1 m²")]
        public int SurfaceM2 { get; set; }

        [Range(0, int.MaxValue)]
        public int NbChambres { get; set; }

        [Required]
        public string Gouvernorat { get; set; } = string.Empty;

        [Required]
        public string Delegation { get; set; } = string.Empty;

        public string? Description { get; set; }
        public List<string> Photos { get; set; } = new();

        public bool HasAscenseur { get; set; }
        public bool HasBalcon { get; set; }
        public bool HasChauffageCentral { get; set; }
        public bool HasClimatisation { get; set; }
        public bool HasGarage { get; set; }
        public bool HasJardin { get; set; }
        public bool HasParking { get; set; }
        public bool HasPiscine { get; set; }
        public bool HasTerrasse { get; set; }
    }
}
