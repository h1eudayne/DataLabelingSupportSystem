using BLL.Interfaces;
using Core.DTOs.Requests;
using Core.DTOs.Responses;
using DAL.Interfaces;
using Core.Constants;
using Core.Entities;
using System.Text.Json;

namespace BLL.Services
{
    public class TaskService : ITaskService
    {
        private readonly IAssignmentRepository _assignmentRepo;
        private readonly IRepository<DataItem> _dataItemRepo;
        private readonly IRepository<Annotation> _annotationRepo;
        private readonly IStatisticService _statisticService;
        private readonly IUserRepository _userRepo;
        private readonly IProjectRepository _projectRepo;
        private readonly IActivityLogService _logService;
        private readonly IAppNotificationService _notification;
        private readonly IWorkflowEmailService _workflowEmailService;

        private const string GUIDELINE_REFERENCE_COMMENT = "Decision based on official project guidelines";

        public TaskService(
            IAssignmentRepository assignmentRepo,
            IRepository<DataItem> dataItemRepo,
            IRepository<Annotation> annotationRepo,
            IStatisticService statisticService,
            IUserRepository userRepo,
            IProjectRepository projectRepo,
            IAppNotificationService notification,
            IActivityLogService logService,
            IWorkflowEmailService workflowEmailService)
        {
            _assignmentRepo = assignmentRepo;
            _dataItemRepo = dataItemRepo;
            _annotationRepo = annotationRepo;
            _statisticService = statisticService;
            _userRepo = userRepo;
            _projectRepo = projectRepo;
            _logService = logService;
            _notification = notification;
            _workflowEmailService = workflowEmailService;
        }

