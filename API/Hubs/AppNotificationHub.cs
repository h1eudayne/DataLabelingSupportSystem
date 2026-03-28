using BLL.Interfaces;
using DAL;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace API.Hubs
{
    [Authorize]
    public class AppNotificationHub : Hub
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<AppNotificationHub> _hubContext;

        public AppNotificationHub(
            ApplicationDbContext context,
            IHubContext<AppNotificationHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
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
                var unreadNotifications = _context.AppNotifications
                    .Where(n => n.UserId == userId && !n.IsRead)
                    .OrderByDescending(n => n.CreatedAt)
                    .Take(50)
                    .ToList();

                if (unreadNotifications.Any())
                {
                    Console.WriteLine($"[SignalR] Sending {unreadNotifications.Count} pending notifications to user {userId}");

                    foreach (var notification in unreadNotifications)
                    {
                        await Clients.User(userId).SendAsync("ReceiveNotification", new
                        {
                            Id = notification.Id,
                            Title = notification.Title,
                            Message = notification.Message,
                            Type = notification.Type,
                            IsRead = notification.IsRead,
                            Timestamp = notification.CreatedAt
                        });
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
            if (string.IsNullOrEmpty(userId)) return;

            try
            {
                var notifications = _context.AppNotifications
                    .Where(n => n.UserId == userId && notificationIds.Contains(n.Id) && !n.IsRead)
                    .ToList();

                foreach (var notification in notifications)
                {
                    notification.IsRead = true;
                }

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SignalR] Error acknowledging notifications: {ex.Message}");
            }
        }
    }
}
