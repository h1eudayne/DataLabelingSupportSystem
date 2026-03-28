using Core.DTOs.Requests;
using Core.DTOs.Responses;

namespace BLL.Interfaces
{
    public interface IReviewService
    {
        Task ReviewAssignmentAsync(string reviewerId, ReviewRequest request);
        Task AuditReviewAsync(string managerId, AuditReviewRequest request);
        Task<List<TaskResponse>> GetTasksForReviewAsync(int projectId, string reviewerId);
        Task<List<AssignedProjectResponse>> GetReviewerProjectsAsync(string reviewerId);

        Task<ReviewQueueResponse> GetReviewQueueGroupedByAnnotatorAsync(int projectId, string reviewerId);

        Task<BatchCompletionStatusResponse> GetBatchCompletionStatusAsync(int projectId, string reviewerId);

        Task HandleEscalatedTaskAsync(string managerId, EscalationActionRequest request);
    }
}
