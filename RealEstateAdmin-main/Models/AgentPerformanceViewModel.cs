using System;

namespace RealEstateAdmin.Models
{
    public class AgentPerformanceViewModel
    {
        public string AgentId { get; set; } = null!;
        public string AgentName { get; set; } = string.Empty;

        public int BiensVendus { get; set; }
        public decimal ValeurTotaleVendue { get; set; }
        public double TauxConversion { get; set; }
        public double DelaiMoyenVente { get; set; }
        public int TotalVisites { get; set; }
        public double TauxPaiementComplet { get; set; }
        public double ScoreGlobal { get; set; }
        public int AncienneteMois { get; set; }

        public DateTime LastComputed { get; set; }
    }
}
