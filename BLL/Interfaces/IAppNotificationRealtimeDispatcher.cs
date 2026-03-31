using Core.DTOs.Responses;

namespace BLL.Interfaces
{
    public interface IAppNotificationRealtimeDispatcher
    {
        Task DispatchAsync(string userId, NotificationPayload notification);
    }
}