        public async Task AssignTeamAsync(string managerId, AssignTeamRequest request)
        {
            var annotatorIds = (request.AnnotatorIds ?? new List<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var reviewerIds = (request.ReviewerIds ?? new List<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!annotatorIds.Any())
                throw new InvalidOperationException("At least one annotator must be selected.");

            var project = await _projectRepo.GetByIdAsync(request.ProjectId);
            if (project == null)
                throw new Exception("Project not found.");

            if (project.ManagerId != managerId)
                throw new UnauthorizedAccessException("You are not the manager of this project.");

            if (project.Status == "Completed" || project.Status == "Archived")
                throw new InvalidOperationException("BR-MNG-20: Tasks cannot be assigned in Completed or Archived projects");

            if (annotatorIds.Contains(managerId, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("BR-MNG-27: Manager cannot assign tasks to themselves");

            if (reviewerIds.Contains(managerId, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("BR-MNG-27: Manager cannot assign tasks to themselves");

            var allUsers = await _userRepo.GetAllAsync();

            var validAnnotators = allUsers
                .Where(u => annotatorIds.Contains(u.Id, StringComparer.OrdinalIgnoreCase) &&
                            u.Role == UserRoles.Annotator)
                .ToList();

            if (validAnnotators.Count != annotatorIds.Count)
                throw new Exception("One or more Annotator IDs are invalid or lack the required role.");

            var validReviewers = new List<User>();
            if (reviewerIds.Any())
            {
                validReviewers = allUsers
                    .Where(u => reviewerIds.Contains(u.Id, StringComparer.OrdinalIgnoreCase) &&
                                u.Role == UserRoles.Reviewer)
                    .ToList();

                if (validReviewers.Count != reviewerIds.Count)
                    throw new Exception("One or more Reviewer IDs are invalid or lack the required role.");
            }

            var dataItems = await _assignmentRepo.GetUnassignedDataItemsAsync(request.ProjectId, request.TotalQuantity);
            if (!dataItems.Any())
                throw new Exception("Not enough available data items in this project.");

            int totalAnn = validAnnotators.Count;
            int totalRev = validReviewers.Count;
            int totalItems = dataItems.Count;
            int baseAssignments = totalItems * totalAnn;
            int totalRecords = totalRev > 0 ? baseAssignments * totalRev : baseAssignments;

            var newAssignments = new List<Assignment>();

            foreach (var item in dataItems)
            {
                item.Status = TaskStatusConstants.Assigned;
                _dataItemRepo.Update(item);
            }

            foreach (var item in dataItems)
            {
                foreach (var annotator in validAnnotators)
                {
                    if (totalRev > 0)
                    {
                        foreach (var reviewer in validReviewers)
                        {
                            var assignment = new Assignment
                            {
                                ProjectId = request.ProjectId,
                                DataItemId = item.Id,
                                AnnotatorId = annotator.Id,
                                ReviewerId = reviewer.Id,
                                Status = TaskStatusConstants.Assigned,
                                AssignedDate = DateTime.UtcNow
                            };
                            newAssignments.Add(assignment);
                        }
                    }
                    else
                    {
                        var assignment = new Assignment
                        {
                            ProjectId = request.ProjectId,
                            DataItemId = item.Id,
                            AnnotatorId = annotator.Id,
                            ReviewerId = null,
                            Status = TaskStatusConstants.Assigned,
                            AssignedDate = DateTime.UtcNow
                        };
                        newAssignments.Add(assignment);
                    }
                }
            }

            foreach (var assignment in newAssignments)
            {
                await _assignmentRepo.AddAsync(assignment);
            }

            await _assignmentRepo.SaveChangesAsync();
            await _dataItemRepo.SaveChangesAsync();

            await _logService.LogActionAsync(
                managerId,
                "AssignTeam",
                "Project",
                project.Id.ToString(),
                $"Manager assigned {totalItems} items to {totalAnn} annotators. " +
                $"Each annotator received {totalItems} items. " +
                $"Base assignments: {baseAssignments}. " +
                $"With {totalRev} reviewers, total records in DB: {totalRecords}. " +
                $"Each reviewer reviews {baseAssignments} assignments (all annotators' work on all items)."
            );

            var manager = await _userRepo.GetByIdAsync(managerId) ?? new User
            {
                Id = managerId,
                FullName = "Project Manager",
                Email = string.Empty,
                Role = UserRoles.Manager
            };

            foreach (var annotator in validAnnotators)
            {
                await _statisticService.TrackNewAssignmentAsync(annotator.Id, request.ProjectId, totalItems);
                await _notification.SendNotificationAsync(
                    annotator.Id,
                    $"Manager has assigned you {totalItems} tasks in project {project.Name}! " +
                    $"(Each of your tasks will be reviewed by {totalRev} reviewers.)",
                    "Success");

                await _workflowEmailService.SendAnnotatorAssignmentEmailAsync(
                    project,
                    manager,
                    annotator,
                    totalItems,
                    totalRev);
            }

            if (totalRev > 0)
            {
                foreach (var reviewer in validReviewers)
                {
                    await _notification.SendNotificationAsync(
                        reviewer.Id,
                        $"You have been assigned to review ALL {baseAssignments} tasks " +
                        $"(from {totalAnn} annotators, {totalItems} items each) in project {project.Name}!",
                        "Info");

                    await _workflowEmailService.SendReviewerAssignmentEmailAsync(
                        project,
                        manager,
                        reviewer,
                        baseAssignments,
                        totalAnn);
                }
            }
        }

        private int? ExtractClassIdFromJSON(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("classId", out var prop) || root.TryGetProperty("ClassId", out prop))
                    {
                        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out int id))
                            return id;

                        if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out int parsedId))
                            return parsedId;
                    }
                }
                else if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
                {
                    foreach (var element in root.EnumerateArray())
                    {
                        if (element.ValueKind == JsonValueKind.Object)
                        {
                            if (element.TryGetProperty("classId", out var prop) || element.TryGetProperty("ClassId", out prop))
                            {
                                if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out int id))
                                    return id;

                                if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out int parsedId))
                                    return parsedId;
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static int GetAnnotatorStatusPriority(string? status)
        {
            return status switch
            {
                TaskStatusConstants.Rejected => 0,
                "Escalated" => 1,
                TaskStatusConstants.InProgress => 2,
                TaskStatusConstants.Assigned => 3,
                TaskStatusConstants.Submitted => 4,
                TaskStatusConstants.Approved => 5,
                _ => 6
            };
        }

