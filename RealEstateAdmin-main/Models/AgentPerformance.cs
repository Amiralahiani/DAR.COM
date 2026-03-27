using System.ComponentModel.DataAnnotations;

namespace RealEstateAdmin.Models
{
    public class AgentPerformance
    {
        [Key]
        [StringLength(450)]
        public string AgentId { get; set; } = null!;

        // Scores 0..100
        public double PunctualityScore { get; set; }
        public double FeedbackScore { get; set; }
        public double ConversionScore { get; set; }

        public DateTime LastComputed { get; set; } = DateTime.Now;
    }
}
