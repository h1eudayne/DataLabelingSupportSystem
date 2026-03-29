using BLL.Interfaces;
using Core.DTOs.Requests;
using Core.DTOs.Responses;
using DAL.Interfaces;
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

        public ReviewService(
            IAssignmentRepository assignmentRepo,
            IRepository<ReviewLog> reviewLogRepo,
            IRepository<DataItem> dataItemRepo,
            IStatisticService statisticService,
            IProjectRepository projectRepo,
            IUserRepository userRepo,
            IAppNotificationService notification,
            IActivityLogService logService,
            IRepository<UserProjectStat> statsRepo)
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
        }

        private async Task<bool> HasReviewerAlreadyReviewedAsync(int assignmentId, string reviewerId)
        {
            var existingReviews = await _reviewLogRepo.FindAsync(
                rl => rl.AssignmentId == assignmentId && rl.ReviewerId == reviewerId);
            return existingReviews.Any();
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
                .Select(a => a.ReviewLogs?
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
            var allAssignments = await _assignmentRepo.GetAllAsync();

            var reviewerAssignments = allAssignments
                .Where(a => a.ReviewerId == reviewerId ||
                            (a.ReviewLogs != null && a.ReviewLogs.Any(r => r.ReviewerId == reviewerId)))
                .ToList();

            var response = new List<AssignedProjectResponse>();
            var grouped = reviewerAssignments.GroupBy(a => a.ProjectId);

            foreach (var g in grouped)
            {
                var project = await _projectRepo.GetProjectWithDetailsAsync(g.Key);

                response.Add(new AssignedProjectResponse
                {
                    ProjectId = g.Key,
                    ProjectName = project?.Name ?? "Unknown Project",
                    Description = project?.Description ?? "",
                    ThumbnailUrl = project?.DataItems?.FirstOrDefault()?.StorageUrl ?? "",
                    AssignedDate = g.Min(a => a.AssignedDate),
                    Deadline = project?.Deadline ?? DateTime.MinValue,
                    TotalImages = g.Count(),
                    CompletedImages = g.Count(a => a.Status == TaskStatusConstants.Approved || a.Status == TaskStatusConstants.Rejected),
                    Status = g.All(a => a.Status == TaskStatusConstants.Approved) ? "Completed" : "InProgress"
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
                throw new Exception("This task is not ready for review.");
            if (!request.IsApproved && string.IsNullOrWhiteSpace(request.Comment))
            {
                throw new Exception("Rejection requires a clear comment explaining the error.");
            }

            var alreadyReviewed = await HasReviewerAlreadyReviewedAsync(assignment.Id, reviewerId);
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

                if (assignment.ReviewLogs != null && assignment.ReviewLogs.Any())
                {
                    foreach (var reviewLog in assignment.ReviewLogs)
                    {
                        reviewLog.ErrorCategory = null;
                        _reviewLogRepo.Update(reviewLog);
                    }
                }
            }
            else
            {
                assignment.Status = TaskStatusConstants.Rejected;
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

            if (request.IsApproved)
            {
                var existingReviewLogs = await _reviewLogRepo.GetAllAsync();
                var hasBeenReviewedBefore = existingReviewLogs
                    .Any(rl => rl.AssignmentId == assignment.Id);

                if (!hasBeenReviewedBefore)
                {
                    await _statisticService.TrackFirstPassCorrectAsync(
                        assignment.AnnotatorId,
                        reviewerId,
                        assignment.ProjectId);
                }
            }

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

            if (request.IsApproved)
            {
                await _notification.SendNotificationAsync(
                    assignment.AnnotatorId,
                    $"Great job! Your task #{assignment.Id} in project \"{project.Name}\" has been approved by reviewer.",
                    "Success");
            }
            else
            {
                await _notification.SendNotificationAsync(
                    assignment.AnnotatorId,
                    $"Your task #{assignment.Id} in project \"{project.Name}\" has been rejected by reviewer. Reason: {request.Comment}. Penalty: {penaltyScore} point(s).",
                    "Error");
            }

            await NotifyPenaltyTieIfNeededAsync(assignment, project);
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

            var alreadyReviewed = await HasReviewerAlreadyReviewedAsync(assignment.Id, reviewerId);
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
            var allReviewLogs = await _reviewLogRepo.GetAllAsync();

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
                int reviewedCount = allReviewLogs.Count(rl =>
                    tasksForGroup.Any(t => t.AssignmentId == rl.AssignmentId) &&
                    rl.ReviewerId == reviewerId);
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
            var project = await _projectRepo.GetProjectWithDetailsAsync(projectId);
            if (project == null) throw new Exception("Project not found");

            var allAssignments = project.DataItems.SelectMany(d => d.Assignments).ToList();
            var assignments = allAssignments.Where(a => a.ReviewerId == reviewerId || string.IsNullOrEmpty(a.ReviewerId)).ToList();

            var submittedAssignments = assignments.Where(a => a.Status == TaskStatusConstants.Submitted).ToList();
            var grouped = submittedAssignments.GroupBy(a => a.AnnotatorId);

            var allReviewLogs = await _reviewLogRepo.GetAllAsync();
            var reviewerLogs = allReviewLogs.Where(rl => rl.ReviewerId == reviewerId).ToList();

            var allStats = await _statsRepo.GetAllAsync();
            var annotatorBatches = new List<AnnotatorBatchStatus>();

            foreach (var group in grouped)
            {
                var annotatorId = group.Key;
                var annotator = group.First().Annotator;
                var stat = allStats.FirstOrDefault(s => s.UserId == annotatorId && s.ProjectId == projectId);

                var tasksForAnnotator = group.ToList();
                int totalSubmitted = tasksForAnnotator.Count;

                var assignmentIds = tasksForAnnotator.Select(a => a.Id).ToHashSet();
                int approved = reviewerLogs.Count(rl => assignmentIds.Contains(rl.AssignmentId) && (rl.Verdict == "Approved" || rl.Verdict == "Approve"));
                int rejected = reviewerLogs.Count(rl => assignmentIds.Contains(rl.AssignmentId) && (rl.Verdict == "Rejected" || rl.Verdict == "Reject"));
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

        public async Task HandleEscalatedTaskAsync(string managerId, EscalationActionRequest request)
        {
            var assignment = await _assignmentRepo.GetAssignmentWithDetailsAsync(request.AssignmentId);
            if (assignment == null) throw new Exception("Assignment not found");
            if (!assignment.IsEscalated) throw new Exception("This task is not escalated");
            if (assignment.Project?.ManagerId != managerId) throw new UnauthorizedAccessException("Only the project manager can handle escalated tasks");

            var originalAnnotatorId = assignment.AnnotatorId;

            switch (request.Action.ToLower())
            {
                case "approve":
                    assignment.Status = TaskStatusConstants.Approved;
                    assignment.IsEscalated = false;
                    assignment.SubmittedAt = DateTime.UtcNow;

                    var dataItem = await _dataItemRepo.GetByIdAsync(assignment.DataItemId);
                    if (dataItem != null)
                    {
                        dataItem.Status = TaskStatusConstants.Approved;
                        _dataItemRepo.Update(dataItem);
                    }

                    await _notification.SendNotificationAsync(
                        assignment.AnnotatorId,
                        $"Your escalated task #{assignment.Id} has been approved by Manager.",
                        "Success");
                    break;

                case "reject":
                    assignment.Status = TaskStatusConstants.Rejected;
                    assignment.IsEscalated = false;
                    assignment.RejectCount = 0;

                    await _notification.SendNotificationAsync(
                        assignment.AnnotatorId,
                        $"Your escalated task #{assignment.Id} has been rejected by Manager. Reason: {request.Comment ?? "No comment"}",
                        "Error");
                    break;

                case "reassign":
                    if (string.IsNullOrEmpty(request.NewAnnotatorId))
                        throw new Exception("New Annotator ID is required for reassignment action");

                    var newAnnotator = await _userRepo.GetByIdAsync(request.NewAnnotatorId);
                    if (newAnnotator == null) throw new Exception("New annotator not found");
                    if (newAnnotator.Role != UserRoles.Annotator) throw new Exception("Selected user is not an Annotator");
                    if (request.NewAnnotatorId == managerId) throw new Exception("BR-MNG-27: Manager cannot assign tasks to themselves");

                    assignment.AnnotatorId = request.NewAnnotatorId;
                    assignment.Status = TaskStatusConstants.Assigned;
                    assignment.IsEscalated = false;
                    assignment.RejectCount = 0;

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
                    assignment.IsEscalated = false;
                    assignment.AnnotatorId = "";

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

            _assignmentRepo.Update(assignment);
            await _assignmentRepo.SaveChangesAsync();
            await _dataItemRepo.SaveChangesAsync();

            await _logService.LogActionAsync(
                managerId,
                "HandleEscalatedTask",
                "Assignment",
                assignment.Id.ToString(),
                $"Manager performed '{request.Action}' action on escalated task {assignment.Id}.");
        }
    }
}
