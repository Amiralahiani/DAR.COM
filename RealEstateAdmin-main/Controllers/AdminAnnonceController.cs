using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateAdmin.Data;
using RealEstateAdmin.Models;

namespace RealEstateAdmin.Controllers
{
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class AdminAnnonceController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public AdminAnnonceController(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        // GET: AdminAnnonce
        public async Task<IActionResult> Index(string? statut)
        {
            var query = _db.Annonces.Include(a => a.Photos).AsQueryable();

            if (!string.IsNullOrWhiteSpace(statut))
                query = query.Where(a => a.Statut == statut);

            var annonces = await query.OrderByDescending(a => a.CreatedAt).ToListAsync();

            ViewBag.StatutFiltre = statut;
            ViewBag.Counts = new
            {
                EnAttente  = await _db.Annonces.CountAsync(a => a.Statut == "En attente"),
                Approuvee  = await _db.Annonces.CountAsync(a => a.Statut == "Approuvée"),
                Refusee    = await _db.Annonces.CountAsync(a => a.Statut == "Refusée")
            };

            return View(annonces);
        }

        // POST: AdminAnnonce/Approuver/5
        // Convertit l'Annonce en BienImmobilier et met son statut à "Approuvée"
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Approuver(int id)
        {
            var annonce = await _db.Annonces.Include(a => a.Photos).FirstOrDefaultAsync(a => a.Id == id);
            if (annonce == null)
                return NotFound();

            if (annonce.Statut != "En attente")
            {
                TempData["Error"] = "Cette annonce a déjà été traitée.";
                return RedirectToAction(nameof(Index));
            }

            var admin = await _userManager.GetUserAsync(User);

            var bien = new BienImmobilier
            {
                UserId                       = annonce.UserId,
                Titre                        = BuildTitre(annonce),
                Description                  = annonce.Description,
                Prix                         = annonce.PrixTnd,
                Adresse                      = $"{annonce.Delegation}, {annonce.Gouvernorat}",
                Surface                      = annonce.SurfaceM2,
                NombrePieces                 = annonce.NbChambres,
                TypeTransaction               = "A Vendre",
                StatutCommercial             = "Disponible",
                IsPublished                  = true,
                PublicationStatus            = "Publié",
                PublicationValidatedByAdminId = admin?.Id,
                PublicationValidatedAt       = DateTime.UtcNow,
                NatureBien                   = annonce.NatureBien,
                EtatBien                     = annonce.EtatBien,
                HasAscenseur                 = annonce.HasAscenseur,
                HasBalcon                    = annonce.HasBalcon,
                HasChauffageCentral          = annonce.HasChauffageCentral,
                HasClimatisation             = annonce.HasClimatisation,
                HasGarage                    = annonce.HasGarage,
                HasJardin                    = annonce.HasJardin,
                HasParking                   = annonce.HasParking,
                HasPiscine                   = annonce.HasPiscine,
                HasTerrasse                  = annonce.HasTerrasse,
            };

            // Première photo comme image principale, reste dans la galerie
            var photos = annonce.Photos.Where(p => !string.IsNullOrWhiteSpace(p.Url)).ToList();
            if (photos.Count > 0)
            {
                bien.ImageUrl = photos[0].Url;
                foreach (var photo in photos)
                    bien.Images.Add(new BienImage { Url = photo.Url });
            }

            _db.Biens.Add(bien);
            await _db.SaveChangesAsync();

            annonce.Statut = "Approuvée";
            annonce.BienImmobilierId = bien.Id;

            TempData["Success"] = $"Annonce #{annonce.Id} approuvée — Bien #{bien.Id} créé et publié sur le shop.";
            return RedirectToAction(nameof(Index));
        }

        // POST: AdminAnnonce/Refuser/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Refuser(int id)
        {
            var annonce = await _db.Annonces.FindAsync(id);
            if (annonce == null)
                return NotFound();

            if (annonce.Statut != "En attente")
            {
                TempData["Error"] = "Cette annonce a déjà été traitée.";
                return RedirectToAction(nameof(Index));
            }

            annonce.Statut = "Refusée";
            await _db.SaveChangesAsync();

            TempData["Success"] = $"Annonce #{annonce.Id} refusée.";
            return RedirectToAction(nameof(Index));
        }

        private static string BuildTitre(Annonce a)
        {
            if (!string.IsNullOrWhiteSpace(a.Titre))
                return a.Titre;

            var type = !string.IsNullOrWhiteSpace(a.NatureBien)
                ? a.NatureBien
                : a.SurfaceM2 switch
                {
                    < 60  => "Studio",
                    < 100 => "Appartement",
                    < 250 => "Appartement spacieux",
                    _     => "Villa"
                };
            return $"{type} {a.SurfaceM2} m² — {a.Delegation}, {a.Gouvernorat}";
        }
    }
}
