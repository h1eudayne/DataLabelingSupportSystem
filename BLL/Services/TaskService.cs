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

        private const string GUIDELINE_REFERENCE_COMMENT = "Decision based on official project guidelines";

        public TaskService(
            IAssignmentRepository assignmentRepo,
            IRepository<DataItem> dataItemRepo,
            IRepository<Annotation> annotationRepo,
            IStatisticService statisticService,
            IUserRepository userRepo,
            IProjectRepository projectRepo,
            IAppNotificationService notification,
            IActivityLogService logService)
        {
            _assignmentRepo = assignmentRepo;
            _dataItemRepo = dataItemRepo;
            _annotationRepo = annotationRepo;
            _statisticService = statisticService;
            _userRepo = userRepo;
            _projectRepo = projectRepo;
            _logService = logService;
            _notification = notification;
        }

        public async Task AssignTeamAsync(string managerId, AssignTeamRequest request)
        {
            var project = await _projectRepo.GetByIdAsync(request.ProjectId);
            if (project == null)
                throw new Exception("Project not found.");

            if (project.ManagerId != managerId)
                throw new UnauthorizedAccessException("You are not the manager of this project.");

            if (project.Status == "Completed" || project.Status == "Archived")
                throw new InvalidOperationException("BR-MNG-20: Tasks cannot be assigned in Completed or Archived projects");

            if (request.AnnotatorIds.Contains(managerId))
                throw new InvalidOperationException("BR-MNG-27: Manager cannot assign tasks to themselves");

            if (request.ReviewerIds != null && request.ReviewerIds.Contains(managerId))
                throw new InvalidOperationException("BR-MNG-27: Manager cannot assign tasks to themselves");

            var allUsers = await _userRepo.GetAllAsync();

            var validAnnotators = allUsers
                .Where(u => request.AnnotatorIds.Contains(u.Id) &&
                            u.Role == UserRoles.Annotator)
                .ToList();

            if (validAnnotators.Count != request.AnnotatorIds.Count)
                throw new Exception("One or more Annotator IDs are invalid or lack the required role.");

            var validReviewers = new List<User>();
            if (request.ReviewerIds != null && request.ReviewerIds.Any())
            {
                validReviewers = allUsers
                    .Where(u => request.ReviewerIds.Contains(u.Id) &&
                                u.Role == UserRoles.Reviewer)
                    .ToList();

                if (validReviewers.Count != request.ReviewerIds.Count)
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

            foreach (var annotator in validAnnotators)
            {
                await _statisticService.TrackNewAssignmentAsync(annotator.Id, request.ProjectId, totalItems);
                await _notification.SendNotificationAsync(
                    annotator.Id,
                    $"Manager has assigned you {totalItems} tasks in project {project.Name}! " +
                    $"(Each of your tasks will be reviewed by {totalRev} reviewers.)",
                    "Success");
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

            await _notification.SendNotificationAsync(
                request.AnnotatorId,
                $"Manager has assigned you {dataItems.Count} new tasks in the project!",
                "Success");

            if (!string.IsNullOrEmpty(request.ReviewerId))
            {
                await _notification.SendNotificationAsync(
                    request.ReviewerId,
                    $"You have been assigned as a Reviewer for {dataItems.Count} new tasks!",
                    "Info");
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
                    TotalImages = g.Count(),
                    CompletedImages = g.Count(a =>
                        a.Status == TaskStatusConstants.Submitted ||
                        a.Status == TaskStatusConstants.Approved),
                    Status = g.All(a => a.Status == TaskStatusConstants.Approved)
                        ? "Completed"
                        : g.Any(a => a.Status != TaskStatusConstants.Assigned)
                            ? "InProgress"
                            : "Assigned"
                })
                .ToList();
        }

        public async Task<List<AssignmentResponse>> GetTaskImagesAsync(int projectId, string annotatorId)
        {
            var assignments = await _assignmentRepo.GetAssignmentsByAnnotatorAsync(annotatorId, projectId);

            return assignments.Select(a =>
            {
                var taskDeadline = a.AssignedDate.AddHours(a.Project?.MaxTaskDurationHours ?? 24);
                var effectiveDeadline = taskDeadline < (a.Project?.Deadline ?? DateTime.UtcNow)
                    ? taskDeadline
                    : (a.Project?.Deadline ?? DateTime.UtcNow);

                return new AssignmentResponse
                {
                    Id = a.Id,
                    DataItemId = a.DataItemId,
                    DataItemUrl = a.DataItem?.StorageUrl ?? "",
                    Status = a.Status,
                    AnnotationData = a.Annotations?
                        .OrderByDescending(an => an.CreatedAt)
                        .FirstOrDefault()
                        ?.DataJSON ?? "",
                    AssignedDate = a.AssignedDate,
                    Deadline = effectiveDeadline,
                    RejectionReason = a.Status == TaskStatusConstants.Rejected
                    ? (a.ReviewLogs?.OrderByDescending(r => r.CreatedAt).FirstOrDefault()?.Comment ?? "")
                    : "",
                    ErrorCategory = a.Status == TaskStatusConstants.Rejected
                    ? (a.ReviewLogs?.OrderByDescending(r => r.CreatedAt).FirstOrDefault()?.ErrorCategory ?? "")
                    : ""
                };
            }).ToList();
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

            var annotation = new Annotation
            {
                AssignmentId = assignment.Id,
                DataJSON = request.DataJSON,
                CreatedAt = DateTime.UtcNow,
                ClassId = request.ClassId
            };

            await _annotationRepo.AddAsync(annotation);

            assignment.Status = TaskStatusConstants.Submitted;
            assignment.SubmittedAt = DateTime.UtcNow;

            _assignmentRepo.Update(assignment);

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
                    $"Task #{assignment.Id} has been submitted by annotator and is awaiting review.",
                    "Info");
            }

            if (!string.IsNullOrEmpty(assignment.ReviewerId))
            {
                await _notification.SendNotificationAsync(
                    assignment.ReviewerId,
                    $"Task #{assignment.Id} has been submitted and is waiting for your review!",
                    "Info");
            }
        }

        public async Task<SubmitMultipleTasksResponse> SubmitMultipleTasksAsync(string userId, SubmitMultipleTasksRequest request)
        {
            var response = new SubmitMultipleTasksResponse();
            int? lastProjectId = null;

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

                    if (assignment.Status == TaskStatusConstants.Submitted || assignment.Status == TaskStatusConstants.Approved)
                    {
                        response.FailureCount++;
                        response.Errors.Add($"Task ID {id}: Task has already been submitted or approved.");
                        continue;
                    }

                    var latestAnnotation = assignment.Annotations?.OrderByDescending(a => a.CreatedAt).FirstOrDefault();

                    if (latestAnnotation == null || string.IsNullOrEmpty(latestAnnotation.DataJSON) || latestAnnotation.DataJSON == "[]")
                    {
                        response.FailureCount++;
                        response.Errors.Add($"Task ID {id}: Missing annotation data. Please save draft before submitting.");
                        continue;
                    }

                    assignment.Status = TaskStatusConstants.Submitted;
                    assignment.SubmittedAt = DateTime.UtcNow;

                    _assignmentRepo.Update(assignment);
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
