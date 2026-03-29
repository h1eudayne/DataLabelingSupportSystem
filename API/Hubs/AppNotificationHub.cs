using BLL.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace API.Hubs
{
    [Authorize]
    public class AppNotificationHub : Hub
    {
        private readonly IAppNotificationService _notificationService;

        public AppNotificationHub(IAppNotificationService notificationService)
        {
            _notificationService = notificationService;
        }

        public override async Task OnConnectedAsync()
        {
            var userId = Context.UserIdentifier;
            Console.WriteLine($"[SignalR] User {userId} connected. ConnectionId: {Context.ConnectionId}");

            if (!string.IsNullOrEmpty(userId))
            {
                await SendPendingNotificationsAsync(userId);
            }

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = Context.UserIdentifier;
            Console.WriteLine($"[SignalR] User {userId} disconnected.");
            await base.OnDisconnectedAsync(exception);
        }

        private async Task SendPendingNotificationsAsync(string userId)
        {
            try
            {
                var unreadNotifications = await _notificationService.GetUnreadNotificationsForHubAsync(userId);

                if (unreadNotifications.Any())
                {
                    Console.WriteLine($"[SignalR] Sending {unreadNotifications.Count()} pending notifications to user {userId}");

                    foreach (var notification in unreadNotifications)
                    {
                        await Clients.User(userId).SendAsync("ReceiveNotification", notification);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SignalR] Error sending pending notifications to user {userId}: {ex.Message}");
            }
        }

        public async Task AcknowledgeNotifications(List<int> notificationIds)
        {
            var userId = Context.UserIdentifier;
            if (string.IsNullOrEmpty(userId) || notificationIds == null || !notificationIds.Any()) return;

            try
            {
                await _notificationService.MarkListAsReadAsync(notificationIds, userId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SignalR] Error acknowledging notifications: {ex.Message}");
            }
        }
    }
}