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
            // Simple example scoring:
            // - Punctuality: percent of visits where agent attended on time (mocked via messages)
            // - Feedback: average rating parsed from messages content if present
            // - Conversion: number of sales assigned to agent over number of visits

            // Compute only for users in the 'Admin' role
            var userRefs = await _context.Set<UserReference>().ToListAsync();
            foreach (var user in userRefs)
            {
                // only compute for admins/agents — simplified: skip empty ids
                if (string.IsNullOrWhiteSpace(user.Id)) continue;

                // ensure this user is in Admin role and NOT a SuperAdmin
                var appUser = await _userManager.FindByIdAsync(user.Id);
                if (appUser == null) continue;
                if (!await _userManager.IsInRoleAsync(appUser, "Admin")) continue;
                if (await _userManager.IsInRoleAsync(appUser, "SuperAdmin")) continue;

                // compute counts from Messages and Sales
                var totalVisits = await _context.Messages.CountAsync(m => m.Destinataire == user.Id && (m.Sujet ?? "").Contains("VISITE"));
                var attended = await _context.Messages.CountAsync(m => m.Destinataire == user.Id && (m.Sujet ?? "").Contains("VISITE") && (m.Statut ?? "") == "Traité");
                var punctuality = totalVisits == 0 ? 100.0 : (attended * 100.0) / totalVisits;

                // feedback score: parse lines like RATING=4.5 from message content
                var feedbackMessages = await _context.Messages.Where(m => m.Contenu != null && m.Contenu.Contains("RATING=")).ToListAsync();
                double feedbackSum = 0; int feedbackCount = 0;
                foreach (var msg in feedbackMessages)
                {
                    var lines = msg.Contenu!.Split(new[] {'\n','\r'}, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        if (line.StartsWith("RATING=", StringComparison.OrdinalIgnoreCase))
                        {
                            if (double.TryParse(line[("RATING=").Length..].Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var r))
                            {
                                feedbackSum += r; feedbackCount++;
                            }
                        }
                    }
                }
                var feedbackScore = feedbackCount == 0 ? 100.0 : (feedbackSum / feedbackCount) * 20.0; // scale 0..5 -> 0..100

                // conversion: number of sales where this user was assigned as SchedulingAgent (stored in messages metadata)
                var salesCount = await _context.Sales.CountAsync(s => s.SellerId == user.Id || s.BuyerId == user.Id);
                var conversionScore = 50.0; // placeholder

                var perf = await _context.AgentPerformances.FindAsync(user.Id);
                if (perf == null)
                {
                    perf = new AgentPerformance
                    {
                        AgentId = user.Id,
                        PunctualityScore = punctuality,
                        FeedbackScore = feedbackScore,
                        ConversionScore = conversionScore,
                        LastComputed = DateTime.Now
                    };
                    _context.AgentPerformances.Add(perf);
                }
                else
                {
                    perf.PunctualityScore = punctuality;
                    perf.FeedbackScore = feedbackScore;
                    perf.ConversionScore = conversionScore;
                    perf.LastComputed = DateTime.Now;
                    _context.AgentPerformances.Update(perf);
                }
            }
            // remove any AgentPerformance entries that no longer correspond to an Admin
            // and explicitly exclude SuperAdmin users even if they also have the Admin role
            var adminUsers = await _userManager.GetUsersInRoleAsync("Admin");
            var superAdmins = await _userManager.GetUsersInRoleAsync("SuperAdmin");
            var superAdminIds = new HashSet<string>(superAdmins.Select(u => u.Id), StringComparer.OrdinalIgnoreCase);
            var adminIds = new HashSet<string>(adminUsers.Where(u => !superAdminIds.Contains(u.Id)).Select(u => u.Id), StringComparer.OrdinalIgnoreCase);
            var existingPerfs = await _context.AgentPerformances.ToListAsync();
            foreach (var perf in existingPerfs)
            {
                if (!adminIds.Contains(perf.AgentId))
                {
                    _context.AgentPerformances.Remove(perf);
                }
            }

            await _context.SaveChangesAsync();
        }

        public async Task<IReadOnlyList<AgentPerformance>> GetAllAsync()
        {
            return await _context.AgentPerformances.AsNoTracking().ToListAsync();
        }

        public async Task<IReadOnlyList<AgentPerformanceViewModel>> GetAllViewModelsAsync()
        {
            var perfs = await _context.AgentPerformances.AsNoTracking().ToListAsync();
            var result = new List<AgentPerformanceViewModel>();
            foreach (var p in perfs)
            {
                var user = await _context.Set<UserReference>().FindAsync(p.AgentId);
                var name = user?.Nom ?? user?.UserName ?? user?.Email ?? p.AgentId;
                result.Add(new AgentPerformanceViewModel
                {
                    AgentId = p.AgentId,
                    AgentName = name,
                    PunctualityScore = p.PunctualityScore,
                    FeedbackScore = p.FeedbackScore,
                    ConversionScore = p.ConversionScore,
                    LastComputed = p.LastComputed
                });
            }

            return result;
        }
    }
}
