namespace BLL.Interfaces
{
    public interface IAppNotificationService
    {
        Task SendNotificationAsync(
            string userId,
            string message,
            string type = "Info",
            string? referenceType = null,
            string? referenceId = null,
            string? actionKey = null,
            string? metadataJson = null);
        Task<IEnumerable<object>> GetUnreadNotificationsForHubAsync(string userId);
        Task MarkListAsReadAsync(List<int> notificationIds, string userId);
        Task<int> GetUnreadCountAsync(string userId);
        Task<IEnumerable<object>> GetMyNotificationsAsync(string userId);
        Task MarkAsReadAsync(int id, string userId);
        Task MarkAllAsReadAsync(string userId);
    }
}
