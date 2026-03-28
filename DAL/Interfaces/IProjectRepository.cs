using Core.Entities;

namespace DAL.Interfaces
{
    public interface IProjectRepository : IRepository<Project>
    {
        Task<Project?> GetProjectWithDetailsAsync(int id);
        Task<Project?> GetProjectForExportAsync(int id);
        Task<Project?> GetProjectWithStatsDataAsync(int id);
        Task<List<Project>> GetProjectsByManagerIdAsync(string managerId);
        Task<Dictionary<int, int>> GetProjectLabelCountsAsync(int projectId);
        Task<List<Project>> GetProjectsByAnnotatorAsync(string annotatorId);
        Task<List<Project>> GetProjectsByIdsAsync(List<int> projectIds);
        Task<List<DataItem>> GetProjectDataItemsAsync(int projectId);
        Task<int> GetProjectDataItemsCountAsync(int projectId);
        Task<List<Project>> GetAllProjectsForAdminStatsAsync();
        Task<List<DataItem>> GetDataItemsByBucketIdAsync(int projectId, int bucketId);
        Task AddDataItemsAsync(List<DataItem> dataItems);
    }
}
