using BLL.Services;
using BLL.Interfaces;
using Core.DTOs.Responses;
using Core.Entities;
using Core.Interfaces;
using Moq;

namespace BLL.Tests
{
    public class AppNotificationServiceTests
    {
        private readonly Mock<IRepository<AppNotification>> _notificationRepoMock;
        private readonly Mock<IAppNotificationRealtimeDispatcher> _realtimeDispatcherMock;
        private readonly AppNotificationService _service;

        public AppNotificationServiceTests()
        {
            _notificationRepoMock = new Mock<IRepository<AppNotification>>();
            _realtimeDispatcherMock = new Mock<IAppNotificationRealtimeDispatcher>();
            _service = new AppNotificationService(_notificationRepoMock.Object, _realtimeDispatcherMock.Object);
        }

        [Fact]
        public async Task SendNotificationAsync_SetsNonNullTitleBeforeSaving()
        {
            AppNotification? captured = null;

            _notificationRepoMock
                .Setup(r => r.AddAsync(It.IsAny<AppNotification>()))
                .Callback<AppNotification>(notification => captured = notification)
                .Returns(Task.CompletedTask);

            _notificationRepoMock
                .Setup(r => r.SaveChangesAsync())
                .Returns(Task.CompletedTask);

            await _service.SendNotificationAsync("user-1", "Tasks assigned", "Success");

            Assert.NotNull(captured);
            Assert.Equal("user-1", captured!.UserId);
            Assert.Equal("Success Notification", captured.Title);
            Assert.Equal("Tasks assigned", captured.Message);
            Assert.Equal("Success", captured.Type);
            _notificationRepoMock.Verify(r => r.SaveChangesAsync(), Times.Once);
            _realtimeDispatcherMock.Verify(r => r.DispatchAsync(
                "user-1",
                It.Is<NotificationPayload>(p =>
                    p.Title == "Success Notification" &&
                    p.Message == "Tasks assigned" &&
                    p.Type == "Success" &&
                    p.Timestamp != default)),
                Times.Once);
        }
    }
}

