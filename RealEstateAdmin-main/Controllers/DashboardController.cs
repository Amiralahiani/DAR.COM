using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateAdmin.Data;
using RealEstateAdmin.Models;
using RealEstateAdmin.Services;

namespace RealEstateAdmin.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly IDashboardService _dashboardService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _db;

        private static string ExtractMeta(string? contenu, string key)
        {
            if (string.IsNullOrWhiteSpace(contenu)) return "";
            var line = contenu.Split('\n').Select(l => l.Trim())
                .FirstOrDefault(l => l.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase));
            return line?[(key.Length + 1)..].Trim() ?? "";
        }

        public DashboardController(IDashboardService dashboardService, UserManager<ApplicationUser> userManager, ApplicationDbContext db)
        {
            _dashboardService = dashboardService;
            _userManager = userManager;
            _db = db;
        }

        // GET: Dashboard
        public async Task<IActionResult> Index()
        {
            var currentUser = await _userManager.GetUserAsync(User);
            var isAdmin = User.IsInRole("Admin") || User.IsInRole("SuperAdmin");

            var data = await _dashboardService.BuildAsync(currentUser?.Id, isAdmin);

            ViewBag.TotalBiens = data.TotalBiens;
            ViewBag.TotalUtilisateurs = data.TotalUtilisateurs;
            ViewBag.TotalMessages = data.TotalMessages;
            ViewBag.BiensParPrix = data.BiensParPrix;
            ViewBag.IsAdmin = data.IsAdmin;
            ViewBag.ZoneStats = data.ZoneStats;
            ViewBag.TopZone = data.TopZone;
            ViewBag.AvgPrice = data.AvgPrice;
            ViewBag.AvgPricePerM2 = data.AvgPricePerM2;
            ViewBag.ZoneCount = data.ZoneCount;

            ViewBag.TotalTransactions = data.TotalTransactions;
            ViewBag.PaidTransactions = data.PaidTransactions;
            ViewBag.TotalRevenue = data.TotalRevenue;
            ViewBag.ConversionRate = data.ConversionRate;
            ViewBag.RevenueByMethod = data.RevenueByMethod;

            ViewBag.MySales = data.MySales;
            ViewBag.MyPurchases = data.MyPurchases;
            ViewBag.MyTransactionAmount = data.MyTransactionAmount;

            // Données spécifiques au client
            if (!isAdmin && currentUser != null)
            {
                var uid = currentUser.Id;

                ViewBag.AnnoncesEnAttente = await _db.Annonces.CountAsync(a => a.UserId == uid && a.Statut == "En attente");
                ViewBag.AnnoncesApprouvees = await _db.Annonces.CountAsync(a => a.UserId == uid && a.Statut == "Approuvée");
                ViewBag.AnnoncesRefusees = await _db.Annonces.CountAsync(a => a.UserId == uid && a.Statut == "Refusée");

                ViewBag.DemandesEnAttente = await _db.Messages.CountAsync(m => m.UserId == uid && m.Statut == "Nouveau");
                ViewBag.DemandesAcceptees = await _db.Messages.CountAsync(m => m.UserId == uid && m.Statut == "Accepté");
                ViewBag.DemandesRefusees  = await _db.Messages.CountAsync(m => m.UserId == uid && m.Statut == "Refusé");

                ViewBag.BiensDisponibles = await _db.Biens
                    .CountAsync(b => b.UserId == uid && b.StatutCommercial == "Disponible");

                ViewBag.ClientAnnonces = await _db.Annonces
                    .Where(a => a.UserId == uid)
                    .OrderByDescending(a => a.CreatedAt)
                    .Take(5)
                    .Select(a => new {
                        a.NatureBien, a.Delegation, a.Gouvernorat,
                        a.PrixTnd, a.SurfaceM2, a.NbChambres, a.Statut, a.CreatedAt
                    })
                    .ToListAsync();

                ViewBag.ClientDemandes = await _db.Messages
                    .Where(m => m.UserId == uid &&
                                m.Contenu != null &&
                               (m.Contenu.Contains("TYPE=VISITE") || m.Contenu.Contains("TYPE=RDV_AGENT")))
                    .OrderByDescending(m => m.DateCreation)
                    .Take(5)
                    .ToListAsync() is var msgs
                    ? msgs.Select(m => new {
                        BienTitre = ExtractMeta(m.Contenu, "BIEN_TITRE"),
                        Type      = ExtractMeta(m.Contenu, "TYPE"),
                        SlotStr   = DateTime.TryParse(ExtractMeta(m.Contenu, "SLOT_LOCAL"), out var sl)
                                    ? sl.ToString("dd/MM HH:mm") : "—",
                        m.Statut
                      }).ToList()
                    : null;
            }

            return View();
        }
    }
}
