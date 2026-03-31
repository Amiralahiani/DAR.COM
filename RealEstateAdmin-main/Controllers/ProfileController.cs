using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using RealEstateAdmin.Models;
using RealEstateAdmin.Services;
using System.Linq;

namespace RealEstateAdmin.Controllers
{
    [Authorize]
    public class ProfileController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IAgentPerformanceService _performanceService;

        public ProfileController(UserManager<ApplicationUser> userManager, 
                               SignInManager<ApplicationUser> signInManager,
                               IAgentPerformanceService performanceService)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _performanceService = performanceService;
        }

        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound();
            }

            var roles = await _userManager.GetRolesAsync(user);
            var isAdmin = roles.Contains("Admin") && !roles.Contains("SuperAdmin");

            var model = new ProfileViewModel
            {
                Nom = user.Nom ?? string.Empty,
                Email = user.Email ?? string.Empty,
                Role = roles.FirstOrDefault() ?? "Utilisateur",
                DateInscription = user.DateInscription
            };

            if (isAdmin)
            {
                // GetAllViewModelsAsync now includes ALL admins, even those without performance records
                var allPerfs = await _performanceService.GetAllViewModelsAsync();
                var sortedPerfs = allPerfs.OrderByDescending(p => p.ScoreGlobal).ToList();
                var myPerf = sortedPerfs.FirstOrDefault(p => string.Equals(p.AgentId, user.Id, StringComparison.OrdinalIgnoreCase));

                if (myPerf != null)
                {
                    model.Performance = myPerf;
                    model.Rank = sortedPerfs.IndexOf(myPerf) + 1;
                    model.TotalAgents = sortedPerfs.Count;
                }
                else
                {
                    // Fallback: show an empty performance card with zeroes
                    model.Performance = new AgentPerformanceViewModel
                    {
                        AgentId = user.Id,
                        AgentName = user.Nom ?? user.Email ?? "",
                        ScoreGlobal = 0,
                        LastComputed = DateTime.MinValue
                    };
                    model.Rank = 0;
                    model.TotalAgents = sortedPerfs.Count;
                }
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Update(ProfileViewModel model)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound();
            }

            if (!ModelState.IsValid)
            {
                return View("Index", model);
            }

            user.Nom = model.Nom;
            // Email change is not supported in this simple version to avoid complex validation/confirmation flows
            
            var result = await _userManager.UpdateAsync(user);
            if (result.Succeeded)
            {
                if (!string.IsNullOrEmpty(model.NewPassword))
                {
                    if (string.IsNullOrEmpty(model.OldPassword))
                    {
                        ModelState.AddModelError(string.Empty, "L'ancien mot de passe est obligatoire pour changer de mot de passe.");
                        return View("Index", model);
                    }

                    var passResult = await _userManager.ChangePasswordAsync(user, model.OldPassword, model.NewPassword);
                    if (!passResult.Succeeded)
                    {
                        foreach (var error in passResult.Errors)
                        {
                            ModelState.AddModelError(string.Empty, error.Description);
                        }
                        return View("Index", model);
                    }
                }

                TempData["SuccessMessage"] = "Votre profil a été mis à jour avec succès.";
                return RedirectToAction(nameof(Index));
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return View("Index", model);
        }
    }
}
