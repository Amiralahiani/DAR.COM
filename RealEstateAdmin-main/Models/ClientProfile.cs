namespace RealEstateAdmin.Models
{
    public sealed class ClientProfile
    {
        public string UserId { get; init; } = string.Empty;
        public decimal PredictedBudget { get; init; }
        public double LeadScorePct { get; init; }
        public string PredictedIntent { get; init; } = string.Empty;
        public string PersonaSegment { get; init; } = string.Empty;
        public int ClusterId { get; init; }
        public DateTime LastUpdated { get; init; }
        public string ModelTrainedOn { get; init; } = string.Empty;
        public string UserDataSource { get; init; } = string.Empty;

        public string LeadScoreLabel => LeadScorePct >= 70 ? "Chaud"
            : LeadScorePct >= 40 ? "Tiède"
            : "Froid";

        public string LeadScoreBadgeClass => LeadScorePct >= 70 ? "badge bg-danger"
            : LeadScorePct >= 40 ? "badge bg-warning text-dark"
            : "badge bg-secondary";
    }
}