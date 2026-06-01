using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateAdmin.Data;

namespace RealEstateAdmin.Controllers.Api
{
    [ApiController]
    [Route("api/shop")]
    public class ShopApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public ShopApiController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: api/shop/filter
        [HttpGet("filter")]
        public async Task<IActionResult> Filter(
            decimal? prixMin,
            decimal? prixMax,
            string? adresse,
            int? surfaceMin,
            int? surfaceMax,
            string? natureBien
        )
        {
            var query = _context.Biens.AsQueryable()
                .Where(b => b.IsPublished && b.PublicationStatus == "Publié")
                .Where(b => string.IsNullOrEmpty(b.TypeTransaction) || b.TypeTransaction == "A Vendre");

            if (prixMin.HasValue) query = query.Where(b => b.Prix >= prixMin.Value);
            if (prixMax.HasValue) query = query.Where(b => b.Prix <= prixMax.Value);
            if (!string.IsNullOrWhiteSpace(adresse)) query = query.Where(b => b.Adresse != null && b.Adresse.Contains(adresse));
            if (surfaceMin.HasValue) query = query.Where(b => b.Surface >= surfaceMin.Value);
            if (surfaceMax.HasValue) query = query.Where(b => b.Surface <= surfaceMax.Value);
            if (!string.IsNullOrWhiteSpace(natureBien)) query = query.Where(b => b.NatureBien == natureBien);

            var results = await query
                .OrderByDescending(b => b.Prix)
                .Select(b => new {
                    b.Id,
                    b.Titre,
                    b.Prix,
                    b.Surface,
                    b.ImageUrl
                })
                .ToListAsync();

            return Ok(new { count = results.Count, items = results });
        }
    }
}