        private static string GetAggregatedAnnotatorStatus(IEnumerable<Assignment> assignments)
        {
            var groupedAssignments = assignments.ToList();

            if (!groupedAssignments.Any())
                return TaskStatusConstants.Assigned;

            if (groupedAssignments.Any(a => string.Equals(a.Status, TaskStatusConstants.Rejected, StringComparison.OrdinalIgnoreCase)))
                return TaskStatusConstants.Rejected;

            if (groupedAssignments.Any(a => string.Equals(a.Status, "Escalated", StringComparison.OrdinalIgnoreCase)))
                return "Escalated";

            if (groupedAssignments.Any(a => string.Equals(a.Status, TaskStatusConstants.InProgress, StringComparison.OrdinalIgnoreCase)))
                return TaskStatusConstants.InProgress;

            if (groupedAssignments.All(a => string.Equals(a.Status, TaskStatusConstants.Approved, StringComparison.OrdinalIgnoreCase)))
                return TaskStatusConstants.Approved;

            if (groupedAssignments.Any(a =>
                    string.Equals(a.Status, TaskStatusConstants.Submitted, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a.Status, TaskStatusConstants.Approved, StringComparison.OrdinalIgnoreCase)))
            {
                return TaskStatusConstants.Submitted;
            }

            return TaskStatusConstants.Assigned;
        }

        private static Assignment SelectRepresentativeAnnotatorAssignment(IEnumerable<Assignment> assignments)
        {
            return assignments
                .OrderBy(a => GetAnnotatorStatusPriority(a.Status))
                .ThenBy(a => a.Id)
                .First();
        }

        private static Annotation? GetLatestAnnotation(IEnumerable<Assignment> assignments)
        {
            return assignments
                .SelectMany(a => a.Annotations ?? Enumerable.Empty<Annotation>())
                .OrderByDescending(a => a.CreatedAt)
                .FirstOrDefault();
        }

        private static ReviewLog? GetLatestReviewLog(IEnumerable<Assignment> assignments)
        {
            return assignments
                .SelectMany(a => a.ReviewLogs ?? Enumerable.Empty<ReviewLog>())
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefault();
        }

        private static DateTime CalculateEffectiveDeadline(Assignment assignment)
        {
            var taskDeadline = assignment.AssignedDate.AddHours(assignment.Project?.MaxTaskDurationHours ?? 24);
            var projectDeadline = assignment.Project?.Deadline ?? DateTime.UtcNow;
            return taskDeadline < projectDeadline ? taskDeadline : projectDeadline;
        }

        private static string BuildAssignmentGroupKey(Assignment assignment)
        {
            return $"{assignment.ProjectId}:{assignment.DataItemId}:{assignment.AnnotatorId}";
        }

