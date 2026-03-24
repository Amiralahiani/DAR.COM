using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using RealEstateAdmin.Models;
using RealEstateAdmin.Services;

namespace RealEstateAdmin.Controllers
{
    public class MessageController : Controller
    {
        private readonly IMessageService _messageService;
        private readonly UserManager<ApplicationUser> _userManager;

        public MessageController(IMessageService messageService, UserManager<ApplicationUser> userManager)
        {
            _messageService = messageService;
            _userManager = userManager;
        }

        // GET: Message (Admin et SuperAdmin)
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> Index()
        {
            var currentUser = await _userManager.GetUserAsync(User);
            var isSuperAdmin = User.IsInRole("SuperAdmin");

            if (isSuperAdmin)
            {
                ViewBag.AssignableAdmins = await _messageService.GetAssignableAdminsAsync();
            }

            var messages = await _messageService.GetMessagesAsync(currentUser?.Id, isSuperAdmin);
            return View(messages);
        }

        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> ExportCsv()
        {
            var csv = await _messageService.ExportCsvAsync();
            return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", "messages.csv");
        }

        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> ExportPdf()
        {
            var bytes = await _messageService.ExportPdfAsync();
            return File(bytes, "application/pdf", "messages.pdf");
        }

        // GET: Message/Create
        [AllowAnonymous]
        public IActionResult Create()
        {
            return View();
        }

        // POST: Message/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [AllowAnonymous]
        public async Task<IActionResult> Create([Bind("NomUtilisateur,Email,Sujet,Contenu,Destinataire")] Message message)
        {
            if (!ModelState.IsValid)
            {
                return View(message);
            }

            var currentUser = await _userManager.GetUserAsync(User);
            var result = await _messageService.CreateAsync(message, currentUser?.Id);
            if (result.Success)
            {
                TempData["SuccessMessage"] = result.Message;
                return RedirectToAction(nameof(Create));
            }

            if (!string.IsNullOrWhiteSpace(result.Message))
            {
                ModelState.AddModelError(string.Empty, result.Message);
            }

            return View(message);
        }

        // GET: Message/Details/5
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var message = await _messageService.GetByIdAsync(id.Value);
            if (message == null)
            {
                return NotFound();
            }

            return View(message);
        }

        // GET: Message/Delete/5
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var message = await _messageService.GetByIdAsync(id.Value);
            if (message == null)
            {
                return NotFound();
            }

            return View(message);
        }

        // POST: Message/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            var result = await _messageService.DeleteAsync(id, currentUser?.Id);
            if (result.Success)
            {
                return RedirectToAction(nameof(Index));
            }

            return HandleResult(result);
        }

        // GET: Message/Reply/5
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> Reply(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var message = await _messageService.GetByIdAsync(id.Value);
            if (message == null)
            {
                return NotFound();
            }

            ViewBag.Message = message;
            return View();
        }

        // POST: Message/Reply
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> Reply(int messageId, string reponse)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            var result = await _messageService.ReplyAsync(messageId, reponse, currentUser?.Id);
            if (result.Success)
            {
                TempData["SuccessMessage"] = result.Message;
                return RedirectToAction(nameof(Index));
            }

            return HandleResult(result);
        }

        // POST: Message/MarkTreated/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> MarkTreated(int id)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            var result = await _messageService.MarkTreatedAsync(id, currentUser?.Id);
            if (result.Success)
            {
                return RedirectToAction(nameof(Index));
            }

            return HandleResult(result);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<IActionResult> AssignToAdmin(int id, string adminUserId)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            var result = await _messageService.AssignToAdminAsync(id, adminUserId, currentUser?.Id, true);
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
                    TempData["ErrorMessage"] = result.Message ?? "Une erreur est survenue.";
                    return RedirectToAction(nameof(Index));
            }
        }
    }
}
