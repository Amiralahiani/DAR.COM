using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using RealEstateAdmin.Models;
using RealEstateAdmin.Services;

namespace RealEstateAdmin.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly IDashboardService _dashboardService;
        private readonly UserManager<ApplicationUser> _userManager;

        public DashboardController(IDashboardService dashboardService, UserManager<ApplicationUser> userManager)
        {
            _dashboardService = dashboardService;
            _userManager = userManager;
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

            return View();
        }
    }
}
