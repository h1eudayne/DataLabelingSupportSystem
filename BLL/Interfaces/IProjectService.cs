using Core.DTOs.Requests;
using Core.DTOs.Responses;

namespace BLL.Interfaces
{
    public interface IProjectService
    {
        Task<ProjectDetailResponse> CreateProjectAsync(string managerId, CreateProjectRequest request);
        Task<List<AnnotatorProjectStatsResponse>> GetAssignedProjectsAsync(string annotatorId);
        Task ImportDataItemsAsync(int projectId, List<string> storageUrls);
        Task<ProjectDetailResponse?> GetProjectDetailsAsync(int projectId);
        Task<ManagerStatsResponse> GetManagerStatsAsync(string managerId);
        Task<List<ProjectSummaryResponse>> GetProjectsByManagerAsync(string managerId);
        Task UpdateProjectAsync(int projectId, UpdateProjectRequest request, string actingUserId);

        Task<int> UploadDirectDataItemsAsync(int projectId, List<(Stream Content, string Extension)> files, string webRootPath);

        Task DeleteProjectAsync(int projectId);
        Task<List<ProjectSummaryResponse>> GetAllProjectsForAdminAsync();
        Task<List<AnnotatorProjectStatsResponse>> GetAssignedProjectsForUserAsync(string userId);
        Task AssignReviewersAsync(AssignReviewersRequest request);
        Task CompleteProjectAsync(int projectId, string managerId);
        Task<ProjectCompletionReviewResponse> GetProjectCompletionReviewAsync(int projectId, string managerId);
        Task ReturnProjectItemForReworkAsync(int projectId, int assignmentId, string managerId, string comment);
        Task ArchiveProjectAsync(int projectId, string managerId);

        Task ActivateProjectAsync(int projectId, string managerId);
        Task<byte[]> ExportProjectCsvAsync(int projectId, string userId);
        Task<List<Core.DTOs.Responses.BucketResponse>> GetBucketsAsync(int projectId, string userId);

        Task<byte[]> ExportProjectDataAsync(int projectId, string userId);
        Task<ProjectStatisticsResponse> GetProjectStatisticsAsync(int projectId);
        Task<object> GetUserProjectsByUserIdAsync(string userId);
        Task RemoveUserFromProjectAsync(int projectId, string userId);

        Task ToggleUserLockAsync(int projectId, string userId, bool lockStatus, string managerId);
    }
}
