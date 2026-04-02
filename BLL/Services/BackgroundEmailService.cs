using BLL.Interfaces;
using BLL.Exceptions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Mail;

namespace BLL.Services
{
    /// <summary>
    /// SMTP email sender that validates configuration and propagates delivery errors
    /// so callers can decide whether to block, warn, or just log the failure.
    /// </summary>
    public class BackgroundEmailService : IEmailService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<BackgroundEmailService> _logger;
        private readonly IHostEnvironment _hostEnvironment;

        public BackgroundEmailService(
            IConfiguration config,
            ILogger<BackgroundEmailService> logger,
            IHostEnvironment hostEnvironment)
        {
            _config = config;
            _logger = logger;
            _hostEnvironment = hostEnvironment;
        }

        public async Task SendEmailAsync(string toEmail, string subject, string htmlBody)
        {
            if (string.IsNullOrWhiteSpace(toEmail))
            {
                throw new EmailDeliveryException("SMTP delivery failed because the recipient email address is empty.");
            }

            var smtpOptions = GetSmtpOptions();

            try
            {
                using var client = smtpOptions.UsePickupDirectory
                    ? new SmtpClient
                    {
                        DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory,
                        PickupDirectoryLocation = smtpOptions.PickupDirectoryLocation,
                        Timeout = smtpOptions.TimeoutMilliseconds
                    }
                    : new SmtpClient(smtpOptions.MailServer, smtpOptions.MailPort)
                    {
                        EnableSsl = smtpOptions.EnableSsl,
                        UseDefaultCredentials = smtpOptions.UseDefaultCredentials,
                        DeliveryMethod = SmtpDeliveryMethod.Network,
                        Timeout = smtpOptions.TimeoutMilliseconds
                    };

                if (smtpOptions.UsePickupDirectory && !string.IsNullOrWhiteSpace(smtpOptions.PickupDirectoryLocation))
                {
                    Directory.CreateDirectory(smtpOptions.PickupDirectoryLocation);
                }

                if (!smtpOptions.UsePickupDirectory && !smtpOptions.UseDefaultCredentials)
                {
                    client.Credentials = new NetworkCredential(
                        smtpOptions.Username,
                        smtpOptions.Password);
                }

                using var mailMessage = new MailMessage
                {
                    From = new MailAddress(smtpOptions.SenderEmail, smtpOptions.SenderName),
                    Subject = subject,
                    Body = htmlBody,
                    IsBodyHtml = true
                };

                mailMessage.To.Add(toEmail);

                await client.SendMailAsync(mailMessage);
                if (smtpOptions.UsePickupDirectory)
                {
                    _logger.LogInformation(
                        "Email written to pickup directory {PickupDirectory} for {ToEmail}: {Subject}",
                        smtpOptions.PickupDirectoryLocation,
                        toEmail,
                        subject);
                }
                else
                {
                    _logger.LogInformation("Email sent successfully to {ToEmail}: {Subject}", toEmail, subject);
                }
            }
            catch (EmailDeliveryException)
            {
                throw;
            }
            catch (SmtpException ex)
            {
                _logger.LogError(ex, "SMTP delivery failed to {ToEmail}: {Subject}", toEmail, subject);
                throw new EmailDeliveryException(
                    $"SMTP delivery failed for {toEmail}. Server response: {ex.StatusCode}. {ex.Message}",
                    ex,
                    new[] { toEmail });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email delivery failed to {ToEmail}: {Subject}", toEmail, subject);
                throw new EmailDeliveryException(
                    $"Email delivery failed for {toEmail}. {ex.Message}",
                    ex,
                    new[] { toEmail });
            }
        }

