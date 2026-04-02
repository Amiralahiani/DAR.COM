using System.Text;
using System.Net;
using System.Net.Mail;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using RealEstateAdmin.Models;
using RealEstateAdmin.Services;

namespace RealEstateAdmin.Controllers
{
    public class AccountController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IEmailSender _emailSender;
        private readonly IUserManagementService _userService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AccountController> _logger;
        private readonly IWebHostEnvironment _environment;

        public AccountController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IEmailSender emailSender,
            IUserManagementService userService,
            IConfiguration configuration,
            ILogger<AccountController> logger,
            IWebHostEnvironment environment)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _emailSender = emailSender;
            _userService = userService;
            _configuration = configuration;
            _logger = logger;
            _environment = environment;
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Login(string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var result = await _signInManager.PasswordSignInAsync(
                model.Email, model.Password, model.RememberMe, lockoutOnFailure: false);

            if (result.Succeeded)
            {
                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                {
                    return Redirect(returnUrl);
                }

                return RedirectToAction("Index", "Dashboard");
            }

            if (result.IsNotAllowed)
            {
                ModelState.AddModelError(string.Empty, "Votre email n'est pas encore confirmé. Vérifiez votre boîte mail.");
                return View(model);
            }

            if (result.IsLockedOut)
            {
                ModelState.AddModelError(string.Empty, "Votre compte est suspendu. Contactez un administrateur.");
                return View(model);
            }

            ModelState.AddModelError(string.Empty, "Tentative de connexion non valide.");
            return View(model);
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Register(string? returnUrl = null)
        {
            if (IsPublicRegistrationDisabled())
            {
                TempData["ErrorMessage"] = "Inscription publique désactivée. Utilisez un compte d'équipe.";
                return RedirectToAction(nameof(Login), new { returnUrl });
            }

            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model, string? returnUrl = null)
        {
            if (IsPublicRegistrationDisabled())
            {
                TempData["ErrorMessage"] = "Inscription publique désactivée. Utilisez un compte d'équipe.";
                return RedirectToAction(nameof(Login), new { returnUrl });
            }

            ViewData["ReturnUrl"] = returnUrl;

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            model.Email = model.Email.Trim();
            var devBypassEmailFlow = _environment.IsDevelopment() && !IsSmtpConfigured();

            if (!devBypassEmailFlow && !IsSmtpConfigured())
            {
                ModelState.AddModelError(string.Empty, "Inscription indisponible: service d'email non configuré.");
                return View(model);
            }

            if (!devBypassEmailFlow && !await IsEmailDomainReachableAsync(model.Email))
            {
                ModelState.AddModelError(nameof(model.Email), "Adresse email invalide ou domaine introuvable.");
                return View(model);
            }

            var user = new ApplicationUser
            {
                UserName = model.Email,
                Email = model.Email,
                Nom = model.Nom,
                EmailConfirmed = devBypassEmailFlow
            };

            var result = await _userManager.CreateAsync(user, model.Password);

            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }

                return View(model);
            }

            var roleResult = await _userManager.AddToRoleAsync(user, "Utilisateur");
            if (!roleResult.Succeeded)
            {
                await _userManager.DeleteAsync(user);
                foreach (var error in roleResult.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }

                return View(model);
            }

            if (devBypassEmailFlow)
            {
                _logger.LogInformation("Inscription en mode développement: email confirmé automatiquement pour {Email}.", model.Email);
                TempData["SuccessMessage"] = "Compte créé en mode développement. Vous pouvez vous connecter directement.";
                return RedirectToAction(nameof(Login), new { returnUrl });
            }

            var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
            var callbackUrl = Url.Action(
                nameof(ConfirmEmail),
                "Account",
                values: new { userId = user.Id, code, returnUrl },
                protocol: Request.Scheme);

            if (string.IsNullOrWhiteSpace(callbackUrl))
            {
                await _userManager.DeleteAsync(user);
                ModelState.AddModelError(string.Empty, "Impossible de générer le lien de confirmation.");
                return View(model);
            }

            try
            {
                await _emailSender.SendEmailAsync(
                    model.Email,
                    "Confirmation de votre compte DAR.COM",
                    $"Confirmez votre compte en cliquant ici : <a href='{callbackUrl}'>Confirmer mon email</a>");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Échec d'envoi de l'email de confirmation vers {Email}. Suppression du compte non confirmé.", model.Email);
                await _userManager.DeleteAsync(user);
                ModelState.AddModelError(nameof(model.Email), "Adresse email invalide ou impossible à joindre. Vérifiez puis réessayez.");
                return View(model);
            }

            return RedirectToAction(nameof(RegisterConfirmation), new { email = model.Email, emailSent = true });
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult RegisterConfirmation(string email, bool emailSent = true)
        {
            ViewBag.Email = email;
            ViewBag.EmailSent = emailSent;
            return View();
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> ConfirmEmail(string userId, string code, string? returnUrl = null)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(code))
            {
                ViewBag.ConfirmationSucceeded = false;
                return View();
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                ViewBag.ConfirmationSucceeded = false;
                return View();
            }

            var decodedCode = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
            var result = await _userManager.ConfirmEmailAsync(user, decodedCode);

            ViewBag.ConfirmationSucceeded = result.Succeeded;
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<IActionResult> ManageRoles()
        {
            return View(await _userService.GetUsersAsync());
        }

        [HttpPost]
        [Authorize(Roles = "SuperAdmin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignRole(string userId, string roleName)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            var result = await _userService.AssignRoleAsync(
                userId,
                roleName,
                currentUser?.Id,
                User.IsInRole("SuperAdmin"));

            if (result.Success)
            {
                TempData["SuccessMessage"] = result.Message;
            }
            else
            {
                TempData["ErrorMessage"] = result.Message;
            }

            return RedirectToAction(nameof(ManageRoles));
        }

        private bool IsSmtpConfigured()
        {
            return !string.IsNullOrWhiteSpace(_configuration["Smtp:Host"])
                && !string.IsNullOrWhiteSpace(_configuration["Smtp:From"]);
        }

        private bool IsPublicRegistrationDisabled()
        {
            return _configuration.GetValue<bool>("Bootstrap:DisablePublicRegistration");
        }

        private async Task<bool> IsEmailDomainReachableAsync(string email)
        {
            try
            {
                var address = new MailAddress(email);
                var domain = address.Host;
                if (string.IsNullOrWhiteSpace(domain))
                {
                    return false;
                }

                var addresses = await Dns.GetHostAddressesAsync(domain);
                return addresses.Length > 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
