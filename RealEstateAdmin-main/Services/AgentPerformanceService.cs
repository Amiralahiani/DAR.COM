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
            var agents = await GetTrackedAgentsAsync();

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

                var allAgentSales = await _context.Sales.CountAsync(s => s.AgentId == agent.Id);

                // 2. Taux de Conversion (Vendus / Assignés)
                // On considère "Assignés" comme les biens validés par cet agent
                var assignesCount = await _context.Biens.CountAsync(b => b.PublicationValidatedByAdminId == agent.Id);
                if (assignesCount == 0)
                {
                    // Fallback: si l'agent ne valide pas de publications (cas agents non-admin),
                    // on mesure la conversion sur les transactions qui lui sont réellement affectées.
                    assignesCount = allAgentSales;
                }

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

                // 4. Nombre de Visites
                int totalVisites = sales.Sum(s => s.NbVisites);

                // 5. Taux de Paiement Complet
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
                // Paiement (20%)
                double p5 = (s.TauxPaiementComplet / 100.0) * 20.0;

                s.ScoreGlobal = Math.Min(100.0, p1 + p2 + p3 + p4 + p5);
            }

            await _context.SaveChangesAsync();
        }

        public async Task<IReadOnlyList<AgentPerformance>> GetAllAsync()
        {
            return await _context.AgentPerformances.AsNoTracking().ToListAsync();
        }

        public async Task<IReadOnlyList<AgentPerformanceViewModel>> GetAllViewModelsAsync()
        {
            var agents = await GetTrackedAgentsAsync();

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
                    TotalVisites = p?.TotalVisites ?? 0,
                    TauxPaiementComplet = p?.TauxPaiementComplet ?? 0,
                    ScoreGlobal = p?.ScoreGlobal ?? 0,
                    AncienneteMois = ancienneteMois,
                    LastComputed = p?.LastComputed ?? DateTime.MinValue
                });
            }

            return result;
        }

        private async Task<List<ApplicationUser>> GetTrackedAgentsAsync()
        {
            var superAdmins = await _userManager.GetUsersInRoleAsync("SuperAdmin");
            var superAdminIds = new HashSet<string>(superAdmins.Select(u => u.Id), StringComparer.OrdinalIgnoreCase);

            var tracked = new Dictionary<string, ApplicationUser>(StringComparer.OrdinalIgnoreCase);

            // 1) Inclure les Admins (hors SuperAdmin)
            var adminUsers = await _userManager.GetUsersInRoleAsync("Admin");
            foreach (var admin in adminUsers)
            {
                if (!superAdminIds.Contains(admin.Id))
                {
                    tracked[admin.Id] = admin;
                }
            }

            // 2) Inclure aussi les utilisateurs réellement affectés comme agent dans les transactions
            //    (même s'ils n'ont pas le rôle Admin).
            var agentIdsInSales = await _context.Sales
                .AsNoTracking()
                .Where(s => !string.IsNullOrWhiteSpace(s.AgentId))
                .Select(s => s.AgentId!)
                .Distinct()
                .ToListAsync();

            if (agentIdsInSales.Count > 0)
            {
                var salesAgents = await _userManager.Users
                    .Where(u => agentIdsInSales.Contains(u.Id))
                    .ToListAsync();

                foreach (var agent in salesAgents)
                {
                    if (!superAdminIds.Contains(agent.Id))
                    {
                        tracked[agent.Id] = agent;
                    }
                }
            }

            return tracked.Values
                .OrderBy(u => u.Nom ?? u.UserName ?? u.Email ?? u.Id)
                .ToList();
        }
    }
}
