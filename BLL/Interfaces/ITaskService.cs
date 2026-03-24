using Core.DTOs.Requests;
using Core.DTOs.Responses;

namespace BLL.Interfaces
{
    public interface ITaskService
    {
        Task AssignTasksToAnnotatorAsync(AssignTaskRequest request, string managerId);
        Task<AnnotatorStatsResponse> GetAnnotatorStatsAsync(string annotatorId);
        Task<List<AssignedProjectResponse>> GetAssignedProjectsAsync(string annotatorId);
        Task<List<AssignmentResponse>> GetTaskImagesAsync(int projectId, string annotatorId);
        Task<AssignmentResponse> GetAssignmentByIdAsync(int assignmentId, string userId);
        Task SaveDraftAsync(string userId, SubmitAnnotationRequest request);
        Task SubmitTaskAsync(string userId, SubmitAnnotationRequest request);
        Task AssignTeamAsync(string managerId, AssignTeamRequest request);
        Task<AssignmentResponse> JumpToDataItemAsync(int projectId, int dataItemId, string userId);
        Task<List<AssignmentResponse>> GetTasksByBucketAsync(int projectId, int bucketId, string userId);
        Task<SubmitMultipleTasksResponse> SubmitMultipleTasksAsync(string userId, SubmitMultipleTasksRequest request);
    }
}