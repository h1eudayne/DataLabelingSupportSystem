namespace BLL.Interfaces
{
    public interface IAppNotificationService
    {
        Task SendNotificationAsync(string userId, string message, string type = "Info");
        Task<int> GetUnreadCountAsync(string userId);
    }
}