using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateAdmin.Data;
using RealEstateAdmin.Models;

namespace RealEstateAdmin.Controllers
{
    [Authorize]
    public class MesDemandesController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public MesDemandesController(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login", "Account");

            var demandes = await _db.Messages
                .Where(m => m.UserId == user.Id &&
                           (m.Contenu != null) &&
                           (m.Contenu.Contains("TYPE=VISITE") ||
                            m.Contenu.Contains("TYPE=RDV_AGENT") ||
                            m.Contenu.Contains("TYPE=INTERET")))
                .OrderByDescending(m => m.DateCreation)
                .ToListAsync();

            return View(demandes);
        }
    }
}