        private SmtpOptions GetSmtpOptions()
        {
            var mailServer = _config["EmailSettings:MailServer"]?.Trim();
            var senderName = _config["EmailSettings:SenderName"]?.Trim();
            var senderEmail = _config["EmailSettings:SenderEmail"]?.Trim();
            var username = _config["EmailSettings:Username"]?.Trim();
            var password = _config["EmailSettings:Password"];
            var deliveryMode = _config["EmailSettings:DeliveryMode"]?.Trim();
            var pickupDirectory = _config["EmailSettings:PickupDirectory"]?.Trim();
            var useDefaultCredentials = bool.TryParse(_config["EmailSettings:UseDefaultCredentials"], out var parsedDefaultCredentials)
                && parsedDefaultCredentials;
            var enableSsl = !bool.TryParse(_config["EmailSettings:EnableSsl"], out var parsedEnableSsl) || parsedEnableSsl;

            if (!int.TryParse(_config["EmailSettings:MailPort"], out var mailPort))
            {
                mailPort = 587;
            }

            if (!int.TryParse(_config["EmailSettings:TimeoutSeconds"], out var timeoutSeconds))
            {
                timeoutSeconds = 30;
            }

            if (string.IsNullOrWhiteSpace(senderEmail))
            {
                throw new EmailDeliveryException("EmailSettings:SenderEmail is missing. Please configure the SMTP sender address before sending email.");
            }

            bool usePickupDirectory =
                string.Equals(deliveryMode, "PickupDirectory", StringComparison.OrdinalIgnoreCase) ||
                (string.IsNullOrWhiteSpace(deliveryMode) &&
                 _hostEnvironment.IsDevelopment() &&
                 !useDefaultCredentials &&
                 string.IsNullOrWhiteSpace(password));

            if (usePickupDirectory)
            {
                var effectivePickupDirectory = string.IsNullOrWhiteSpace(pickupDirectory)
                    ? Path.Combine(Directory.GetCurrentDirectory(), "mail-drop")
                    : Path.GetFullPath(Path.IsPathRooted(pickupDirectory)
                        ? pickupDirectory
                        : Path.Combine(Directory.GetCurrentDirectory(), pickupDirectory));

                return new SmtpOptions
                {
                    SenderName = string.IsNullOrWhiteSpace(senderName) ? senderEmail : senderName,
                    SenderEmail = senderEmail,
                    TimeoutMilliseconds = Math.Max(timeoutSeconds, 1) * 1000,
                    UsePickupDirectory = true,
                    PickupDirectoryLocation = effectivePickupDirectory
                };
            }

            if (string.IsNullOrWhiteSpace(mailServer))
            {
                throw new EmailDeliveryException("EmailSettings:MailServer is missing. Please configure the SMTP server before sending email.");
            }

            if (mailPort <= 0)
            {
                throw new EmailDeliveryException($"EmailSettings:MailPort is invalid ({mailPort}).");
            }

            if (!useDefaultCredentials)
            {
                username = string.IsNullOrWhiteSpace(username) ? senderEmail : username;

                if (string.IsNullOrWhiteSpace(password))
                {
                    throw new EmailDeliveryException(
                        "EmailSettings:Password is missing. SMTP authentication cannot run without a password or app password.");
                }
            }

            return new SmtpOptions
            {
                MailServer = mailServer,
                MailPort = mailPort,
                SenderName = string.IsNullOrWhiteSpace(senderName) ? senderEmail : senderName,
                SenderEmail = senderEmail,
                Username = string.IsNullOrWhiteSpace(username) ? senderEmail : username,
                Password = password ?? string.Empty,
                EnableSsl = enableSsl,
                UseDefaultCredentials = useDefaultCredentials,
                TimeoutMilliseconds = Math.Max(timeoutSeconds, 1) * 1000
            };
        }

        private sealed class SmtpOptions
        {
            public string MailServer { get; init; } = string.Empty;
            public int MailPort { get; init; }
            public string SenderName { get; init; } = string.Empty;
            public string SenderEmail { get; init; } = string.Empty;
            public string Username { get; init; } = string.Empty;
            public string Password { get; init; } = string.Empty;
            public bool EnableSsl { get; init; }
            public bool UseDefaultCredentials { get; init; }
            public int TimeoutMilliseconds { get; init; }
            public bool UsePickupDirectory { get; init; }
            public string PickupDirectoryLocation { get; init; } = string.Empty;
        }
    }
}
