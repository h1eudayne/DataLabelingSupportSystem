using BLL.Exceptions;
using BLL.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BLL.Tests
{
    public class BackgroundEmailServiceTests
    {
        [Fact]
        public async Task SendEmailAsync_WhenMailServerMissing_ThrowsEmailDeliveryException()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["EmailSettings:SenderEmail"] = "sender@test.com",
                    ["EmailSettings:Password"] = "secret"
                })
                .Build();
            var logger = Mock.Of<ILogger<BackgroundEmailService>>();
            var hostEnvironment = Mock.Of<IHostEnvironment>(env => env.EnvironmentName == Environments.Production);
            var service = new BackgroundEmailService(configuration, logger, hostEnvironment);

            var ex = await Assert.ThrowsAsync<EmailDeliveryException>(() =>
                service.SendEmailAsync("user@test.com", "Subject", "<p>Body</p>"));

            Assert.Contains("MailServer", ex.Message);
        }

        [Fact]
        public async Task SendEmailAsync_WhenPasswordMissing_ThrowsEmailDeliveryException()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["EmailSettings:MailServer"] = "smtp.gmail.com",
                    ["EmailSettings:MailPort"] = "587",
                    ["EmailSettings:SenderEmail"] = "sender@test.com",
                    ["EmailSettings:SenderName"] = "Sender"
                })
                .Build();
            var logger = Mock.Of<ILogger<BackgroundEmailService>>();
            var hostEnvironment = Mock.Of<IHostEnvironment>(env => env.EnvironmentName == Environments.Production);
            var service = new BackgroundEmailService(configuration, logger, hostEnvironment);

            var ex = await Assert.ThrowsAsync<EmailDeliveryException>(() =>
                service.SendEmailAsync("user@test.com", "Subject", "<p>Body</p>"));

            Assert.Contains("Password", ex.Message);
        }

        [Fact]
        public async Task SendEmailAsync_InDevelopmentWithoutPassword_WritesEmailToPickupDirectory()
        {
            var pickupDirectory = Path.Combine(Path.GetTempPath(), $"mail-drop-{Guid.NewGuid():N}");
            Directory.CreateDirectory(pickupDirectory);

            try
            {
                var configuration = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["EmailSettings:SenderEmail"] = "sender@test.com",
                        ["EmailSettings:SenderName"] = "Sender",
                        ["EmailSettings:PickupDirectory"] = pickupDirectory
                    })
                    .Build();
                var logger = Mock.Of<ILogger<BackgroundEmailService>>();
                var hostEnvironment = Mock.Of<IHostEnvironment>(env => env.EnvironmentName == Environments.Development);
                var service = new BackgroundEmailService(configuration, logger, hostEnvironment);

                await service.SendEmailAsync("user@test.com", "Subject", "<p>Body</p>");

                Assert.NotEmpty(Directory.GetFiles(pickupDirectory));
            }
            finally
            {
                if (Directory.Exists(pickupDirectory))
                {
                    Directory.Delete(pickupDirectory, recursive: true);
                }
            }
        }
    }
}
