using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateAdmin.Data;

namespace RealEstateAdmin.ViewComponents
{
    public class NewMessagesBadgeViewComponent : ViewComponent
    {
        private readonly ApplicationDbContext _context;

        public NewMessagesBadgeViewComponent(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var count = await _context.Messages.CountAsync(m => m.Statut == "Nouveau");
            return View(count);
        }
    }
}
