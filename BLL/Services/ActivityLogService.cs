using BLL.Interfaces;
using Core.Entities;
using DAL.Interfaces;
using Microsoft.AspNetCore.Http;

namespace BLL.Services
{
    public class ActivityLogService : IActivityLogService
    {
        private readonly IActivityLogRepository _logRepo;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public ActivityLogService(IActivityLogRepository logRepo, IHttpContextAccessor httpContextAccessor)
        {
            _logRepo = logRepo;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task LogActionAsync(string userId, string actionType, string entityName, string entityId, string description)
        {
            var ipAddress = _httpContextAccessor.HttpContext?.Connection?.RemoteIpAddress?.ToString();

            var log = new ActivityLog
            {
                UserId = userId,
                ActionType = actionType,
                EntityName = entityName,
                EntityId = entityId,
                Description = description,
                IpAddress = ipAddress,
                Timestamp = DateTime.UtcNow
            };

            await _logRepo.AddAsync(log);
            await _logRepo.SaveChangesAsync();
        }

        public async Task<List<ActivityLog>> GetSystemLogsAsync()
        {
            return await _logRepo.GetSystemLogsAsync(200);
        }

        public async Task<List<ActivityLog>> GetProjectLogsAsync(int projectId)
        {
            return await _logRepo.GetLogsByEntityAsync("Project", projectId.ToString());
        }
    }
}
