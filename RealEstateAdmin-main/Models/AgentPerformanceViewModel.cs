using System;

namespace RealEstateAdmin.Models
{
    public class AgentPerformanceViewModel
    {
        public string AgentId { get; set; } = null!;
        public string AgentName { get; set; } = string.Empty;

        public double PunctualityScore { get; set; }
        public double FeedbackScore { get; set; }
        public double ConversionScore { get; set; }

        public DateTime LastComputed { get; set; }
    }
}