        private static bool HasBeenReviewed(Assignment assignment)
        {
            return (assignment.ReviewLogs?.Any() ?? false) ||
                   string.Equals(assignment.Status, TaskStatusConstants.Approved, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(assignment.Status, TaskStatusConstants.Rejected, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(assignment.Status, "Escalated", StringComparison.OrdinalIgnoreCase);
        }

        private static bool NeedsAnnotationClone(Assignment assignment, string dataJson, int? classId)
        {
            var latestAnnotation = assignment.Annotations?
                .OrderByDescending(a => a.CreatedAt)
                .FirstOrDefault();

            return latestAnnotation == null ||
                   !string.Equals(latestAnnotation.DataJSON, dataJson, StringComparison.Ordinal) ||
                   latestAnnotation.ClassId != classId;
        }

        private async Task<List<Assignment>> GetAssignmentGroupAsync(Assignment assignment)
        {
            var assignments = await _assignmentRepo.GetAssignmentsByAnnotatorAsync(assignment.AnnotatorId, assignment.ProjectId);

            return assignments
                .Where(a => a.DataItemId == assignment.DataItemId)
                .OrderBy(a => a.Id)
                .ToList();
        }

        public async Task<List<AssignmentResponse>> GetTasksByBucketAsync(int projectId, int bucketId, string userId)
        {
            var existingAssignments = await _assignmentRepo.GetAssignmentsByBucketAsync(projectId, bucketId, userId);
            if (existingAssignments.Any())
            {
            return existingAssignments.Select(a => new AssignmentResponse
            {
                Id = a.Id,
                DataItemId = a.DataItemId,
                DataItemUrl = a.DataItem?.StorageUrl ?? "",
                Status = a.Status,
                AssignedDate = a.AssignedDate,
                AnnotationData = a.Annotations?
                    .OrderByDescending(an => an.CreatedAt)
                    .FirstOrDefault()
                    ?.DataJSON ?? ""
            }).ToList();
            }

            var dataItems = await _projectRepo.GetDataItemsByBucketIdAsync(projectId, bucketId);

            if (!dataItems.Any())
                return new List<AssignmentResponse>();

            var newAssignments = new List<Assignment>();
            foreach (var item in dataItems)
            {
                newAssignments.Add(new Assignment
                {
                    ProjectId = projectId,
                    DataItemId = item.Id,
                    AnnotatorId = userId,
                    ReviewerId = null,
                    Status = TaskStatusConstants.Assigned,
                    AssignedDate = DateTime.UtcNow
                });
            }

            foreach (var assign in newAssignments)
            {
                await _assignmentRepo.AddAsync(assign);
            }
            await _assignmentRepo.SaveChangesAsync();

            return newAssignments.Select(a => new AssignmentResponse
            {
                Id = a.Id,
                DataItemId = a.DataItemId,
                DataItemUrl = dataItems.First(d => d.Id == a.DataItemId).StorageUrl,
                Status = a.Status,
                AssignedDate = a.AssignedDate,
                AnnotationData = ""
            }).ToList();
        }

        public async Task AssignTasksToAnnotatorAsync(AssignTaskRequest request, string managerId)
        {
            var project = await _projectRepo.GetByIdAsync(request.ProjectId);
            if (project == null)
                throw new Exception("Project not found");
            if (project.ManagerId != managerId)
                throw new UnauthorizedAccessException("You are not the manager of this project.");

            if (project.Status == "Completed" || project.Status == "Archived")
                throw new InvalidOperationException("BR-MNG-20: Tasks cannot be assigned in Completed or Archived projects");

            if (request.AnnotatorId == managerId)
                throw new InvalidOperationException("BR-MNG-27: Manager cannot assign tasks to themselves");

            if (!string.IsNullOrEmpty(request.ReviewerId) && request.ReviewerId == managerId)
                throw new InvalidOperationException("BR-MNG-27: Manager cannot assign tasks to themselves");

            var dataItems = await _assignmentRepo
                .GetUnassignedDataItemsAsync(request.ProjectId, int.MaxValue);

            if (!dataItems.Any())
                throw new Exception("No available data items in this project to assign.");

            var annotator = await _userRepo.GetByIdAsync(request.AnnotatorId);
            if (annotator == null)
                throw new Exception("Annotator not found");
            if (annotator.Role != UserRoles.Annotator)
                throw new Exception("Selected user is not an Annotator");

            if (string.IsNullOrWhiteSpace(request.ReviewerId))
            {
                request.ReviewerId = null;
            }
            else
            {
                var reviewer = await _userRepo.GetByIdAsync(request.ReviewerId);
                if (reviewer == null)
                    throw new Exception("Reviewer not found");
                if (reviewer.Role != UserRoles.Reviewer)
                    throw new Exception("Selected user is not a Reviewer");
            }

            foreach (var item in dataItems)
            {
                var assignment = new Assignment
                {
                    ProjectId = request.ProjectId,
                    DataItemId = item.Id,
                    AnnotatorId = request.AnnotatorId,
                    ReviewerId = request.ReviewerId,
                    Status = TaskStatusConstants.Assigned,
                    AssignedDate = DateTime.UtcNow
                };

                await _assignmentRepo.AddAsync(assignment);
                item.Status = TaskStatusConstants.Assigned;
                _dataItemRepo.Update(item);
            }

            await _assignmentRepo.SaveChangesAsync();
            await _dataItemRepo.SaveChangesAsync();

            await _statisticService.TrackNewAssignmentAsync(
                request.AnnotatorId,
                request.ProjectId,
                dataItems.Count
            );

            var manager = await _userRepo.GetByIdAsync(managerId) ?? new User
            {
                Id = managerId,
                FullName = "Project Manager",
                Email = string.Empty,
                Role = UserRoles.Manager
            };

            await _notification.SendNotificationAsync(
                request.AnnotatorId,
                $"Manager has assigned you {dataItems.Count} new tasks in the project!",
                "Success");

            await _workflowEmailService.SendAnnotatorAssignmentEmailAsync(
                project,
                manager,
                annotator,
                dataItems.Count,
                string.IsNullOrEmpty(request.ReviewerId) ? 0 : 1);

            if (!string.IsNullOrEmpty(request.ReviewerId))
            {
                var reviewer = await _userRepo.GetByIdAsync(request.ReviewerId);

                await _notification.SendNotificationAsync(
                    request.ReviewerId,
                    $"You have been assigned as a Reviewer for {dataItems.Count} new tasks!",
                    "Info");

                if (reviewer != null)
                {
                    await _workflowEmailService.SendReviewerAssignmentEmailAsync(
                        project,
                        manager,
                        reviewer,
                        dataItems.Count,
                        1);
                }
            }
        }

        public async Task<AssignmentResponse> GetAssignmentByIdAsync(int assignmentId, string userId)
        {
            var assignment = await _assignmentRepo.GetAssignmentWithDetailsAsync(assignmentId);

            if (assignment == null)
                throw new KeyNotFoundException("Task not found");

            if (assignment.AnnotatorId != userId)
                throw new UnauthorizedAccessException("Unauthorized access to this task");

            var taskDeadline = assignment.AssignedDate.AddHours(assignment.Project?.MaxTaskDurationHours ?? 24);

            var effectiveDeadline = taskDeadline < (assignment.Project?.Deadline ?? DateTime.UtcNow)
                ? taskDeadline
                : (assignment.Project?.Deadline ?? DateTime.UtcNow);

            return new AssignmentResponse
            {
                Id = assignment.Id,
                DataItemId = assignment.DataItemId,
                DataItemUrl = assignment.DataItem?.StorageUrl ?? "",
                Status = assignment.Status ?? "",
                AnnotationData = assignment.Annotations?
                    .OrderByDescending(an => an.CreatedAt)
                    .FirstOrDefault()
                    ?.DataJSON ?? "",
                AssignedDate = assignment.AssignedDate,
                Deadline = effectiveDeadline,
                RejectionReason = assignment.Status == TaskStatusConstants.Rejected
                    ? (assignment.ReviewLogs?.OrderByDescending(r => r.CreatedAt).FirstOrDefault()?.Comment ?? "")
                    : "",
                ErrorCategory = assignment.Status == TaskStatusConstants.Rejected
                    ? (assignment.ReviewLogs?.OrderByDescending(r => r.CreatedAt).FirstOrDefault()?.ErrorCategory ?? "")
                    : ""
            };
        }

        public async Task<AnnotatorStatsResponse> GetAnnotatorStatsAsync(string annotatorId)
        {
            return await _assignmentRepo.GetAnnotatorStatsAsync(annotatorId);
        }

        public async Task<List<AssignedProjectResponse>> GetAssignedProjectsAsync(string annotatorId)
        {
            var allAssignments = await _assignmentRepo.GetAssignmentsByAnnotatorAsync(annotatorId);

            return allAssignments
                .GroupBy(a => a.ProjectId)
                .Select(g => new AssignedProjectResponse
                {
                    ProjectId = g.Key,
                    ProjectName = g.First().Project?.Name ?? "Unknown Project",
                    Description = g.First().Project?.Description ?? "",
                    ThumbnailUrl = g.First().DataItem?.StorageUrl ?? "",
                    AssignedDate = g.Min(a => a.AssignedDate),
                    Deadline = g.First().Project?.Deadline ?? DateTime.UtcNow,
                    TotalImages = g.Select(a => a.DataItemId).Distinct().Count(),
                    CompletedImages = g.GroupBy(a => a.DataItemId)
                        .Count(imageGroup => string.Equals(
                            GetAggregatedAnnotatorStatus(imageGroup),
                            TaskStatusConstants.Approved,
                            StringComparison.OrdinalIgnoreCase)),
                    Status = g.GroupBy(a => a.DataItemId)
                        .All(imageGroup => string.Equals(
                            GetAggregatedAnnotatorStatus(imageGroup),
                            TaskStatusConstants.Approved,
                            StringComparison.OrdinalIgnoreCase))
                        ? "Completed"
                        : g.GroupBy(a => a.DataItemId)
                            .Any(imageGroup => !string.Equals(
                                GetAggregatedAnnotatorStatus(imageGroup),
                                TaskStatusConstants.Assigned,
                                StringComparison.OrdinalIgnoreCase))
                            ? "InProgress"
                            : "Assigned"
                })
                .ToList();
        }

        public async Task<List<AssignmentResponse>> GetTaskImagesAsync(int projectId, string annotatorId)
        {
            var assignments = await _assignmentRepo.GetAssignmentsByAnnotatorAsync(annotatorId, projectId);

            return assignments
                .GroupBy(a => a.DataItemId)
                .Select(group =>
                {
                    var groupedAssignments = group.ToList();
                    var representative = SelectRepresentativeAnnotatorAssignment(groupedAssignments);
                    var aggregatedStatus = GetAggregatedAnnotatorStatus(groupedAssignments);
                    var latestAnnotation = GetLatestAnnotation(groupedAssignments);
                    var latestReviewLog = GetLatestReviewLog(groupedAssignments);

                    return new AssignmentResponse
                    {
                        Id = representative.Id,
                        DataItemId = representative.DataItemId,
                        DataItemUrl = representative.DataItem?.StorageUrl ?? "",
                        Status = aggregatedStatus,
                        AnnotationData = latestAnnotation?.DataJSON ?? "",
                        AssignedDate = groupedAssignments.Min(a => a.AssignedDate),
                        Deadline = CalculateEffectiveDeadline(representative),
                        RejectionReason = string.Equals(aggregatedStatus, TaskStatusConstants.Rejected, StringComparison.OrdinalIgnoreCase)
                            ? (latestReviewLog?.Comment ?? "")
                            : "",
                        ErrorCategory = string.Equals(aggregatedStatus, TaskStatusConstants.Rejected, StringComparison.OrdinalIgnoreCase)
                            ? (latestReviewLog?.ErrorCategory ?? "")
                            : ""
                    };
                })
                .OrderBy(a => a.DataItemId)
                .ToList();
        }

        public async Task<AssignmentResponse> JumpToDataItemAsync(int projectId, int dataItemId, string userId)
        {
            var assignments = await _assignmentRepo.GetAllAsync();

            var target = assignments.FirstOrDefault(a =>
                a.ProjectId == projectId &&
                a.DataItemId == dataItemId &&
                a.AnnotatorId == userId);

            if (target == null)
                throw new Exception("You are not assigned to this image in this project.");

            return await GetAssignmentByIdAsync(target.Id, userId);
        }

        public async Task SaveDraftAsync(string userId, SubmitAnnotationRequest request)
        {
            if (string.IsNullOrEmpty(request.DataJSON) || request.DataJSON == "[]")
                return;

            var assignment = await _assignmentRepo.GetAssignmentWithDetailsAsync(request.AssignmentId);

            if (assignment == null)
                throw new KeyNotFoundException("Task not found");

            if (assignment.AnnotatorId != userId)
                throw new UnauthorizedAccessException("Unauthorized");

            if (assignment.Status == TaskStatusConstants.Approved)
                throw new InvalidOperationException("BR-MNG-08: Approved data items cannot be reassigned");

            var latestAnnotation = assignment.Annotations?
                .OrderByDescending(a => a.CreatedAt)
                .FirstOrDefault();

            bool shouldCreateNew = assignment.Status == TaskStatusConstants.Rejected;

            if (latestAnnotation != null && !shouldCreateNew)
            {
                latestAnnotation.DataJSON = request.DataJSON;
                latestAnnotation.CreatedAt = DateTime.UtcNow;
                latestAnnotation.ClassId = request.ClassId;
                _annotationRepo.Update(latestAnnotation);
            }
            else
            {
                var annotation = new Annotation
                {
                    AssignmentId = assignment.Id,
                    DataJSON = request.DataJSON,
                    CreatedAt = DateTime.UtcNow,
                    ClassId = request.ClassId
                };
                await _annotationRepo.AddAsync(annotation);
            }

            if (assignment.Status == TaskStatusConstants.New ||
                assignment.Status == TaskStatusConstants.Assigned ||
                assignment.Status == TaskStatusConstants.Rejected)
            {
                assignment.Status = TaskStatusConstants.InProgress;
                _assignmentRepo.Update(assignment);
            }

            await _logService.LogActionAsync(
                userId,
                "SaveDraft",
                "Assignment",
                assignment.Id.ToString(),
                $"Annotator saved draft for Task {assignment.Id}."
            );

            await _annotationRepo.SaveChangesAsync();
            await _assignmentRepo.SaveChangesAsync();
        }

        public async Task SubmitTaskAsync(string userId, SubmitAnnotationRequest request)
        {
            var assignment = await _assignmentRepo.GetAssignmentWithDetailsAsync(request.AssignmentId);

            if (assignment == null)
                throw new KeyNotFoundException("Task not found");

            if (assignment.AnnotatorId != userId)
                throw new UnauthorizedAccessException("Unauthorized");

            if (assignment.Status == TaskStatusConstants.Submitted)
                throw new InvalidOperationException("Task has already been submitted. Cannot submit again.");

            if (assignment.Status == TaskStatusConstants.Approved)
                throw new InvalidOperationException("BR-MNG-08: Approved data items cannot be reassigned");

            if (string.IsNullOrEmpty(request.DataJSON) || request.DataJSON == "[]")
                throw new InvalidOperationException("Annotation data is empty. Please save a draft before submitting.");

            var assignmentGroup = await GetAssignmentGroupAsync(assignment);
            var syncTargets = assignmentGroup
                .Where(a => !HasBeenReviewed(a))
                .ToList();

            if (!syncTargets.Any())
            {
                syncTargets.Add(assignment);
            }

            foreach (var target in syncTargets)
            {
                await _annotationRepo.AddAsync(new Annotation
                {
                    AssignmentId = target.Id,
                    DataJSON = request.DataJSON,
                    CreatedAt = DateTime.UtcNow,
                    ClassId = request.ClassId
                });

                target.Status = TaskStatusConstants.Submitted;
                target.SubmittedAt = DateTime.UtcNow;
                _assignmentRepo.Update(target);
            }

            await _logService.LogActionAsync(
                userId,
                "SubmitTask",
                "Assignment",
                assignment.Id.ToString(),
                $"Annotator submitted task {assignment.Id} for review."
            );

            await _assignmentRepo.SaveChangesAsync();

            var project = await _projectRepo.GetByIdAsync(assignment.ProjectId);
            if (project != null && !string.IsNullOrEmpty(project.ManagerId))
            {
                await _notification.SendNotificationAsync(
                    project.ManagerId,
                    $"Task #{assignment.Id} in project \"{project.Name}\" has been submitted by annotator and is awaiting review.",
                    "Info");
            }

            var reviewerIds = syncTargets
                .Select(a => a.ReviewerId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var reviewerId in reviewerIds)
            {
                await _notification.SendNotificationAsync(
                    reviewerId!,
                    $"Task #{assignment.Id} in project \"{project?.Name ?? $"Project #{assignment.ProjectId}"}\" has been submitted by annotator and is waiting for your review.",
                    "Info");
            }
        }

        public async Task<SubmitMultipleTasksResponse> SubmitMultipleTasksAsync(string userId, SubmitMultipleTasksRequest request)
        {
            var response = new SubmitMultipleTasksResponse();
            int? lastProjectId = null;
            var processedGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using var transaction = await _assignmentRepo.BeginTransactionAsync();
            try
            {
                foreach (var id in request.AssignmentIds)
                {
                    var assignment = await _assignmentRepo.GetAssignmentWithDetailsAsync(id);

                    if (assignment == null)
                    {
                        response.FailureCount++;
                        response.Errors.Add($"Task ID {id}: Not found.");
                        continue;
                    }

                    if (assignment.AnnotatorId != userId)
                    {
                        response.FailureCount++;
                        response.Errors.Add($"Task ID {id}: Unauthorized access.");
                        continue;
                    }

                    var assignmentGroup = await GetAssignmentGroupAsync(assignment);
                    var groupKey = BuildAssignmentGroupKey(assignment);

                    if (!processedGroups.Add(groupKey))
                    {
                        continue;
                    }

                    if (assignmentGroup.All(a =>
                        string.Equals(a.Status, TaskStatusConstants.Submitted, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(a.Status, TaskStatusConstants.Approved, StringComparison.OrdinalIgnoreCase)))
                    {
                        response.FailureCount++;
                        response.Errors.Add($"Task ID {id}: Task has already been submitted or approved.");
                        continue;
                    }

                    var latestAnnotation = GetLatestAnnotation(assignmentGroup);

                    if (latestAnnotation == null || string.IsNullOrEmpty(latestAnnotation.DataJSON) || latestAnnotation.DataJSON == "[]")
                    {
                        response.FailureCount++;
                        response.Errors.Add($"Task ID {id}: Missing annotation data. Please save draft before submitting.");
                        continue;
                    }

                    var syncTargets = assignmentGroup
                        .Where(a => !HasBeenReviewed(a))
                        .ToList();

                    if (!syncTargets.Any())
                    {
                        syncTargets.Add(assignment);
                    }

                    foreach (var target in syncTargets)
                    {
                        if (NeedsAnnotationClone(target, latestAnnotation.DataJSON, latestAnnotation.ClassId))
                        {
                            await _annotationRepo.AddAsync(new Annotation
                            {
                                AssignmentId = target.Id,
                                DataJSON = latestAnnotation.DataJSON,
                                CreatedAt = DateTime.UtcNow,
                                ClassId = latestAnnotation.ClassId
                            });
                        }

                        target.Status = TaskStatusConstants.Submitted;
                        target.SubmittedAt = DateTime.UtcNow;
                        _assignmentRepo.Update(target);
                    }

                    response.SuccessCount++;
                    lastProjectId = assignment.ProjectId;
                }

                if (response.SuccessCount > 0)
                {
                    if (lastProjectId.HasValue)
                    {
                        await _logService.LogActionAsync(
                            userId,
                            "SubmitBatchTasks",
                            "Project",
                            lastProjectId.Value.ToString(),
                            $"Annotator batch submitted {response.SuccessCount} tasks for review."
                        );
                    }

                    await _assignmentRepo.SaveChangesAsync();
                    await transaction.CommitAsync();
                }
                else
                {
                    await transaction.RollbackAsync();
                }

                return response;
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
    }
}
