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
                AnnotatorId = annotatorId,
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

            try
            {
                if (!string.IsNullOrEmpty(assignment.ReviewerId))
                {
                    await _notification.SendNotificationAsync(assignment.ReviewerId, $"A dispute has been filed for Assignment {assignment.Id}", "Warning");
                }

                string? managerId = null;
                if (assignment.Project != null)
                {
                    managerId = assignment.Project.ManagerId;
                }
                else
                {
                    var project = await _projectRepo.GetByIdAsync(assignment.ProjectId);
                    managerId = project?.ManagerId;
                }

                if (!string.IsNullOrEmpty(managerId))
                {
                    await _notification.SendNotificationAsync(
                        managerId,
                        $"A dispute has been filed for Task #{assignment.Id} by annotator. Reason: {request.Reason}",
                        "Warning");
                }
            }
            catch (Exception notificationEx)
            {
                await _logService.LogActionAsync(
                    annotatorId,
                    "NotificationError",
                    "Dispute",
                    dispute.Id.ToString(),
                    $"Dispute created but notification failed: {notificationEx.Message}"
                );
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

            var assignment = dispute.Assignment;
            if (assignment == null) throw new Exception("Related assignment not found");

            var project = await _projectRepo.GetByIdAsync(assignment.ProjectId);
            if (project == null) throw new Exception("Project not found");

            if (string.IsNullOrWhiteSpace(request.ManagerComment))
                throw new Exception("BR-MNG-13: Manager decisions must include reference to official guidelines. Decision note is required.");

            var guidelineVersion = project.GuidelineVersion ?? "1.0";
            bool referencesGuideline = request.ManagerComment.Contains(guidelineVersion, StringComparison.OrdinalIgnoreCase) ||
                                      request.ManagerComment.Contains("guideline", StringComparison.OrdinalIgnoreCase) ||
                                      request.ManagerComment.Contains("v" + guidelineVersion, StringComparison.OrdinalIgnoreCase) ||
                                      request.ManagerComment.Contains("version", StringComparison.OrdinalIgnoreCase);

            if (!referencesGuideline)
            {
                request.ManagerComment = $"[Guideline v{guidelineVersion}] {request.ManagerComment}";
            }

            dispute.ManagerComment = request.ManagerComment;
            dispute.ManagerId = managerId;
            dispute.ResolvedAt = DateTime.UtcNow;

            var reviewLogs = assignment.ReviewLogs?.ToList() ?? new List<ReviewLog>();

            var relatedAssignments = await _assignmentRepo.GetRelatedAssignmentsForDisputeAsync(
                assignment.Id,
                assignment.AnnotatorId,
                assignment.DataItemId);

            var allReviewLogs = new List<ReviewLog>(reviewLogs);
            foreach (var relatedAssignment in relatedAssignments)
            {
                if (relatedAssignment.ReviewLogs != null)
                {
                    allReviewLogs.AddRange(relatedAssignment.ReviewLogs);
                }
            }

            if (request.IsAccepted)
            {
                dispute.Status = "Resolved";
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

                foreach (var relatedAssignment in relatedAssignments)
                {
                    if (relatedAssignment.Status != TaskStatusConstants.Approved)
                    {
                        relatedAssignment.Status = TaskStatusConstants.Approved;
                        _assignmentRepo.Update(relatedAssignment);
                    }
                }

                var reviewerResults = allReviewLogs
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

                foreach (var reviewerId in reviewerResults.Select(r => r.reviewerId).Distinct())
                {
                    var reviewerReview = allReviewLogs.FirstOrDefault(r => r.ReviewerId == reviewerId);
                    if (reviewerReview != null)
                    {
                        string verdict = reviewerReview.Verdict == "Approved" ? "approved" : "rejected";
                        await _notification.SendNotificationAsync(
                            reviewerId,
                            $"Manager has resolved a dispute for a task you {verdict}. " +
                            $"Decision: Annotator was correct. Your accuracy has been updated.",
                            verdict == "rejected" ? "Warning" : "Info");
                    }
                }
            }
            else
            {
                dispute.Status = "Rejected";

                var reviewerResults = allReviewLogs
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

                foreach (var reviewerId in reviewerResults.Select(r => r.reviewerId).Distinct())
                {
                    var reviewerReview = allReviewLogs.FirstOrDefault(r => r.ReviewerId == reviewerId);
                    if (reviewerReview != null)
                    {
                        string verdict = reviewerReview.Verdict == "Approved" ? "approved" : "rejected";
                        await _notification.SendNotificationAsync(
                            reviewerId,
                            $"Manager has resolved a dispute for a task you {verdict}. " +
                            $"Decision: Annotator was incorrect. Your accuracy has been updated.",
                            verdict == "approved" ? "Warning" : "Info");
                    }
                }
            }

            await _disputeRepo.SaveChangesAsync();

            string decision = request.IsAccepted ? "Accepted" : "Rejected";
            await _logService.LogActionAsync(
                managerId,
                "ResolveDispute",
                "Dispute",
                dispute.Id.ToString(),
                $"Manager {decision} dispute {dispute.Id} for Task {assignment.Id} (DataItem {assignment.DataItemId}). " +
                $"Affected reviewers: {allReviewLogs.Select(r => r.ReviewerId).Distinct().Count()}."
            );

            if (request.IsAccepted)
            {
                await _notification.SendNotificationAsync(
                    assignment.AnnotatorId,
                    $"Your dispute for task #{assignment.Id} has been Accepted. Quality credit has been restored to your score.",
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

        public async Task<List<DisputeResolutionDetailsResponse>> GetDisputeResolutionDetailsForReviewerAsync(string reviewerId)
        {
            var allDisputes = await _disputeRepo.GetAllAsync();
            var resolvedDisputes = allDisputes.Where(d => d.Status == "Accepted" || d.Status == "Rejected").ToList();

            var reviewerDisputes = new List<DisputeResolutionDetailsResponse>();

            foreach (var dispute in resolvedDisputes)
            {
                var assignment = await _assignmentRepo.GetAssignmentWithDetailsAsync(dispute.AssignmentId);
                if (assignment == null || assignment.ReviewerId != reviewerId) continue;

                var project = await _projectRepo.GetByIdAsync(assignment.ProjectId);
                var reviewLog = assignment.ReviewLogs?.OrderByDescending(r => r.CreatedAt).FirstOrDefault();

                bool wasOverturned = dispute.Status == "Accepted";
                string resolutionSummary = wasOverturned
                    ? "The dispute was accepted. The annotator's work was approved, overturning your rejection."
                    : "The dispute was rejected. Your rejection decision was upheld by the manager.";

                reviewerDisputes.Add(new DisputeResolutionDetailsResponse
                {
                    DisputeId = dispute.Id,
                    AssignmentId = dispute.AssignmentId,
                    AnnotatorId = dispute.AnnotatorId,
                    AnnotatorName = dispute.Annotator?.FullName ?? dispute.Annotator?.Email ?? "Unknown",
                    Reason = dispute.Reason,
                    Status = dispute.Status,
                    ManagerComment = dispute.ManagerComment,
                    ManagerName = dispute.Manager?.FullName ?? dispute.Manager?.Email ?? "Unknown Manager",
                    ReviewerComment = reviewLog?.Comment,
                    ReviewerVerdict = reviewLog?.Verdict,
                    CreatedAt = dispute.CreatedAt,
                    ResolvedAt = dispute.ResolvedAt,
                    ProjectId = project?.Id,
                    ProjectName = project?.Name,
                    DataItemUrl = assignment.DataItem?.StorageUrl,
                    AssignmentStatus = assignment.Status,
                    WasOverturned = wasOverturned,
                    ResolutionSummary = resolutionSummary
                });
            }

            return reviewerDisputes.OrderByDescending(d => d.ResolvedAt).ToList();
        }
    }
}
