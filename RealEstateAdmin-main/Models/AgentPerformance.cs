using System.ComponentModel.DataAnnotations;

namespace RealEstateAdmin.Models
{
    public class AgentPerformance
    {
        [Key]
        [StringLength(450)]
        public string AgentId { get; set; } = null!;

        public int BiensVendus { get; set; }
        public decimal ValeurTotaleVendue { get; set; }
        public double TauxConversion { get; set; }
        public double DelaiMoyenVente { get; set; }
        public double SatisfactionClient { get; set; }
        public int TotalVisites { get; set; }
        public double TauxPaiementComplet { get; set; }
        
        public double ScoreGlobal { get; set; }

        public DateTime LastComputed { get; set; } = DateTime.Now;
    }
}
