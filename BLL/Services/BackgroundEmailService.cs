using BLL.Interfaces;
using BLL.Exceptions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Mail;
using System.Threading.Channels;

namespace BLL.Services
{
    /// <summary>
    /// Queues SMTP email work so request threads are not blocked on slow or unreachable mail servers.
    /// </summary>
    public class BackgroundEmailService : BackgroundService, IEmailService
    {
        private const int DefaultTimeoutSeconds = 10;
        private const int DefaultQueueCapacity = 10000;

        private readonly IConfiguration _config;
        private readonly ILogger<BackgroundEmailService> _logger;
        private readonly IHostEnvironment _hostEnvironment;
        private readonly Channel<QueuedEmailMessage> _queue;

        public BackgroundEmailService(
            IConfiguration config,
            ILogger<BackgroundEmailService> logger,
            IHostEnvironment hostEnvironment)
        {
            _config = config;
            _logger = logger;
            _hostEnvironment = hostEnvironment;
            _queue = CreateQueue();
        }

        public Task SendEmailAsync(string toEmail, string subject, string htmlBody)
        {
            if (string.IsNullOrWhiteSpace(toEmail))
            {
                throw new EmailDeliveryException("SMTP delivery failed because the recipient email address is empty.");
            }

            var smtpOptions = GetSmtpOptions();
            var queuedMessage = new QueuedEmailMessage(toEmail, subject, htmlBody, smtpOptions);

            if (!_queue.Writer.TryWrite(queuedMessage))
            {
                _logger.LogWarning("Email queue is full. Failed to queue email for {ToEmail}: {Subject}", toEmail, subject);
                throw new EmailDeliveryException(
                    $"Email delivery queue is full. Email for {toEmail} could not be scheduled. Increase EmailSettings:QueueCapacity or restore SMTP connectivity.",
                    failedRecipients: new[] { toEmail });
            }

            _logger.LogInformation("Email queued for {ToEmail}: {Subject}", toEmail, subject);
            return Task.CompletedTask;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                while (await _queue.Reader.WaitToReadAsync(stoppingToken))
                {
                    while (_queue.Reader.TryRead(out var message))
                    {
                        try
                        {
                            await SendQueuedEmailAsync(message);
                        }
                        catch (EmailDeliveryException ex)
                        {
                            _logger.LogError(ex, "Queued email delivery failed for {ToEmail}: {Subject}", message.ToEmail, message.Subject);
                        }
                        catch (SmtpException ex)
                        {
                            _logger.LogError(
                                ex,
                                "SMTP delivery failed for queued email to {ToEmail}: {Subject}. Status: {StatusCode}",
                                message.ToEmail,
                                message.Subject,
                                ex.StatusCode);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Queued email delivery failed unexpectedly for {ToEmail}: {Subject}", message.ToEmail, message.Subject);
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _queue.Writer.TryComplete();
            await base.StopAsync(cancellationToken);
        }

        private async Task SendQueuedEmailAsync(QueuedEmailMessage message)
        {
            try
            {
                using var client = message.SmtpOptions.UsePickupDirectory
                    ? new SmtpClient
                    {
                        DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory,
                        PickupDirectoryLocation = message.SmtpOptions.PickupDirectoryLocation,
                        Timeout = message.SmtpOptions.TimeoutMilliseconds
                    }
                    : new SmtpClient(message.SmtpOptions.MailServer, message.SmtpOptions.MailPort)
                    {
                        EnableSsl = message.SmtpOptions.EnableSsl,
                        UseDefaultCredentials = message.SmtpOptions.UseDefaultCredentials,
                        DeliveryMethod = SmtpDeliveryMethod.Network,
                        Timeout = message.SmtpOptions.TimeoutMilliseconds
                    };

                if (message.SmtpOptions.UsePickupDirectory && !string.IsNullOrWhiteSpace(message.SmtpOptions.PickupDirectoryLocation))
                {
                    Directory.CreateDirectory(message.SmtpOptions.PickupDirectoryLocation);
                }

                if (!message.SmtpOptions.UsePickupDirectory && !message.SmtpOptions.UseDefaultCredentials)
                {
                    client.Credentials = new NetworkCredential(
                        message.SmtpOptions.Username,
                        message.SmtpOptions.Password);
                }

                using var mailMessage = new MailMessage
                {
                    From = new MailAddress(message.SmtpOptions.SenderEmail, message.SmtpOptions.SenderName),
                    Subject = message.Subject,
                    Body = message.HtmlBody,
                    IsBodyHtml = true
                };

                mailMessage.To.Add(message.ToEmail);

                await client.SendMailAsync(mailMessage);
                if (message.SmtpOptions.UsePickupDirectory)
                {
                    _logger.LogInformation(
                        "Email written to pickup directory {PickupDirectory} for {ToEmail}: {Subject}",
                        message.SmtpOptions.PickupDirectoryLocation,
                        message.ToEmail,
                        message.Subject);
                }
                else
                {
                    _logger.LogInformation("Email sent successfully to {ToEmail}: {Subject}", message.ToEmail, message.Subject);
                }
            }
            catch (SmtpException ex)
            {
                throw new EmailDeliveryException(
                    $"SMTP delivery failed for {message.ToEmail}. Server response: {ex.StatusCode}. {ex.Message}",
                    ex,
                    new[] { message.ToEmail });
            }
            catch (Exception ex)
            {
                throw new EmailDeliveryException(
                    $"Email delivery failed for {message.ToEmail}. {ex.Message}",
                    ex,
                    new[] { message.ToEmail });
            }
        }

        private Channel<QueuedEmailMessage> CreateQueue()
        {
            var queueCapacity = DefaultQueueCapacity;
            if (int.TryParse(_config["EmailSettings:QueueCapacity"], out var configuredCapacity) && configuredCapacity > 0)
            {
                queueCapacity = configuredCapacity;
            }

            return Channel.CreateBounded<QueuedEmailMessage>(new BoundedChannelOptions(queueCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            });
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
                timeoutSeconds = DefaultTimeoutSeconds;
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

        private sealed record QueuedEmailMessage(
            string ToEmail,
            string Subject,
            string HtmlBody,
            SmtpOptions SmtpOptions);

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
