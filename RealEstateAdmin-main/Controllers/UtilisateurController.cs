using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using RealEstateAdmin.Models;
using RealEstateAdmin.Services;

namespace RealEstateAdmin.Controllers
{
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class UtilisateurController : Controller
    {
        private readonly IUserManagementService _userService;
        private readonly UserManager<ApplicationUser> _userManager;

        public UtilisateurController(IUserManagementService userService, UserManager<ApplicationUser> userManager)
        {
            _userService = userService;
            _userManager = userManager;
        }

        // GET: Utilisateur
        public async Task<IActionResult> Index()
        {
            return View(await _userService.GetUsersAsync());
        }

        // GET: Utilisateur/Details/5
        public async Task<IActionResult> Details(string? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var vm = await _userService.GetUserDetailsAsync(id);
            if (vm == null)
            {
                return NotFound();
            }

            return View(vm);
        }

        [HttpGet]
        [Authorize(Roles = "SuperAdmin")]
        public IActionResult Create()
        {
            ViewBag.RoleOptions = new[] { "Admin" };
            return View(new UserManagementViewModel
            {
                RoleName = "Admin"
            });
        }

        [HttpPost]
        [Authorize(Roles = "SuperAdmin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(UserManagementViewModel model)
        {
            ViewBag.RoleOptions = new[] { "Admin" };
            model.RoleName = "Admin";
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var currentUser = await _userManager.GetUserAsync(User);
            var result = await _userService.CreateAsync(model, currentUser?.Id);
            if (result.Success)
            {
                TempData["SuccessMessage"] = result.Message;
                return RedirectToAction(nameof(Index));
            }

            if (!string.IsNullOrWhiteSpace(result.Message))
            {
                ModelState.AddModelError(string.Empty, result.Message);
            }

            return View(model);
        }

        [HttpGet]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<IActionResult> Edit(string? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            ViewBag.RoleOptions = _userService.AllowedRoles.ToArray();
            var result = await _userService.GetEditModelAsync(id);
            if (result.Success)
            {
                return View(result.Data);
            }

            return HandleResult(result);
        }

        [HttpPost]
        [Authorize(Roles = "SuperAdmin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string id, UserManagementViewModel model)
        {
            ViewBag.RoleOptions = _userService.AllowedRoles.ToArray();
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var currentUser = await _userManager.GetUserAsync(User);
            var result = await _userService.EditAsync(id, model, currentUser?.Id);
            if (result.Success)
            {
                TempData["SuccessMessage"] = result.Message;
                return RedirectToAction(nameof(Index));
            }

            if (result.ErrorCode == ServiceErrorCode.Validation && !string.IsNullOrWhiteSpace(result.Message))
            {
                ModelState.AddModelError(string.Empty, result.Message);
                return View(model);
            }

            return HandleResult(result);
        }

        [HttpGet]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<IActionResult> Delete(string? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var vm = await _userService.GetDeleteModelAsync(id);
            if (vm == null)
            {
                return NotFound();
            }

            return View(vm);
        }

        [HttpPost, ActionName("Delete")]
        [Authorize(Roles = "SuperAdmin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(string id)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            var result = await _userService.DeleteAsync(id, currentUser?.Id);
            if (result.Success)
            {
                TempData["SuccessMessage"] = result.Message;
                return RedirectToAction(nameof(Index));
            }

            if (!string.IsNullOrWhiteSpace(result.Message))
            {
                TempData["ErrorMessage"] = result.Message;
            }

            return HandleResult(result);
        }

        [HttpPost]
        [Authorize(Roles = "SuperAdmin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleSuspension(string id)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            var result = await _userService.ToggleSuspensionAsync(id, currentUser?.Id);
            if (result.Success)
            {
                TempData["SuccessMessage"] = result.Message;
                return RedirectToAction(nameof(Index));
            }

            return HandleResult(result);
        }

        private IActionResult HandleResult(ServiceResult result)
        {
            switch (result.ErrorCode)
            {
                case ServiceErrorCode.NotFound:
                    return NotFound();
                case ServiceErrorCode.BadRequest:
                    return BadRequest();
                case ServiceErrorCode.Forbidden:
                    return Forbid();
                default:
                    if (!string.IsNullOrWhiteSpace(result.Message))
                    {
                        TempData["ErrorMessage"] = result.Message;
                    }

                    return RedirectToAction(nameof(Index));
            }
        }
    }
}
