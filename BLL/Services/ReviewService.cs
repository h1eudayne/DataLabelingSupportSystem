using BLL.Interfaces;
using BLL.Helpers;
using Core.DTOs.Requests;
using Core.DTOs.Responses;
using Core.Interfaces;
using Core.Constants;
using Core.Entities;
using System.Text.Json;

namespace BLL.Services
{
    public class ReviewService : IReviewService
    {
        private readonly IAssignmentRepository _assignmentRepo;
        private readonly IRepository<ReviewLog> _reviewLogRepo;
        private readonly IRepository<DataItem> _dataItemRepo;
        private readonly IStatisticService _statisticService;
        private readonly IProjectRepository _projectRepo;
        private readonly IUserRepository _userRepo;
        private readonly IActivityLogService _logService;
        private readonly IAppNotificationService _notification;
        private readonly IRepository<UserProjectStat> _statsRepo;
        private readonly IWorkflowEmailService? _workflowEmailService;

        public ReviewService(
            IAssignmentRepository assignmentRepo,
            IRepository<ReviewLog> reviewLogRepo,
            IRepository<DataItem> dataItemRepo,
            IStatisticService statisticService,
            IProjectRepository projectRepo,
            IUserRepository userRepo,
            IAppNotificationService notification,
            IActivityLogService logService,
            IRepository<UserProjectStat> statsRepo,
            IWorkflowEmailService? workflowEmailService = null)
        {
            _assignmentRepo = assignmentRepo;
            _reviewLogRepo = reviewLogRepo;
            _dataItemRepo = dataItemRepo;
            _statisticService = statisticService;
            _projectRepo = projectRepo;
            _userRepo = userRepo;
            _logService = logService;
            _notification = notification;
            _statsRepo = statsRepo;
            _workflowEmailService = workflowEmailService;
        }

        private static IEnumerable<ReviewLog> GetCurrentSubmissionReviewLogs(
            IEnumerable<ReviewLog>? reviewLogs,
            DateTime? submittedAt)
        {
            if (reviewLogs == null)
            {
                return Enumerable.Empty<ReviewLog>();
            }

            if (!submittedAt.HasValue)
            {
                return reviewLogs;
            }

            return reviewLogs.Where(log => log.CreatedAt >= submittedAt.Value);
        }

        private async Task<bool> HasReviewerAlreadyReviewedAsync(Assignment assignment, string reviewerId)
        {
            var existingReviews = await _reviewLogRepo.FindAsync(
                rl => rl.AssignmentId == assignment.Id && rl.ReviewerId == reviewerId);

            return GetCurrentSubmissionReviewLogs(existingReviews, assignment.SubmittedAt).Any();
        }

