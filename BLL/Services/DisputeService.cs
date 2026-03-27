using BLL.Interfaces;
using Core.Constants;
using Core.DTOs.Requests;
using Core.DTOs.Responses;
using Core.Entities;
using DAL.Interfaces;

namespace BLL.Services
{
    public class DisputeService : IDisputeService
    {
        private readonly IDisputeRepository _disputeRepo;
        private readonly IAssignmentRepository _assignmentRepo;
        private readonly IStatisticService _statisticService;
        private readonly IRepository<ReviewLog> _reviewLogRepo;
        private readonly IProjectRepository _projectRepo;
        private readonly IRepository<DataItem> _dataItemRepo;
        private readonly IAppNotificationService _notification;
        private readonly IActivityLogService _logService; 

        public DisputeService(
            IDisputeRepository disputeRepo,
            IAssignmentRepository assignmentRepo,
            IStatisticService statisticService,
            IRepository<ReviewLog> reviewLogRepo,
            IProjectRepository projectRepo,
            IAppNotificationService notification,
            IRepository<DataItem> dataItemRepo,
            IActivityLogService logService) 
        {
            _disputeRepo = disputeRepo;
            _assignmentRepo = assignmentRepo;
            _statisticService = statisticService;
            _reviewLogRepo = reviewLogRepo;
            _projectRepo = projectRepo;
            _dataItemRepo = dataItemRepo;
            _notification = notification;
            _logService = logService;
        }

        public async Task<DisputeResponse> CreateDisputeAsync(string annotatorId, CreateDisputeRequest request)
        {
            var assignment = await _assignmentRepo.GetAssignmentWithDetailsAsync(request.AssignmentId);
            if (assignment == null) throw new Exception("Assignment not found");
            if (assignment.AnnotatorId != annotatorId) throw new UnauthorizedAccessException("You can only dispute your own assignments");
            if (assignment.Status != TaskStatusConstants.Rejected) throw new Exception("You can only dispute rejected tasks");

            var lastReview = assignment.ReviewLogs?.OrderByDescending(r => r.CreatedAt).FirstOrDefault();
            if (lastReview != null && (DateTime.UtcNow - lastReview.CreatedAt).TotalHours > 48)
            {
                throw new Exception("Dispute Window Expired: You can only file a dispute within 48 hours of rejection.");
            }

            var dispute = new Dispute
            {
                AssignmentId = request.AssignmentId,
                Reason = request.Reason,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow
            };

            await _disputeRepo.AddAsync(dispute);
            await _disputeRepo.SaveChangesAsync();

            await _logService.LogActionAsync(
                annotatorId,
                "CreateDispute",
                "Assignment",
                assignment.Id.ToString(),
                $"Annotator filed a dispute for Task {assignment.Id}. Reason: {request.Reason}"
            );

            if (!string.IsNullOrEmpty(assignment.ReviewerId))
            {
                await _notification.SendNotificationAsync(assignment.ReviewerId, $"A dispute has been filed for Assignment {assignment.Id}", "Warning");
            }

            return new DisputeResponse
            {
                Id = dispute.Id,
                AssignmentId = dispute.AssignmentId,
                Reason = dispute.Reason,
                Status = dispute.Status,
                CreatedAt = dispute.CreatedAt
            };
        }

        public async Task ResolveDisputeAsync(string managerId, ResolveDisputeRequest request)
        {
            var dispute = await _disputeRepo.GetDisputeWithDetailsAsync(request.DisputeId);
            if (dispute == null) throw new Exception("Dispute not found");

            if (dispute.Status != "Pending") throw new Exception("This dispute has already been resolved.");

            dispute.ManagerComment = request.ManagerComment;
            dispute.ManagerId = managerId;
            dispute.ResolvedAt = DateTime.UtcNow;

            var assignment = dispute.Assignment;
            if (assignment == null) throw new Exception("Related assignment not found");

            var project = await _projectRepo.GetByIdAsync(assignment.ProjectId);
            if (project == null) throw new Exception("Project not found");

            var reviewLogs = assignment.ReviewLogs?.ToList() ?? new List<ReviewLog>();

            if (request.IsAccepted)
            {
                dispute.Status = "Accepted";
                assignment.Status = TaskStatusConstants.Approved;

                if (assignment.DataItemId > 0)
                {
                    var dataItem = await _dataItemRepo.GetByIdAsync(assignment.DataItemId);
                    if (dataItem != null)
                    {
                        dataItem.Status = TaskStatusConstants.Approved;
                        _dataItemRepo.Update(dataItem);
                    }
                }

                var reviewerResults = reviewLogs
                    .Select(r => (
                        reviewerId: r.ReviewerId,
                        wasCorrect: r.Verdict == "Approved"
                    ))
                    .ToList();

                await _statisticService.TrackDisputeResolutionAsync(
                    assignment.AnnotatorId,
                    reviewerResults,
                    assignment.ProjectId,
                    annotatorWasCorrect: true);
            }
            else
            {
                dispute.Status = "Rejected";

                var reviewerResults = reviewLogs
                    .Select(r => (
                        reviewerId: r.ReviewerId,
                        wasCorrect: r.Verdict == "Rejected" || r.Verdict == "Reject"
                    ))
                    .ToList();

                await _statisticService.TrackDisputeResolutionAsync(
                    assignment.AnnotatorId,
                    reviewerResults,
                    assignment.ProjectId,
                    annotatorWasCorrect: false);
            }

            await _disputeRepo.SaveChangesAsync();

            string decision = request.IsAccepted ? "Accepted" : "Rejected";
            await _logService.LogActionAsync(
                managerId,
                "ResolveDispute",
                "Dispute",
                dispute.Id.ToString(),
                $"Manager {decision} dispute {dispute.Id} for Task {assignment.Id}."
            );

            if (request.IsAccepted)
            {
                await _notification.SendNotificationAsync(
                    assignment.AnnotatorId,
                    $"Your dispute for task #{assignment.Id} has been Accepted! Your score has been restored.",
                    "Success");
            }
            else
            {
                await _notification.SendNotificationAsync(
                    assignment.AnnotatorId,
                    $"Your dispute for task #{assignment.Id} has been Rejected. Manager's note: {request.ManagerComment}",
                    "Error");
            }
        }

        public async Task<List<DisputeResponse>> GetDisputesAsync(int projectId, string userId, string role)
        {
            List<Dispute> disputes;

            if (role == UserRoles.Manager || role == UserRoles.Admin)
            {
                disputes = await _disputeRepo.GetDisputesByProjectAsync(projectId);
            }
            else
            {
                disputes = await _disputeRepo.GetDisputesByAnnotatorAsync(userId);
            }

            return disputes.Select(MapToResponse).ToList();
        }

        private static DisputeResponse MapToResponse(Dispute d)
        {
            return new DisputeResponse
            {
                Id = d.Id,
                AssignmentId = d.AssignmentId,
                AnnotatorId = d.AnnotatorId,
                AnnotatorName = d.Annotator?.FullName ?? d.Annotator?.Email,
                Reason = d.Reason,
                Status = d.Status,
                ManagerComment = d.ManagerComment,
                CreatedAt = d.CreatedAt,
                ResolvedAt = d.ResolvedAt,
                ProjectId = d.Assignment?.ProjectId,
                ProjectName = d.Assignment?.Project?.Name,
                DataItemUrl = d.Assignment?.DataItem?.StorageUrl,
                AssignmentStatus = d.Assignment?.Status,
                ReviewerName = d.Assignment?.Reviewer?.FullName ?? d.Assignment?.Reviewer?.Email,
            };
        }
    }
}