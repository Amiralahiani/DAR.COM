using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using System.Linq;
using RealEstateAdmin.Data;
using RealEstateAdmin.Models;

namespace RealEstateAdmin.Services
{
    public class AgentPerformanceService : IAgentPerformanceService
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public AgentPerformanceService(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task RecomputeAllAsync()
        {
            var adminUsers = await _userManager.GetUsersInRoleAsync("Admin");
            var superAdmins = await _userManager.GetUsersInRoleAsync("SuperAdmin");
            var superAdminIds = new HashSet<string>(superAdmins.Select(u => u.Id), StringComparer.OrdinalIgnoreCase);
            var agents = adminUsers.Where(u => !superAdminIds.Contains(u.Id)).ToList();

            if (!agents.Any()) return;

            var stats = new List<AgentPerformance>();

            foreach (var agent in agents)
            {
                // 1. Biens Vendus & Valeur Totale
                var sales = await _context.Sales
                    .Include(s => s.BienImmobilier)
                    .Where(s => s.AgentId == agent.Id && s.BienImmobilier!.StatutCommercial == "Vendu")
                    .ToListAsync();

                int biensVendus = sales.Count;
                decimal valeurTotale = sales.Sum(s => s.Amount);

                // 2. Taux de Conversion (Vendus / Assignés)
                // On considère "Assignés" comme les biens validés par cet agent
                var assignesCount = await _context.Biens.CountAsync(b => b.PublicationValidatedByAdminId == agent.Id);
                double conversion = assignesCount == 0 ? 0 : (double)biensVendus * 100.0 / assignesCount;

                // 3. Délai Moyen de Vente (jours)
                double delaiMoyen = 0;
                if (biensVendus > 0)
                {
                    double totalDays = 0;
                    foreach (var s in sales)
                    {
                        var span = (s.PaidAt ?? DateTime.Now) - s.CreatedAt;
                        totalDays += span.TotalDays;
                    }
                    delaiMoyen = totalDays / biensVendus;
                }

                // 4. Satisfaction Client
                var ratings = sales.Where(s => s.NoteClient.HasValue).Select(s => (double)s.NoteClient!.Value).ToList();
                double satisfaction = ratings.Any() ? ratings.Average() : 0;

                // 5. Nombre de Visites
                int totalVisites = sales.Sum(s => s.NbVisites);

                // 6. Taux de Paiement Complet
                var allAgentSales = await _context.Sales.CountAsync(s => s.AgentId == agent.Id);
                var completePayments = await _context.Sales.CountAsync(s => s.AgentId == agent.Id && s.StatutPaiementDetaille == "Complet");
                double tauxPaiement = allAgentSales == 0 ? 0 : (double)completePayments * 100.0 / allAgentSales;

                var perf = await _context.AgentPerformances.FindAsync(agent.Id);
                if (perf == null)
                {
                    perf = new AgentPerformance { AgentId = agent.Id };
                    _context.AgentPerformances.Add(perf);
                }

                perf.BiensVendus = biensVendus;
                perf.ValeurTotaleVendue = valeurTotale;
                perf.TauxConversion = conversion;
                perf.DelaiMoyenVente = delaiMoyen;
                perf.SatisfactionClient = satisfaction;
                perf.TotalVisites = totalVisites;
                perf.TauxPaiementComplet = tauxPaiement;
                perf.LastComputed = DateTime.Now;

                stats.Add(perf);
            }

            // SCORE GLOBAL (0-100) pondéré
            // Normalisation simplifiée par rapport aux maximums de la cohorte pour les volumes, et absolu pour les taux/notes
            int maxVendus = stats.Any() ? stats.Max(s => s.BiensVendus) : 1;
            decimal maxValeur = stats.Any() ? stats.Max(s => s.ValeurTotaleVendue) : 1;
            if (maxVendus == 0) maxVendus = 1;
            if (maxValeur == 0) maxValeur = 1;

            foreach (var s in stats)
            {
                // Ventes (25%)
                double p1 = (s.BiensVendus / (double)maxVendus) * 25.0;
                // Valeur (20%)
                double p2 = (double)(s.ValeurTotaleVendue / maxValeur) * 20.0;
                // Conversion (20%)
                double p3 = (s.TauxConversion / 100.0) * 20.0;
                // Délai (15%) : On considère 30 jours comme "parfait", plus c'est long, moins on a de points
                double p4 = s.DelaiMoyenVente <= 30 ? 15.0 : Math.Max(0, 15.0 - ((s.DelaiMoyenVente - 30) / 10.0));
                // Satisfaction (10%)
                double p5 = (s.SatisfactionClient / 5.0) * 10.0;
                // Paiement (10%)
                double p6 = (s.TauxPaiementComplet / 100.0) * 10.0;

                s.ScoreGlobal = Math.Min(100.0, p1 + p2 + p3 + p4 + p5 + p6);
            }

            await _context.SaveChangesAsync();
        }

        public async Task<IReadOnlyList<AgentPerformance>> GetAllAsync()
        {
            return await _context.AgentPerformances.AsNoTracking().ToListAsync();
        }

        public async Task<IReadOnlyList<AgentPerformanceViewModel>> GetAllViewModelsAsync()
        {
            // Get all Admin users excluding SuperAdmins
            var adminUsers = await _userManager.GetUsersInRoleAsync("Admin");
            var superAdmins = await _userManager.GetUsersInRoleAsync("SuperAdmin");
            var superAdminIds = new HashSet<string>(superAdmins.Select(u => u.Id), StringComparer.OrdinalIgnoreCase);
            var agents = adminUsers.Where(u => !superAdminIds.Contains(u.Id)).ToList();

            // Load all existing performance records
            var perfMap = (await _context.AgentPerformances.AsNoTracking().ToListAsync())
                          .ToDictionary(p => p.AgentId, StringComparer.OrdinalIgnoreCase);

            var result = new List<AgentPerformanceViewModel>();
            foreach (var agent in agents)
            {
                perfMap.TryGetValue(agent.Id, out var p);

                var ancienneteMois = 0;
                var span = DateTime.Now - agent.DateInscription;
                ancienneteMois = (int)(span.TotalDays / 30.44);

                result.Add(new AgentPerformanceViewModel
                {
                    AgentId = agent.Id,
                    AgentName = agent.Nom ?? agent.UserName ?? agent.Email ?? agent.Id,
                    BiensVendus = p?.BiensVendus ?? 0,
                    ValeurTotaleVendue = p?.ValeurTotaleVendue ?? 0,
                    TauxConversion = p?.TauxConversion ?? 0,
                    DelaiMoyenVente = p?.DelaiMoyenVente ?? 0,
                    SatisfactionClient = p?.SatisfactionClient ?? 0,
                    TotalVisites = p?.TotalVisites ?? 0,
                    TauxPaiementComplet = p?.TauxPaiementComplet ?? 0,
                    ScoreGlobal = p?.ScoreGlobal ?? 0,
                    AncienneteMois = ancienneteMois,
                    LastComputed = p?.LastComputed ?? DateTime.MinValue
                });
            }

            return result;
        }
    }
}
