using API.Hubs;
using BLL.Interfaces;
using Core.DTOs.Responses;
using Microsoft.AspNetCore.SignalR;

namespace API.Services
{
    public class SignalRAppNotificationRealtimeDispatcher : IAppNotificationRealtimeDispatcher
    {
        private readonly IHubContext<AppNotificationHub> _hubContext;

        public SignalRAppNotificationRealtimeDispatcher(IHubContext<AppNotificationHub> hubContext)
        {
            _hubContext = hubContext;
        }

        public Task DispatchAsync(string userId, NotificationPayload notification)
        {
            return _hubContext.Clients.User(userId).SendAsync("ReceiveNotification", notification);
        }
    }
}
