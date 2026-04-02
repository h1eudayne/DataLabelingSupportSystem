using BLL.Services;
using BLL.Interfaces;
using Core.Constants;
using Core.DTOs.Responses;
using Core.Entities;
using Core.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BLL.Tests
{
    public class AppNotificationServiceTests
    {
        private readonly Mock<IRepository<AppNotification>> _notificationRepoMock;
        private readonly Mock<IRepository<GlobalUserBanRequest>> _globalBanRequestRepoMock;
        private readonly Mock<IAppNotificationRealtimeDispatcher> _realtimeDispatcherMock;
        private readonly AppNotificationService _service;

        public AppNotificationServiceTests()
        {
            _notificationRepoMock = new Mock<IRepository<AppNotification>>();
            _globalBanRequestRepoMock = new Mock<IRepository<GlobalUserBanRequest>>();
            _realtimeDispatcherMock = new Mock<IAppNotificationRealtimeDispatcher>();
            _service = new AppNotificationService(
                _notificationRepoMock.Object,
                _globalBanRequestRepoMock.Object,
                NullLogger<AppNotificationService>.Instance,
                _realtimeDispatcherMock.Object);
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
                    p.Timestamp != default &&
                    p.Timestamp.Kind == DateTimeKind.Utc)),
                Times.Once);
        }

        [Fact]
        public async Task SendNotificationAsync_RealtimeDispatchFails_StillPersistsNotification()
        {
            _notificationRepoMock
                .Setup(r => r.AddAsync(It.IsAny<AppNotification>()))
                .Returns(Task.CompletedTask);

            _notificationRepoMock
                .Setup(r => r.SaveChangesAsync())
                .Returns(Task.CompletedTask);

            _realtimeDispatcherMock
                .Setup(r => r.DispatchAsync(It.IsAny<string>(), It.IsAny<NotificationPayload>()))
                .ThrowsAsync(new InvalidOperationException("SignalR unavailable"));

            await _service.SendNotificationAsync(
                "user-2",
                "Pending approval",
                "Info",
                metadataJson: "{\"requestStatus\":\"Pending\"}");

            _notificationRepoMock.Verify(r => r.AddAsync(It.Is<AppNotification>(n =>
                n.UserId == "user-2" &&
                n.MetadataJson == "{\"requestStatus\":\"Pending\"}")), Times.Once);
            _notificationRepoMock.Verify(r => r.SaveChangesAsync(), Times.Once);
        }

        [Fact]
        public async Task GetMyNotificationsAsync_StalePendingGlobalBanNotification_IsReconciledBeforeReturn()
        {
            var notification = new AppNotification
            {
                Id = 14,
                UserId = "manager-1",
                Message = "Please review global ban request.",
                Type = "GlobalUserBanApproval",
                ReferenceType = "GlobalUserBanRequest",
                ReferenceId = "1",
                ActionKey = "ResolveGlobalUserBanRequest",
                MetadataJson = "{\"banRequestId\":1,\"requestStatus\":\"Pending\"}",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };
            var request = new GlobalUserBanRequest
            {
                Id = 1,
                Status = GlobalUserBanRequestStatusConstants.Approved,
                DecisionNote = "Already handled",
                ResolvedAt = DateTime.UtcNow.AddMinutes(-2)
            };

            _notificationRepoMock
                .Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<AppNotification, bool>>>()))
                .ReturnsAsync(new List<AppNotification> { notification });
            _notificationRepoMock
                .Setup(r => r.SaveChangesAsync())
                .Returns(Task.CompletedTask);
            _globalBanRequestRepoMock
                .Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<GlobalUserBanRequest, bool>>>()))
                .ReturnsAsync(new List<GlobalUserBanRequest> { request });

            var result = (await _service.GetMyNotificationsAsync("manager-1")).ToList();

            Assert.Single(result);
            Assert.True(notification.IsRead);
            Assert.Contains("\"requestStatus\":\"Approved\"", notification.MetadataJson);
            Assert.Contains("\"decisionNote\":\"Already handled\"", notification.MetadataJson);
            var createdAtProperty = result[0].GetType().GetProperty("CreatedAt");
            Assert.NotNull(createdAtProperty);
            var normalizedCreatedAt = Assert.IsType<DateTime>(createdAtProperty!.GetValue(result[0]));
            Assert.Equal(DateTimeKind.Utc, normalizedCreatedAt.Kind);
            _notificationRepoMock.Verify(r => r.Update(It.Is<AppNotification>(n =>
                n.Id == 14 &&
                n.IsRead &&
                n.MetadataJson != null &&
                n.MetadataJson.Contains("\"requestStatus\":\"Approved\""))), Times.Once);
            _notificationRepoMock.Verify(r => r.SaveChangesAsync(), Times.Once);
        }
    }
}

