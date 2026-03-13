using API.Hubs;
using BLL.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace API.Services
{
    public class AppNotificationService : IAppNotificationService
    {
        private readonly IHubContext<AppNotificationHub> _hubContext;

        public AppNotificationService(IHubContext<AppNotificationHub> hubContext)
        {
            _hubContext = hubContext;
        }

        public async Task SendNotificationAsync(string userId, string message, string type = "Info")
        {
            await _hubContext.Clients.User(userId).SendAsync("ReceiveNotification", new
            {
                Message = message,
                Type = type,
                Timestamp = DateTime.UtcNow
            });
        }
    }
}