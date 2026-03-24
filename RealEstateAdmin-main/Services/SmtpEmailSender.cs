using System.Net;
using System.Net.Mail;
using Microsoft.AspNetCore.Identity.UI.Services;

namespace RealEstateAdmin.Services
{
    public class SmtpEmailSender : IEmailSender
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<SmtpEmailSender> _logger;

        public SmtpEmailSender(IConfiguration configuration, ILogger<SmtpEmailSender> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            var host = _configuration["Smtp:Host"];
            var from = _configuration["Smtp:From"];
            var username = _configuration["Smtp:Username"];
            var password = _configuration["Smtp:Password"];
            var enableSsl = bool.TryParse(_configuration["Smtp:EnableSsl"], out var ssl) && ssl;

            if (!int.TryParse(_configuration["Smtp:Port"], out var port))
            {
                port = 587;
            }

            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from))
            {
                _logger.LogWarning("SMTP non configuré. Email non envoyé à {Email}. Sujet: {Subject}", email, subject);
                _logger.LogInformation("Contenu email (debug): {Body}", htmlMessage);
                return;
            }

            using var message = new MailMessage(from, email, subject, htmlMessage)
            {
                IsBodyHtml = true
            };

            using var client = new SmtpClient(host, port)
            {
                EnableSsl = enableSsl
            };

            if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
            {
                client.Credentials = new NetworkCredential(username, password);
            }

            await client.SendMailAsync(message);
        }
    }
}
