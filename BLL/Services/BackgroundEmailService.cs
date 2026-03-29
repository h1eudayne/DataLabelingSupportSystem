using BLL.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Mail;

namespace BLL.Services
{
    /// <summary>
    /// Fire-and-forget email service that sends emails in a background thread
    /// to avoid blocking API responses. Falls back gracefully on SMTP failures.
    /// </summary>
    public class BackgroundEmailService : IEmailService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<BackgroundEmailService> _logger;

        public BackgroundEmailService(IConfiguration config, ILogger<BackgroundEmailService> logger)
        {
            _config = config;
            _logger = logger;
        }

        public Task SendEmailAsync(string toEmail, string subject, string htmlBody)
        {
            var mailServer = _config["EmailSettings:MailServer"];
            var mailPortStr = _config["EmailSettings:MailPort"] ?? "587";
            var senderName = _config["EmailSettings:SenderName"];
            var senderEmail = _config["EmailSettings:SenderEmail"];
            var password = _config["EmailSettings:Password"];

            if (string.IsNullOrWhiteSpace(mailServer) || string.IsNullOrWhiteSpace(senderEmail))
            {
                _logger.LogWarning("Email settings are not configured. Skipping email to {ToEmail}.", toEmail);
                return Task.CompletedTask;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    var mailPort = int.Parse(mailPortStr);

                    using var client = new SmtpClient(mailServer, mailPort)
                    {
                        EnableSsl = true,
                        UseDefaultCredentials = false,
                        Credentials = new NetworkCredential(senderEmail, password),
                        Timeout = 30000
                    };

                    var mailMessage = new MailMessage
                    {
                        From = new MailAddress(senderEmail!, senderName),
                        Subject = subject,
                        Body = htmlBody,
                        IsBodyHtml = true
                    };

                    mailMessage.To.Add(toEmail);

                    await client.SendMailAsync(mailMessage);
                    _logger.LogInformation("Email sent successfully to {ToEmail}: {Subject}", toEmail, subject);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send background email to {ToEmail}: {Subject}", toEmail, subject);
                }
            });

            return Task.CompletedTask;
        }
    }
}
