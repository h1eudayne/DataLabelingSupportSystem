using Core.Entities;

namespace Core.Interfaces
{
    public interface IActivityLogRepository : IRepository<ActivityLog>
    {
        Task<List<ActivityLog>> GetSystemLogsAsync(int limit = 100);
        Task<List<ActivityLog>> GetLogsByUserAsync(string userId);
        Task<List<ActivityLog>> GetLogsByEntityAsync(string entityName, string entityId);
    }
}
