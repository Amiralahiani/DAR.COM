using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using RealEstateAdmin.Data;
using RealEstateAdmin.Models;

namespace RealEstateAdmin.Services
{
    public sealed class MLService : IMLService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ApplicationDbContext _context;
        private readonly ILogger<MLService> _logger;
        private readonly string _baseUrl;

        public MLService(
            IHttpClientFactory httpClientFactory,
            ApplicationDbContext context,
            IConfiguration configuration,
            ILogger<MLService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _context = context;
            _logger = logger;
            _baseUrl = configuration["MLEngine:BaseUrl"] ?? "http://localhost:8000";
        }

        public async Task<ClientProfile?> GetClientProfileAsync(string userId, UserBehaviorData behavior)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("MLEngine");

                var payload = new
                {
                    user_id               = ToNumericId(userId),
                    avg_price_viewed      = behavior.AvgPriceViewed,
                    property_views_count  = behavior.PropertyViewsCount,
                    favorites_count       = behavior.FavoritesCount,
                    contact_agent_clicks  = behavior.ContactAgentClicks,
                    avg_surface_viewed    = behavior.AvgSurfaceViewed,
                    session_duration_mins = behavior.SessionDurationMins,
                };

                var response = await client.PostAsJsonAsync($"{_baseUrl}/api/ml/update_profile", payload);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("ML Engine a retourné {Status} pour l'utilisateur {UserId}",
                        response.StatusCode, userId);
                    return null;
                }

                var result = await response.Content.ReadFromJsonAsync<MlProfileResponse>();
                if (result is null) return null;

                return new ClientProfile
                {
                    UserId           = userId,
                    PredictedBudget  = (decimal)result.predicted_budget,
                    LeadScorePct     = result.lead_score_pct,
                    PredictedIntent  = result.predicted_intent,
                    PersonaSegment   = result.persona_segment,
                    ClusterId        = result.cluster_id,
                    LastUpdated      = DateTime.TryParse(result.last_updated, out var dt) ? dt : DateTime.UtcNow,
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'appel ML Engine (GetClientProfile) pour {UserId}", userId);
                return null;
            }
        }

        public async Task CollectAsync(string userId, UserBehaviorData behavior, int targetLead, double targetBudget)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("MLEngine");

                var payload = new
                {
                    user_id               = ToNumericId(userId),
                    avg_price_viewed      = behavior.AvgPriceViewed,
                    property_views_count  = behavior.PropertyViewsCount,
                    favorites_count       = behavior.FavoritesCount,
                    contact_agent_clicks  = behavior.ContactAgentClicks,
                    avg_surface_viewed    = behavior.AvgSurfaceViewed,
                    session_duration_mins = behavior.SessionDurationMins,
                    target_lead           = targetLead,
                    target_budget         = targetBudget,
                };

                var response = await client.PostAsJsonAsync($"{_baseUrl}/api/ml/collect", payload);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("ML Engine collect a échoué pour {UserId} — statut {Status}",
                        userId, response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                // Non-bloquant : une erreur de collecte ne doit pas faire échouer la transaction métier
                _logger.LogError(ex, "Erreur lors de l'envoi des données d'entraînement pour {UserId}", userId);
            }
        }

        public async Task<bool> IsAvailableAsync()
        {
            try
            {
                var client = _httpClientFactory.CreateClient("MLEngine");
                var response = await client.GetAsync($"{_baseUrl}/api/ml/health");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<UserBehaviorData> EstimateBehaviorAsync(string userId)
        {
            var cutoff = DateTime.UtcNow.AddDays(-90);

            // Vraies vues de biens (trackées depuis Shop/Details)
            var views = await _context.BienViews
                .Where(v => v.UserId == userId && v.ViewedAt >= cutoff)
                .OrderBy(v => v.ViewedAt)
                .AsNoTracking()
                .ToListAsync();

            // Vrais contacts agent (ExpressInterest, ReserveVisit, RequestMeeting)
            var contactCount = await _context.AuditLogs
                .Where(a => a.UserId == userId && a.Action == "ContactEvent" && a.CreatedAt >= cutoff)
                .CountAsync();

            // Transactions existantes pour le budget réel (ventes finalisées)
            var transactions = await _context.Sales
                .Include(s => s.BienImmobilier)
                .Where(s => s.BuyerId == userId)
                .AsNoTracking()
                .ToListAsync();

            // ── AvgPriceViewed : vues en priorité, sinon transactions ──
            double avgPrice = views.Any()
                ? (double)views.Average(v => v.Prix)
                : transactions.Any()
                    ? (double)transactions.Average(t => t.BienImmobilier?.Prix ?? 300_000m)
                    : 300_000.0;

            // ── AvgSurfaceViewed : vues en priorité, sinon transactions ──
            double avgSurface = views.Any()
                ? views.Average(v => (double)v.Surface)
                : transactions.Any()
                    ? (double)transactions.Average(t => t.BienImmobilier?.Surface ?? 100)
                    : 100.0;

            // ── PropertyViewsCount : vues réelles + visites des transactions ──
            int viewsCount = views.Count + transactions.Sum(t => Math.Max(t.NbVisites, 0));

            // ── FavoritesCount : biens vus au moins 2 fois (signal d'intérêt) ──
            int favoritesCount = views
                .GroupBy(v => v.BienId)
                .Count(g => g.Count() >= 2);

            // ── ContactAgentClicks : vrais clics depuis AuditLog ──
            int contactClicks = contactCount + transactions.Count;

            // ── SessionDurationMins : durée entre 1ère et dernière vue du jour, en moyenne ──
            double sessionMins = 10.0;
            if (views.Count >= 2)
            {
                var byDay = views
                    .GroupBy(v => v.ViewedAt.Date)
                    .Where(g => g.Count() >= 2)
                    .Select(g => (g.Max(v => v.ViewedAt) - g.Min(v => v.ViewedAt)).TotalMinutes)
                    .ToList();

                if (byDay.Any())
                    sessionMins = Math.Min(byDay.Average(), 120.0);
            }

            return new UserBehaviorData(
                AvgPriceViewed:      avgPrice,
                PropertyViewsCount:  Math.Max(viewsCount, 1),
                FavoritesCount:      favoritesCount,
                ContactAgentClicks:  Math.Min(contactClicks, 10),
                AvgSurfaceViewed:    avgSurface,
                SessionDurationMins: sessionMins
            );
        }

        public async Task<List<BienImmobilier>> GetRecommendedBiensAsync(string userId)
        {
            try
            {
                var behavior = await EstimateBehaviorAsync(userId);
                var profile  = await GetClientProfileAsync(userId, behavior);

                var query = _context.Biens
                    .Include(b => b.User)
                    .Include(b => b.Images)
                    .Where(b => b.IsPublished && b.PublicationStatus == "Publié" && b.StatutCommercial == "Disponible")
                    .AsQueryable();

                if (profile is not null)
                {
                    var budgetCeiling = (decimal)(profile.PredictedBudget * 1.25m);
                    query = query.Where(b => b.Prix <= budgetCeiling);

                    if (profile.PredictedIntent.Contains("LOCATION"))
                        query = query.Where(b => b.TypeTransaction == "A Louer");
                    else
                        query = query.Where(b => b.TypeTransaction == "A Vendre");

                    if (behavior.AvgSurfaceViewed > 0)
                    {
                        var surfaceMin = (int)(behavior.AvgSurfaceViewed * 0.5);
                        query = query.Where(b => b.Surface == null || b.Surface >= surfaceMin);
                    }
                }

                return await query
                    .OrderBy(b => Math.Abs((double)(b.Prix - (profile != null ? profile.PredictedBudget : 300_000m))))
                    .Take(4)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur GetRecommendedBiensAsync pour {UserId}", userId);
                return new List<BienImmobilier>();
            }
        }

        // Convertit un userId string (GUID Identity) en int pour le ML Engine
        private static int ToNumericId(string userId) =>
            Math.Abs(userId.GetHashCode() % 1_000_000);

        // DTO interne correspondant à la réponse Python
        private sealed record MlProfileResponse(
            int user_id,
            double predicted_budget,
            double lead_score_pct,
            string predicted_intent,
            string persona_segment,
            int cluster_id,
            string last_updated
        );
    }
}