        private static bool IsApprovedVerdict(string? verdict)
        {
            return string.Equals(verdict, "Approved", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(verdict, "Approve", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRejectedVerdict(string? verdict)
        {
            return string.Equals(verdict, "Rejected", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(verdict, "Reject", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasReviewerReviewedCurrentSubmission(Assignment assignment, string reviewerId)
        {
            return GetCurrentSubmissionReviewLogs(assignment.ReviewLogs, assignment.SubmittedAt)
                .Any(log => string.Equals(log.ReviewerId, reviewerId, StringComparison.OrdinalIgnoreCase));
        }

        private static List<Assignment> FilterPendingAssignmentsForReviewer(IEnumerable<Assignment> assignments, string reviewerId)
        {
            return assignments
                .Where(a =>
                    string.Equals(a.Status, TaskStatusConstants.Submitted, StringComparison.OrdinalIgnoreCase) &&
                    !HasReviewerReviewedCurrentSubmission(a, reviewerId))
                .ToList();
        }

        private async Task<List<Assignment>> GetAssignmentGroupAsync(Assignment assignment)
        {
            var currentAssignment = await _assignmentRepo.GetAssignmentWithDetailsAsync(assignment.Id) ?? assignment;

            if (currentAssignment.DataItemId <= 0)
            {
                return new List<Assignment> { currentAssignment };
            }

            var relatedAssignments = await _assignmentRepo.GetRelatedAssignmentsForDisputeAsync(
                currentAssignment.Id,
                currentAssignment.AnnotatorId,
                currentAssignment.DataItemId) ?? new List<Assignment>();

            return relatedAssignments
                .Append(currentAssignment)
                .GroupBy(a => a.Id)
                .Select(g => g.First())
                .OrderBy(a => a.Id)
                .ToList();
        }

        private static List<(Assignment Assignment, ReviewLog? LatestLog)> GetLatestCycleReviews(IEnumerable<Assignment> assignments)
        {
            return assignments
                .Select(a => (
                    Assignment: a,
                    LatestLog: GetCurrentSubmissionReviewLogs(a.ReviewLogs, a.SubmittedAt)
                        .OrderByDescending(log => log.CreatedAt)
                        .FirstOrDefault()))
                .ToList();
        }

        private static string BuildRejectionSummary(IEnumerable<ReviewLog> reviewLogs)
        {
            var reasons = reviewLogs
                .Select(log => log.Comment?.Trim())
                .Where(comment => !string.IsNullOrWhiteSpace(comment))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();

            if (!reasons.Any())
            {
                return "Please review the latest reviewer notes and update the annotation before resubmitting.";
            }

            return string.Join(" | ", reasons);
        }

        private static string? GetLatestAnnotationData(IEnumerable<Assignment> assignments)
        {
            return assignments
                .SelectMany(assignment => assignment.Annotations ?? Enumerable.Empty<Annotation>())
                .OrderByDescending(annotation => annotation.CreatedAt)
                .FirstOrDefault()
                ?.DataJSON;
        }

        private static List<ReviewerFeedbackResponse> MapReviewerFeedbacks(IEnumerable<(Assignment Assignment, ReviewLog? LatestLog)> latestCycleReviews)
        {
            return latestCycleReviews
                .Where(item => item.LatestLog != null)
                .Select(item => new ReviewerFeedbackResponse
                {
                    ReviewLogId = item.LatestLog!.Id,
                    ReviewerId = item.LatestLog.ReviewerId,
                    ReviewerName = item.Assignment.Reviewer?.FullName ?? item.Assignment.Reviewer?.Email ?? item.LatestLog.ReviewerId,
                    Decision = item.LatestLog.Decision,
                    Verdict = item.LatestLog.Verdict,
                    Comment = item.LatestLog.Comment,
                    ErrorCategories = item.LatestLog.ErrorCategory,
                    ReviewedAt = item.LatestLog.CreatedAt,
                    ScorePenalty = item.LatestLog.ScorePenalty,
                    IsApproved = item.LatestLog.IsApproved,
                    IsAudited = item.LatestLog.IsAudited,
                    AuditResult = item.LatestLog.AuditResult
                })
                .OrderByDescending(item => item.ReviewedAt)
                .ToList();
        }

        private static List<ReviewLog> ExtractLatestReviewLogs(IEnumerable<(Assignment Assignment, ReviewLog? LatestLog)> latestCycleReviews)
        {
            return latestCycleReviews
                .Where(item => item.LatestLog != null)
                .Select(item => item.LatestLog!)
                .OrderByDescending(log => log.CreatedAt)
                .ToList();
        }

        private static string GetEscalationType(IEnumerable<(Assignment Assignment, ReviewLog? LatestLog)> latestCycleReviews)
        {
            var approvedCount = latestCycleReviews.Count(item => item.LatestLog != null && IsApprovedVerdict(item.LatestLog.Verdict));
            var rejectedCount = latestCycleReviews.Count(item => item.LatestLog != null && IsRejectedVerdict(item.LatestLog.Verdict));

            if (approvedCount > 0 && approvedCount == rejectedCount)
            {
                return "PenaltyReview";
            }

            return "RepeatedReject";
        }

        private async Task MarkAssignmentsAsync(IEnumerable<Assignment> assignments, string status, int? rejectCount = null, bool? isEscalated = null)
        {
            foreach (var target in assignments)
            {
                target.Status = status;

                if (rejectCount.HasValue)
                {
                    target.RejectCount = rejectCount.Value;
                }

                if (isEscalated.HasValue)
                {
                    target.IsEscalated = isEscalated.Value;
                }

                _assignmentRepo.Update(target);
            }

            await _assignmentRepo.SaveChangesAsync();
        }

        private async Task<List<User>> GetUsersByIdsAsync(IEnumerable<string?> userIds)
        {
            var users = new List<User>();

            foreach (var userId in userIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var user = await _userRepo.GetByIdAsync(userId!);
                if (user != null)
                {
                    users.Add(user);
                }
            }

            return users;
        }

        private async Task RunReviewSideEffectSafelyAsync(
            string actorUserId,
            string actionType,
            string entityId,
            string description,
            Func<Task> operation)
        {
            try
            {
                await operation();
            }
            catch (Exception ex)
            {
                try
                {
                    await _logService.LogActionAsync(
                        actorUserId,
                        actionType,
                        "Assignment",
                        entityId,
                        $"{description} {ex.Message}");
                }
                catch
                {
                }
            }
        }

        private async Task NotifyManagerIfProjectReadyForCompletionAsync(int projectId)
        {
            var project = await _projectRepo.GetProjectWithDetailsAsync(projectId);
            if (project == null || string.IsNullOrWhiteSpace(project.ManagerId))
            {
                return;
            }

            if (project.Status != ProjectStatusConstants.Active)
            {
                return;
            }

            var allAssignments = project.DataItems.SelectMany(d => d.Assignments).ToList();
            if (!allAssignments.Any())
            {
                return;
            }

            if (allAssignments.All(a => a.Status == TaskStatusConstants.Approved))
            {
                await _notification.SendNotificationAsync(
                    project.ManagerId,
                    $"Project \"{project.Name}\" has all tasks approved and is ready for you to confirm completion.",
                    "ProjectReadyToComplete");
            }
        }

        private async Task NotifyPenaltyTieIfNeededAsync(Assignment assignment, Project project)
        {
            if (assignment.DataItemId <= 0 || string.IsNullOrWhiteSpace(project.ManagerId))
            {
                return;
            }

            var currentAssignment = await _assignmentRepo.GetAssignmentWithDetailsAsync(assignment.Id);
            if (currentAssignment == null)
            {
                return;
            }

            var relatedAssignments = await _assignmentRepo.GetRelatedAssignmentsForDisputeAsync(
                currentAssignment.Id,
                currentAssignment.AnnotatorId,
                currentAssignment.DataItemId);

            var allAssignments = relatedAssignments.Append(currentAssignment).ToList();
            if (allAssignments.Count < 2)
            {
                return;
            }

            var latestLogs = allAssignments
                .Select(a => GetCurrentSubmissionReviewLogs(a.ReviewLogs, a.SubmittedAt)
                    .OrderByDescending(log => log.CreatedAt)
                    .FirstOrDefault())
                .ToList();

            if (latestLogs.Any(log => log == null))
            {
                return;
            }

            int approvedCount = latestLogs.Count(log => IsApprovedVerdict(log!.Verdict));
            int rejectedCount = latestLogs.Count(log => IsRejectedVerdict(log!.Verdict));

            if (approvedCount == 0 || approvedCount != rejectedCount)
            {
                return;
            }

            var annotator = await _userRepo.GetByIdAsync(currentAssignment.AnnotatorId);
            string annotatorName = annotator?.FullName ?? annotator?.Email ?? currentAssignment.AnnotatorId;

            await _notification.SendNotificationAsync(
                project.ManagerId,
                $"Penalty review required in project \"{project.Name}\": task #{currentAssignment.Id} for annotator \"{annotatorName}\" has a tied reviewer result ({approvedCount} approve / {rejectedCount} reject).",
                "PenaltyReview");

            var reviewerIds = latestLogs
                .Select(log => log!.ReviewerId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var reviewerId in reviewerIds)
            {
                await _notification.SendNotificationAsync(
                    reviewerId,
                    $"Penalty review has been triggered in project \"{project.Name}\" for task #{currentAssignment.Id}. Reviewer decisions are tied ({approvedCount} approve / {rejectedCount} reject) and waiting for manager action.",
                    "PenaltyReview");
            }
        }

        public async Task<List<AssignedProjectResponse>> GetReviewerProjectsAsync(string reviewerId)
        {
            var reviewerAssignments = await _assignmentRepo.GetAssignmentsRelevantToReviewerAsync(reviewerId);

            var response = new List<AssignedProjectResponse>();
            var grouped = reviewerAssignments.GroupBy(a => a.ProjectId);

            foreach (var g in grouped)
            {
                var project = await _projectRepo.GetProjectWithDetailsAsync(g.Key);
                var totalProjectItems = project?.DataItems?.Count ?? 0;
                var approvedProjectItems = ProjectWorkflowStatusHelper.CountApprovedDataItems(project);
                var completedImages = g.Count(a => a.Status == TaskStatusConstants.Approved || a.Status == TaskStatusConstants.Rejected);
                var allReviewerItemsResolved = g.Any() && completedImages == g.Count();
                var awaitingManagerConfirmation = allReviewerItemsResolved &&
                    ProjectWorkflowStatusHelper.IsAwaitingManagerConfirmation(project, totalProjectItems, approvedProjectItems);
                var status = string.Equals(project?.Status, ProjectStatusConstants.Completed, StringComparison.OrdinalIgnoreCase)
                    ? ProjectStatusConstants.Completed
                    : awaitingManagerConfirmation
                        ? ProjectStatusConstants.AwaitingManagerConfirmation
                        : ProjectStatusConstants.InProgressDisplay;

                response.Add(new AssignedProjectResponse
                {
                    ProjectId = g.Key,
                    ProjectName = project?.Name ?? "Unknown Project",
                    Description = project?.Description ?? "",
                    ThumbnailUrl = project?.DataItems?.FirstOrDefault()?.StorageUrl ?? "",
                    AssignedDate = g.Min(a => a.AssignedDate),
                    Deadline = project?.Deadline ?? DateTime.MinValue,
                    TotalImages = g.Count(),
                    CompletedImages = completedImages,
                    Status = status,
                    IsAwaitingManagerConfirmation = awaitingManagerConfirmation
                });
            }

            return response;
        }

        public async Task ReviewAssignmentAsync(string reviewerId, ReviewRequest request)
        {
            var assignment = await _assignmentRepo.GetByIdAsync(request.AssignmentId);
            if (assignment == null) throw new Exception("Assignment not found");

            if (reviewerId == assignment.AnnotatorId)
                throw new Exception("BR-REV-10: A reviewer must not review their own annotated tasks");

            var reviewer = await _userRepo.GetByIdAsync(reviewerId);
            if (reviewer == null) throw new Exception("User not found");

            if (reviewer.Role != UserRoles.Reviewer &&
                reviewer.Role != UserRoles.Manager &&
                reviewer.Role != UserRoles.Admin)
            {
                throw new Exception("Permission denied: Only Reviewers or Managers can review tasks.");
            }
            if (string.IsNullOrEmpty(assignment.ReviewerId))
            {
                assignment.ReviewerId = reviewerId;
            }
            else if (assignment.ReviewerId != reviewerId && reviewer.Role == UserRoles.Reviewer)
            {
                throw new Exception("This task is explicitly assigned to another reviewer.");
            }

            if (assignment.Status != TaskStatusConstants.Submitted)
                throw new Exception($"This task is not ready for review. Current status: \"{assignment.Status}\". The annotator must resubmit before you can review again.");
            if (!request.IsApproved && string.IsNullOrWhiteSpace(request.Comment))
            {
                throw new Exception("Rejection requires a clear comment explaining the error.");
            }

            var alreadyReviewed = await HasReviewerAlreadyReviewedAsync(assignment, reviewerId);
            if (alreadyReviewed)
            {
                throw new InvalidOperationException("You have already reviewed this task. Duplicate reviews are not allowed.");
            }

            var project = await _projectRepo.GetByIdAsync(assignment.ProjectId);
            if (project == null) throw new Exception("Project info not found");

            double currentTaskScore = 0;
            int penaltyScore = 0;
            bool isCritical = false;

            if (request.IsApproved)
            {
                currentTaskScore = 100;
            }
            else
            {
                int weight = 0;

                if (project.ChecklistItems != null &&
                    project.ChecklistItems.Any() &&
                    !string.IsNullOrEmpty(request.ErrorCategory))
                {
                    var errorItem = project.ChecklistItems
                        .FirstOrDefault(c => c.Code == request.ErrorCategory);

                    if (errorItem != null)
                    {
                        weight = errorItem.Weight;
                        if (errorItem.IsCritical) isCritical = true;
                    }
                }

                int unit = project.PenaltyUnit > 0 ? project.PenaltyUnit : 10;
                penaltyScore = weight * unit;

                currentTaskScore = Math.Max(0, 100 - penaltyScore);
            }

            await _statisticService.TrackReviewResultAsync(
                assignment.AnnotatorId,
                reviewerId,
                assignment.ProjectId,
                request.IsApproved,
                currentTaskScore,
                isCritical
            );

            var log = new ReviewLog
            {
                AssignmentId = assignment.Id,
                ReviewerId = reviewerId,
                Verdict = request.IsApproved ? "Approved" : "Rejected",
                Comment = request.Comment,
                ErrorCategory = request.IsApproved ? null : request.ErrorCategory,
                ScorePenalty = penaltyScore,
                CreatedAt = DateTime.UtcNow
            };

            assignment.ReviewLogs ??= new List<ReviewLog>();
            assignment.ReviewLogs.Add(log);

            await _reviewLogRepo.AddAsync(log);
            _assignmentRepo.Update(assignment);

            await _assignmentRepo.SaveChangesAsync();

            var actionStr = request.IsApproved ? "approved" : "rejected";
            var actionType = request.IsApproved ? "ApproveTask" : "RejectTask";
            await _logService.LogActionAsync(
                reviewerId,
                actionType,
                "Project",
                assignment.ProjectId.ToString(),
                $"Reviewer {actionStr} task {assignment.Id}."
            );

            var assignmentGroup = await GetAssignmentGroupAsync(assignment);
            var latestCycleReviews = GetLatestCycleReviews(assignmentGroup);
            bool waitingForOtherReviewers = latestCycleReviews.Any(item =>
                item.LatestLog == null &&
                string.Equals(item.Assignment.Status, TaskStatusConstants.Submitted, StringComparison.OrdinalIgnoreCase));

            if (!waitingForOtherReviewers)
            {
                int approvedCount = latestCycleReviews.Count(item => item.LatestLog != null && IsApprovedVerdict(item.LatestLog.Verdict));
                int rejectedCount = latestCycleReviews.Count(item => item.LatestLog != null && IsRejectedVerdict(item.LatestLog.Verdict));
                int currentRejectCount = assignmentGroup.Select(item => item.RejectCount).DefaultIfEmpty(0).Max();
                var latestReviewLogs = ExtractLatestReviewLogs(latestCycleReviews);

                if (approvedCount > rejectedCount)
                {
                    await MarkAssignmentsAsync(assignmentGroup, TaskStatusConstants.Approved, currentRejectCount, false);

                    if (assignment.DataItemId > 0)
                    {
                        var approvedDataItem = await _dataItemRepo.GetByIdAsync(assignment.DataItemId);
                        if (approvedDataItem != null)
                        {
                            approvedDataItem.Status = TaskStatusConstants.Approved;
                            _dataItemRepo.Update(approvedDataItem);
                            await _dataItemRepo.SaveChangesAsync();
                        }
                    }

                    if (currentRejectCount == 0)
                    {
                        await _statisticService.TrackFirstPassCorrectAsync(
                            assignment.AnnotatorId,
                            assignment.ProjectId);
                    }

                    await _notification.SendNotificationAsync(
                        assignment.AnnotatorId,
                        $"Your task #{assignment.Id} in project \"{project.Name}\" has been approved by reviewer consensus ({approvedCount} approve / {rejectedCount} reject).",
                        "Success");
                }
                else if (rejectedCount > approvedCount)
                {
                    int nextRejectCount = currentRejectCount + 1;
                    var rejectionFeedbacks = latestCycleReviews
                        .Where(item => item.LatestLog != null && IsRejectedVerdict(item.LatestLog.Verdict))
                        .Select(item => item.LatestLog!)
                        .ToList();

                    if (nextRejectCount >= 3)
                    {
                        await MarkAssignmentsAsync(assignmentGroup, "Escalated", nextRejectCount, true);

                        if (!string.IsNullOrWhiteSpace(project.ManagerId))
                        {
                            await _notification.SendNotificationAsync(
                                project.ManagerId,
                                $"Task #{assignment.Id} in project \"{project.Name}\" has been escalated after {nextRejectCount} rejected review cycles. Immediate manager action is required.",
                                "Urgent");
                        }

                        await _notification.SendNotificationAsync(
                            assignment.AnnotatorId,
                            $"Your task #{assignment.Id} in project \"{project.Name}\" has been rejected {nextRejectCount} times and is now waiting for manager review. Latest reviewer notes: {BuildRejectionSummary(rejectionFeedbacks)}",
                            "Warning");

                        if (_workflowEmailService != null && !string.IsNullOrWhiteSpace(project.ManagerId))
                        {
                            var manager = await _userRepo.GetByIdAsync(project.ManagerId);
                            var annotator = await _userRepo.GetByIdAsync(assignment.AnnotatorId);
                            var reviewers = await GetUsersByIdsAsync(latestReviewLogs.Select(log => log.ReviewerId));

                            if (manager != null && annotator != null)
                            {
                                await RunReviewSideEffectSafelyAsync(
                                    reviewerId,
                                    "EscalationTriggerEmailError",
                                    assignment.Id.ToString(),
                                    $"Escalation triggered for task {assignment.Id}, but escalation emails could not be delivered.",
                                    () => _workflowEmailService.SendEscalationTriggeredEmailsAsync(
                                        project,
                                        manager,
                                        annotator,
                                        assignment,
                                        reviewers,
                                        latestReviewLogs,
                                        "RepeatedReject",
                                        nextRejectCount));
                            }
                        }
                    }
                    else
                    {
                        await MarkAssignmentsAsync(assignmentGroup, TaskStatusConstants.Rejected, nextRejectCount, false);

                        await _notification.SendNotificationAsync(
                            assignment.AnnotatorId,
                            $"Your task #{assignment.Id} in project \"{project.Name}\" has been rejected by reviewer consensus ({approvedCount} approve / {rejectedCount} reject). Please revise it and resubmit. Notes: {BuildRejectionSummary(rejectionFeedbacks)}",
                            "Error");
                    }
                }
                else if (approvedCount > 0)
                {
                    await MarkAssignmentsAsync(assignmentGroup, "Escalated", currentRejectCount, true);

                    await _notification.SendNotificationAsync(
                        assignment.AnnotatorId,
                        $"Your task #{assignment.Id} in project \"{project.Name}\" has a split reviewer result ({approvedCount} approve / {rejectedCount} reject) and is waiting for manager arbitration.",
                        "Warning");

                    await NotifyPenaltyTieIfNeededAsync(assignment, project);

                    if (_workflowEmailService != null && !string.IsNullOrWhiteSpace(project.ManagerId))
                    {
                        var manager = await _userRepo.GetByIdAsync(project.ManagerId);
                        var annotator = await _userRepo.GetByIdAsync(assignment.AnnotatorId);
                        var reviewers = await GetUsersByIdsAsync(latestReviewLogs.Select(log => log.ReviewerId));

                        if (manager != null && annotator != null)
                        {
                            await RunReviewSideEffectSafelyAsync(
                                reviewerId,
                                "EscalationTriggerEmailError",
                                assignment.Id.ToString(),
                                $"Penalty review triggered for task {assignment.Id}, but escalation emails could not be delivered.",
                                () => _workflowEmailService.SendEscalationTriggeredEmailsAsync(
                                    project,
                                    manager,
                                    annotator,
                                    assignment,
                                    reviewers,
                                    latestReviewLogs,
                                    "PenaltyReview",
                                    currentRejectCount));
                        }
                    }
                }
            }

            await NotifyManagerIfProjectReadyForCompletionAsync(assignment.ProjectId);
        }

        public async Task AuditReviewAsync(string managerId, AuditReviewRequest request)
        {
            var log = await _reviewLogRepo.GetByIdAsync(request.ReviewLogId);
            if (log == null) throw new Exception("Review log not found");
            if (log.IsAudited) throw new Exception("This review has already been audited");

            var assignment = await _assignmentRepo.GetByIdAsync(log.AssignmentId);
            if (assignment == null) throw new Exception("Related assignment not found");

            await _statisticService.TrackAuditResultAsync(log.ReviewerId, assignment.ProjectId, request.IsCorrectDecision);

            log.IsAudited = true;
            log.AuditResult = request.IsCorrectDecision ? "Agree" : "Disagree";

            await _reviewLogRepo.SaveChangesAsync();

            var project = await _projectRepo.GetByIdAsync(assignment.ProjectId);
            string projectName = project?.Name ?? $"Project #{assignment.ProjectId}";

            await _notification.SendNotificationAsync(
                log.ReviewerId,
                request.IsCorrectDecision
                    ? $"Manager audited your review in project \"{projectName}\" and confirmed your evaluation for task #{assignment.Id}."
                    : $"Manager audited your review in project \"{projectName}\" and marked your evaluation for task #{assignment.Id} as failed. Please review the guideline and penalty criteria.",
                request.IsCorrectDecision ? "Info" : "Warning");

            var decisionStr = request.IsCorrectDecision ? "Agreed" : "Disagreed";
            await _logService.LogActionAsync(
                managerId,
                "AuditReview",
                "Project",
                assignment.ProjectId.ToString(),
                $"Manager audited review {log.Id} and {decisionStr.ToLower()} with the decision."
            );
        }

        public async Task<ReviewLog> SubmitReviewAsync(string reviewerId, ReviewRequest request)
        {
            var assignment = await _assignmentRepo.GetAssignmentWithDetailsAsync(request.AssignmentId);
            if (assignment == null) throw new Exception("Assignment not found");

            if (reviewerId == assignment.AnnotatorId)
                throw new Exception("BR-REV-10: A reviewer must not review their own annotated tasks");
            if (assignment.ReviewerId != reviewerId) throw new UnauthorizedAccessException("You are not assigned to review this task");
            if (assignment.Status != TaskStatusConstants.Submitted) throw new Exception("Task is not in a reviewable state");

            var alreadyReviewed = await HasReviewerAlreadyReviewedAsync(assignment, reviewerId);
            if (alreadyReviewed)
            {
                throw new InvalidOperationException("You have already reviewed this task. Duplicate reviews are not allowed.");
            }

            var reviewLog = new ReviewLog
            {
                AssignmentId = request.AssignmentId,
                ReviewerId = reviewerId,
                IsApproved = request.IsApproved,
                Comment = request.Comment,
                ErrorCategory = request.ErrorCategory,
                CreatedAt = DateTime.UtcNow
            };

            if (request.IsApproved)
            {
                assignment.Status = TaskStatusConstants.Approved;

                if (assignment.ReviewLogs != null && assignment.ReviewLogs.Any())
                {
                    foreach (var existingLog in assignment.ReviewLogs)
                    {
                        existingLog.ErrorCategory = null;
                    }
                    _assignmentRepo.Update(assignment);
                }
            }
            else
            {
                assignment.RejectCount += 1;

                if (assignment.RejectCount >= 3)
                {
                    assignment.IsEscalated = true;
                    assignment.Status = "Escalated";

                    var project = await _projectRepo.GetByIdAsync(assignment.ProjectId);
                    if (project != null && !string.IsNullOrEmpty(project.ManagerId))
                    {
                        await _notification.SendNotificationAsync(
                            project.ManagerId,
                            $"Task #{assignment.Id} has been escalated after {assignment.RejectCount} rejections. Immediate action required.",
                            "Urgent");
                    }
                }
                else
                {
                    assignment.Status = TaskStatusConstants.Rejected;
                }
            }

            await _reviewLogRepo.AddAsync(reviewLog);
            _assignmentRepo.Update(assignment);
            await _assignmentRepo.SaveChangesAsync();

            string actionStr = request.IsApproved ? "approved" : "rejected";
            string actionType = request.IsApproved ? "ApproveTask" : "RejectTask";
            await _logService.LogActionAsync(
                reviewerId,
                actionType,
                "Assignment",
                assignment.Id.ToString(),
                $"Reviewer {actionStr} task {assignment.Id}."
            );

            string statusStr = request.IsApproved ? "Approved" : "Rejected";
            await _notification.SendNotificationAsync(
                assignment.AnnotatorId,
                $"Your task (ID: {assignment.Id}) has been {statusStr} by reviewer.",
                "Info");

            return reviewLog;
        }

        public async Task<List<TaskResponse>> GetTasksForReviewAsync(int projectId, string reviewerId)
        {
            var assignments = await _assignmentRepo.GetAssignmentsForReviewerAsync(projectId, reviewerId);
            assignments = FilterPendingAssignmentsForReviewer(assignments, reviewerId);

            var project = await _projectRepo.GetByIdAsync(projectId);
            int samplingRate = 100;
            if (project != null && project.SamplingRate > 0 && project.SamplingRate < 100)
            {
                samplingRate = project.SamplingRate;
                var random = new Random();
                assignments = assignments
                    .Where((a, index) => index % 100 < samplingRate || a.IsEscalated || a.RejectCount > 0)
                    .ToList();
            }

            return assignments.Select(a =>
            {
                var latestAnnotation = a.Annotations?.OrderByDescending(an => an.CreatedAt).FirstOrDefault();
                object? annotationJson = null;
                if (latestAnnotation != null && !string.IsNullOrEmpty(latestAnnotation.DataJSON))
                {
                    try { annotationJson = JsonDocument.Parse(latestAnnotation.DataJSON).RootElement; } catch { }
                }

                return new TaskResponse
                {
                    AssignmentId = a.Id,
                    DataItemId = a.DataItemId,
                    StorageUrl = a.DataItem?.StorageUrl ?? "",
                    ProjectName = a.Project?.Name ?? "",
                    Status = a.Status ?? "",
                    Deadline = a.Project?.Deadline ?? DateTime.MinValue,
                    ReviewerId = a.ReviewerId ?? "",
                    ReviewerName = a.Reviewer?.FullName ?? "",
                    AnnotatorId = a.AnnotatorId,
                    AnnotatorName = a.Annotator?.FullName ?? a.Annotator?.Email ?? "Unknown Annotator",

                    Labels = a.Project?.LabelClasses.Select(l => new LabelResponse
                    {
                        Id = l.Id,
                        Name = l.Name ?? "",
                        Color = l.Color ?? "",
                        GuideLine = l.GuideLine ?? "",
                        IsDefault = l.IsDefault,
                        Checklist = !string.IsNullOrEmpty(l.DefaultChecklist)
                                    ? JsonSerializer.Deserialize<List<string>>(l.DefaultChecklist, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<string>()
                                    : new List<string>()
                    }).ToList() ?? new List<LabelResponse>(),
                    ExistingAnnotations = annotationJson != null ? new List<object> { annotationJson } : new List<object>()
                };
            }).ToList();
        }

        public async Task<ReviewQueueResponse> GetReviewQueueGroupedByAnnotatorAsync(int projectId, string reviewerId)
        {
            var project = await _projectRepo.GetProjectWithDetailsAsync(projectId);
            if (project == null) throw new Exception("Project not found");

            var assignments = await _assignmentRepo.GetAssignmentsForReviewerAsync(projectId, reviewerId);

            var grouped = assignments
                .Where(a => a.Status == TaskStatusConstants.Submitted)
                .GroupBy(a => a.AnnotatorId);

            var annotatorGroups = new List<AnnotatorTaskGroupResponse>();

            foreach (var group in grouped)
            {
                var annotatorId = group.Key;
                var annotator = group.First().Annotator;

                var tasksForGroup = group.Select(a =>
                {
                    var latestAnnotation = a.Annotations?.OrderByDescending(an => an.CreatedAt).FirstOrDefault();
                    object? annotationJson = null;
                    if (latestAnnotation != null && !string.IsNullOrEmpty(latestAnnotation.DataJSON))
                    {
                        try { annotationJson = JsonDocument.Parse(latestAnnotation.DataJSON).RootElement; } catch { }
                    }

                    return new TaskResponse
                    {
                        AssignmentId = a.Id,
                        DataItemId = a.DataItemId,
                        StorageUrl = a.DataItem?.StorageUrl ?? "",
                        ProjectName = a.Project?.Name ?? "",
                        Status = a.Status ?? "",
                        Deadline = a.Project?.Deadline ?? DateTime.MinValue,
                        ReviewerId = a.ReviewerId ?? "",
                        ReviewerName = a.Reviewer?.FullName ?? "",
                        AnnotatorId = a.AnnotatorId,
                        AnnotatorName = a.Annotator?.FullName ?? a.Annotator?.Email ?? "Unknown Annotator",
                        Labels = a.Project?.LabelClasses.Select(l => new LabelResponse
                        {
                            Id = l.Id,
                            Name = l.Name ?? "",
                            Color = l.Color ?? "",
                            GuideLine = l.GuideLine ?? "",
                            IsDefault = l.IsDefault,
                            Checklist = !string.IsNullOrEmpty(l.DefaultChecklist)
                                        ? JsonSerializer.Deserialize<List<string>>(l.DefaultChecklist, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<string>()
                                        : new List<string>()
                        }).ToList() ?? new List<LabelResponse>(),
                        ExistingAnnotations = annotationJson != null ? new List<object> { annotationJson } : new List<object>()
                    };
                }).ToList();

                int totalSubmitted = tasksForGroup.Count;
                int reviewedCount = group.Count(a =>
                    GetCurrentSubmissionReviewLogs(a.ReviewLogs, a.SubmittedAt)
                        .Any(rl => rl.ReviewerId == reviewerId));
                int pendingReviewCount = totalSubmitted - reviewedCount;
                double progressPercentage = totalSubmitted > 0 ? Math.Round((double)reviewedCount / totalSubmitted * 100, 2) : 0;

                annotatorGroups.Add(new AnnotatorTaskGroupResponse
                {
                    AnnotatorId = annotatorId,
                    AnnotatorName = annotator?.FullName ?? annotator?.Email ?? "Unknown",
                    TotalSubmitted = totalSubmitted,
                    ReviewedCount = reviewedCount,
                    PendingReviewCount = pendingReviewCount,
                    ProgressPercentage = progressPercentage,
                    Tasks = tasksForGroup
                });
            }

            var orderedGroups = annotatorGroups.OrderByDescending(g => g.PendingReviewCount).ToList();

            string recommendedAnnotatorId = orderedGroups.FirstOrDefault()?.AnnotatorId ?? string.Empty;
            string recommendedAnnotatorName = orderedGroups.FirstOrDefault()?.AnnotatorName ?? string.Empty;

            return new ReviewQueueResponse
            {
                ProjectId = projectId,
                ProjectName = project.Name,
                AnnotatorGroups = orderedGroups,
                RecommendedAnnotatorId = recommendedAnnotatorId,
                RecommendedAnnotatorName = recommendedAnnotatorName,
                TotalPendingTasks = orderedGroups.Sum(g => g.PendingReviewCount),
                TotalReviewedTasks = orderedGroups.Sum(g => g.ReviewedCount)
            };
        }

        public async Task<BatchCompletionStatusResponse> GetBatchCompletionStatusAsync(int projectId, string reviewerId)
        {
            var project = await _projectRepo.GetProjectWithStatsDataAsync(projectId)
                ?? await _projectRepo.GetProjectWithDetailsAsync(projectId);
            if (project == null) throw new Exception("Project not found");

            var allAssignments = project.DataItems.SelectMany(d => d.Assignments).ToList();
            var assignments = allAssignments.Where(a => a.ReviewerId == reviewerId || string.IsNullOrEmpty(a.ReviewerId)).ToList();

            var submittedAssignments = assignments.Where(a => a.Status == TaskStatusConstants.Submitted).ToList();
            var grouped = submittedAssignments.GroupBy(a => a.AnnotatorId);

            var allStats = await _statsRepo.GetAllAsync();
            var annotatorBatches = new List<AnnotatorBatchStatus>();

            foreach (var group in grouped)
            {
                var annotatorId = group.Key;
                var annotator = group.First().Annotator;
                var stat = allStats.FirstOrDefault(s => s.UserId == annotatorId && s.ProjectId == projectId);

                var tasksForAnnotator = group.ToList();
                int totalSubmitted = tasksForAnnotator.Count;

                int approved = tasksForAnnotator.Count(a =>
                    GetCurrentSubmissionReviewLogs(a.ReviewLogs, a.SubmittedAt)
                        .Any(rl => rl.ReviewerId == reviewerId && IsApprovedVerdict(rl.Verdict)));
                int rejected = tasksForAnnotator.Count(a =>
                    GetCurrentSubmissionReviewLogs(a.ReviewLogs, a.SubmittedAt)
                        .Any(rl => rl.ReviewerId == reviewerId && IsRejectedVerdict(rl.Verdict)));
                int pendingReview = totalSubmitted - approved - rejected;

                bool isComplete = pendingReview == 0;
                double completionPercentage = totalSubmitted > 0 ? Math.Round((double)(approved + rejected) / totalSubmitted * 100, 2) : 0;

                DateTime? lastActivity = tasksForAnnotator
                    .Where(a => a.SubmittedAt.HasValue)
                    .OrderByDescending(a => a.SubmittedAt)
                    .FirstOrDefault()?.SubmittedAt;

                annotatorBatches.Add(new AnnotatorBatchStatus
                {
                    AnnotatorId = annotatorId,
                    AnnotatorName = annotator?.FullName ?? annotator?.Email ?? "Unknown",
                    TotalSubmitted = totalSubmitted,
                    Approved = approved,
                    Rejected = rejected,
                    PendingReview = pendingReview,
                    IsComplete = isComplete,
                    CompletionPercentage = completionPercentage,
                    IsLocked = stat?.IsLocked ?? false,
                    LastActivityAt = lastActivity
                });
            }

            var orderedBatches = annotatorBatches
                .Where(b => !b.IsComplete && !b.IsLocked)
                .OrderByDescending(b => b.PendingReview)
                .Concat(annotatorBatches.Where(b => b.IsComplete || b.IsLocked))
                .ToList();

            string recommendedAnnotatorId = orderedBatches.FirstOrDefault(b => !b.IsComplete && !b.IsLocked)?.AnnotatorId ?? string.Empty;
            string recommendedAnnotatorName = orderedBatches.FirstOrDefault(b => !b.IsComplete && !b.IsLocked)?.AnnotatorName ?? string.Empty;

            int totalAnnotators = annotatorBatches.Count;
            int completedAnnotators = annotatorBatches.Count(b => b.IsComplete);
            bool isProjectComplete = totalAnnotators > 0 && completedAnnotators == totalAnnotators;

            return new BatchCompletionStatusResponse
            {
                ProjectId = projectId,
                ProjectName = project.Name,
                AnnotatorBatches = orderedBatches,
                RecommendedAnnotatorId = recommendedAnnotatorId,
                RecommendedAnnotatorName = recommendedAnnotatorName,
                IsProjectComplete = isProjectComplete,
                TotalAnnotators = totalAnnotators,
                CompletedAnnotators = completedAnnotators
            };
        }

        public async Task<List<EscalatedReviewResponse>> GetEscalatedTasksAsync(int projectId, string managerId)
        {
            var project = await _projectRepo.GetProjectWithStatsDataAsync(projectId);
            if (project == null) throw new Exception("Project not found");
            if (project.ManagerId != managerId) throw new UnauthorizedAccessException("Only the project manager can view escalated tasks");

            var escalatedGroups = project.DataItems
                .SelectMany(dataItem => dataItem.Assignments.Select(assignment => new { DataItem = dataItem, Assignment = assignment }))
                .Where(item => item.Assignment.IsEscalated || string.Equals(item.Assignment.Status, "Escalated", StringComparison.OrdinalIgnoreCase))
                .GroupBy(item => $"{item.Assignment.DataItemId}:{item.Assignment.AnnotatorId}")
                .Select(group =>
                {
                    var assignments = group.Select(item => item.Assignment).ToList();
                    var representative = assignments
                        .OrderByDescending(a => a.SubmittedAt)
                        .ThenBy(a => a.Id)
                        .First();
                    var latestCycleReviews = GetLatestCycleReviews(assignments);

                    return new EscalatedReviewResponse
                    {
                        AssignmentId = representative.Id,
                        ProjectId = project.Id,
                        ProjectName = project.Name,
                        DataItemId = representative.DataItemId,
                        DataItemUrl = group.Select(item => item.DataItem.StorageUrl).FirstOrDefault(),
                        AnnotationData = GetLatestAnnotationData(assignments),
                        AnnotatorId = representative.AnnotatorId,
                        AnnotatorName = representative.Annotator?.FullName ?? representative.Annotator?.Email ?? representative.AnnotatorId,
                        Status = representative.Status,
                        EscalationType = GetEscalationType(latestCycleReviews),
                        ProjectType = project.AllowGeometryTypes,
                        GuidelineVersion = project.GuidelineVersion,
                        RejectCount = assignments.Select(a => a.RejectCount).DefaultIfEmpty(0).Max(),
                        SubmittedAt = assignments.Max(a => a.SubmittedAt),
                        ReviewerFeedbacks = MapReviewerFeedbacks(latestCycleReviews)
                    };
                })
                .OrderByDescending(item => item.EscalationType == "PenaltyReview")
                .ThenByDescending(item => item.RejectCount)
                .ThenByDescending(item => item.SubmittedAt)
                .ToList();

            return escalatedGroups;
        }

        public async Task HandleEscalatedTaskAsync(string managerId, EscalationActionRequest request)
        {
            var assignment = await _assignmentRepo.GetAssignmentWithDetailsAsync(request.AssignmentId);
            if (assignment == null) throw new Exception("Assignment not found");
            if (assignment.Project?.ManagerId != managerId) throw new UnauthorizedAccessException("Only the project manager can handle escalated tasks");

            var assignmentGroup = await GetAssignmentGroupAsync(assignment);
            if (!assignmentGroup.Any(a => a.IsEscalated || string.Equals(a.Status, "Escalated", StringComparison.OrdinalIgnoreCase)))
            {
                throw new Exception("This task is not escalated");
            }

            var latestCycleReviews = GetLatestCycleReviews(assignmentGroup);
            var escalationType = GetEscalationType(latestCycleReviews);
            var reviewerFeedbacks = MapReviewerFeedbacks(latestCycleReviews);
            var latestReviewLogs = ExtractLatestReviewLogs(latestCycleReviews);
            int currentRejectCount = assignmentGroup.Select(a => a.RejectCount).DefaultIfEmpty(0).Max();
            var originalAnnotatorId = assignment.AnnotatorId;
            var normalizedManagerComment = string.IsNullOrWhiteSpace(request.Comment)
                ? null
                : request.Comment.Trim();

            switch (request.Action.ToLower())
            {
                case "approve":
                    await MarkAssignmentsAsync(assignmentGroup, TaskStatusConstants.Approved, currentRejectCount, false);
                    foreach (var target in assignmentGroup)
                    {
                        target.ManagerDecision = "approve";
                        target.ManagerComment = normalizedManagerComment;
                        _assignmentRepo.Update(target);
                    }

                    var dataItem = await _dataItemRepo.GetByIdAsync(assignment.DataItemId);
                    if (dataItem != null)
                    {
                        dataItem.Status = TaskStatusConstants.Approved;
                        _dataItemRepo.Update(dataItem);
                        await _dataItemRepo.SaveChangesAsync();
                    }

                    await _notification.SendNotificationAsync(
                        assignment.AnnotatorId,
                        $"Your escalated task #{assignment.Id} has been approved by Manager.",
                        "Success");

                    foreach (var reviewerFeedback in reviewerFeedbacks)
                    {
                        bool reviewerWasCorrect = string.Equals(reviewerFeedback.Verdict, "Approved", StringComparison.OrdinalIgnoreCase) ||
                                                 string.Equals(reviewerFeedback.Verdict, "Approve", StringComparison.OrdinalIgnoreCase);

                        await _notification.SendNotificationAsync(
                            reviewerFeedback.ReviewerId,
                            reviewerWasCorrect
                                ? $"Manager finalized escalated task #{assignment.Id} as approved. Your review aligned with the final outcome."
                                : $"Manager finalized escalated task #{assignment.Id} as approved. Your review did not align with the final outcome.",
                            reviewerWasCorrect ? "Info" : "Warning");
                    }
                    break;

                case "reject":
                    int resolvedRejectCount = escalationType == "PenaltyReview"
                        ? currentRejectCount + 1
                        : currentRejectCount;

                    await MarkAssignmentsAsync(assignmentGroup, TaskStatusConstants.Rejected, resolvedRejectCount, false);
                    foreach (var target in assignmentGroup)
                    {
                        target.ManagerDecision = "reject";
                        target.ManagerComment = normalizedManagerComment;
                        _assignmentRepo.Update(target);
                    }

                    await _notification.SendNotificationAsync(
                        assignment.AnnotatorId,
                        $"Your escalated task #{assignment.Id} has been rejected by Manager. Reason: {request.Comment ?? "No comment"}",
                        "Error");

                    foreach (var reviewerFeedback in reviewerFeedbacks)
                    {
                        bool reviewerWasCorrect = string.Equals(reviewerFeedback.Verdict, "Rejected", StringComparison.OrdinalIgnoreCase) ||
                                                 string.Equals(reviewerFeedback.Verdict, "Reject", StringComparison.OrdinalIgnoreCase);

                        await _notification.SendNotificationAsync(
                            reviewerFeedback.ReviewerId,
                            reviewerWasCorrect
                                ? $"Manager finalized escalated task #{assignment.Id} as rejected. Your review aligned with the final outcome."
                                : $"Manager finalized escalated task #{assignment.Id} as rejected. Your review did not align with the final outcome.",
                            reviewerWasCorrect ? "Info" : "Warning");
                    }
                    break;

                case "reassign":
                    if (string.IsNullOrEmpty(request.NewAnnotatorId))
                        throw new Exception("New Annotator ID is required for reassignment action");

                    var newAnnotator = await _userRepo.GetByIdAsync(request.NewAnnotatorId);
                    if (newAnnotator == null) throw new Exception("New annotator not found");
                    if (newAnnotator.Role != UserRoles.Annotator) throw new Exception("Selected user is not an Annotator");
                    if (request.NewAnnotatorId == managerId) throw new Exception("BR-MNG-27: Manager cannot assign tasks to themselves");

                    foreach (var target in assignmentGroup)
                    {
                        target.AnnotatorId = request.NewAnnotatorId;
                        target.Status = TaskStatusConstants.Assigned;
                        target.IsEscalated = false;
                        target.RejectCount = 0;
                        target.ManagerDecision = null;
                        target.ManagerComment = null;
                        _assignmentRepo.Update(target);
                    }

                    await _notification.SendNotificationAsync(
                        originalAnnotatorId,
                        $"Task #{assignment.Id} has been reassigned to another annotator due to escalation.",
                        "Warning");

                    await _notification.SendNotificationAsync(
                        request.NewAnnotatorId,
                        $"Task #{assignment.Id} has been assigned to you after escalation review.",
                        "Info");
                    break;

                case "lock":
                    foreach (var target in assignmentGroup)
                    {
                        target.IsEscalated = false;
                        target.AnnotatorId = "";
                        target.ManagerDecision = null;
                        target.ManagerComment = null;
                        _assignmentRepo.Update(target);
                    }

                    var annotatorStat = await _statsRepo.FindAsync(s => s.UserId == originalAnnotatorId && s.ProjectId == assignment.ProjectId);
                    foreach (var stat in annotatorStat)
                    {
                        stat.IsLocked = true;
                        _statsRepo.Update(stat);
                    }

                    await _notification.SendNotificationAsync(
                        originalAnnotatorId,
                        $"Your access to task #{assignment.Id} has been locked due to repeated rejections.",
                        "Warning");
                    break;

                default:
                    throw new Exception("Invalid action. Valid actions are: Approve, Reject, Reassign, Lock");
            }

            await _assignmentRepo.SaveChangesAsync();

            await _logService.LogActionAsync(
                managerId,
                "HandleEscalatedTask",
                "Assignment",
                assignment.Id.ToString(),
                $"Manager performed '{request.Action}' action on escalated task {assignment.Id}.");

            if (_workflowEmailService != null && assignment.Project != null)
            {
                var reviewers = await GetUsersByIdsAsync(reviewerFeedbacks.Select(feedback => feedback.ReviewerId));
                var originalAnnotator = string.IsNullOrWhiteSpace(originalAnnotatorId)
                    ? null
                    : await _userRepo.GetByIdAsync(originalAnnotatorId);
                var manager = await _userRepo.GetByIdAsync(managerId) ?? new User
                {
                    Id = managerId,
                    FullName = "Project Manager",
                    Email = string.Empty,
                    Role = UserRoles.Manager
                };
                User? newAnnotator = null;

                if (!string.IsNullOrWhiteSpace(request.NewAnnotatorId))
                {
                    newAnnotator = await _userRepo.GetByIdAsync(request.NewAnnotatorId);
                }

                await RunReviewSideEffectSafelyAsync(
                    managerId,
                    "EscalationResolutionEmailError",
                    assignment.Id.ToString(),
                    $"Escalation resolved for task {assignment.Id}, but resolution emails could not be delivered.",
                    () => _workflowEmailService.SendEscalationResolvedEmailsAsync(
                        assignment.Project,
                        manager,
                        assignment,
                        originalAnnotator,
                        newAnnotator,
                        reviewers,
                        latestReviewLogs,
                        request.Action,
                        normalizedManagerComment,
                        escalationType,
                        assignmentGroup.Select(a => a.RejectCount).DefaultIfEmpty(0).Max()));
            }
        }
    }
}

