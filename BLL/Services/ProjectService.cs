using BLL.Interfaces;
using BLL.Helpers;
using Core.Interfaces;
using Core.Constants;
using Core.Entities;
using System.Text.Json;
using System.Text;
using Core.DTOs.Requests;
using Core.DTOs.Responses;

namespace BLL.Services
{
    public class ProjectService : IProjectService
    {
        private readonly IProjectRepository _projectRepository;
        private readonly IUserRepository _userRepository;
        private readonly IRepository<UserProjectStat> _statsRepo;
        private readonly IAssignmentRepository _assignmentRepo;
        private readonly IActivityLogRepository _activityLogRepo;
        private readonly IRepository<ProjectFlag> _flagRepo;
        private readonly IDisputeRepository _disputeRepo;
        private readonly IAppNotificationService _notification;
        private readonly IWorkflowEmailService _workflowEmailService;

        private const string GUIDELINE_DECISION_NOTE = "Decision based on official project guidelines";

        private static string SafeSerializeAnnotations(IEnumerable<Annotation>? annotations)
        {
            try
            {
                if (annotations == null) return "[]";
                var latest = annotations.OrderByDescending(a => a.CreatedAt).FirstOrDefault();
                if (latest == null || string.IsNullOrWhiteSpace(latest.DataJSON)) return "[]";
                using var doc = JsonDocument.Parse(latest.DataJSON);
                return latest.DataJSON;
            }
            catch
            {
                return "[]";
            }
        }

        private static bool TryParseAnnotationJson(string? rawJson, out JsonElement element)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                element = JsonSerializer.Deserialize<JsonElement>("[]");
                return false;
            }

            try
            {
                element = JsonSerializer.Deserialize<JsonElement>(rawJson);
                return true;
            }
            catch
            {
                element = JsonSerializer.Deserialize<JsonElement>("[]");
                return false;
            }
        }

        private static Annotation? GetLatestValidAnnotation(IEnumerable<Annotation>? annotations)
        {
            if (annotations == null)
                return null;

            foreach (var annotation in annotations.OrderByDescending(a => a.CreatedAt))
            {
                if (TryParseAnnotationJson(annotation.DataJSON, out _))
                    return annotation;
            }

            return null;
        }

        private static Assignment? SelectExportAssignment(IEnumerable<Assignment> assignments)
        {
            return assignments
                .Where(a => string.Equals(a.Status, TaskStatusConstants.Approved, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(a => GetLatestValidAnnotation(a.Annotations)?.CreatedAt ?? DateTime.MinValue)
                .ThenByDescending(a => a.SubmittedAt ?? DateTime.MinValue)
                .ThenByDescending(a => a.Id)
                .FirstOrDefault();
        }

        private static int CountExportedAnnotations(JsonElement annotationData)
        {
            if (annotationData.ValueKind == JsonValueKind.Object &&
                annotationData.TryGetProperty("annotations", out var annotationsElement) &&
                annotationsElement.ValueKind == JsonValueKind.Array)
            {
                return annotationsElement.GetArrayLength();
            }

            return annotationData.ValueKind switch
            {
                JsonValueKind.Array => annotationData.GetArrayLength(),
                JsonValueKind.Object => 1,
                _ => 0
            };
        }

        private static string ExtractExportFileName(string storageUrl)
        {
            if (string.IsNullOrWhiteSpace(storageUrl))
                return string.Empty;

            if (Uri.TryCreate(storageUrl, UriKind.Absolute, out var absoluteUri))
            {
                return Path.GetFileName(Uri.UnescapeDataString(absoluteUri.LocalPath));
            }

            var normalizedPath = storageUrl
                .Split('?', '#')[0]
                .Replace('\\', '/');

            return Path.GetFileName(normalizedPath);
        }

        private static List<ProjectExportItemResponse> BuildApprovedExportRecords(Project project)
        {
            var exportRecords = new List<ProjectExportItemResponse>();

            foreach (var dataItem in project.DataItems.OrderBy(d => d.Id))
            {
                var approvedGroup = dataItem.Assignments
                    .GroupBy(BuildAssignmentGroupKey)
                    .Select(group => new
                    {
                        Assignments = group.ToList(),
                        AggregatedStatus = GetAggregatedAnnotatorStatus(group),
                        LatestApprovedAt = group
                            .Where(a => string.Equals(a.Status, TaskStatusConstants.Approved, StringComparison.OrdinalIgnoreCase))
                            .Select(a => a.SubmittedAt ?? DateTime.MinValue)
                            .DefaultIfEmpty(DateTime.MinValue)
                            .Max()
                    })
                    .Where(group => string.Equals(group.AggregatedStatus, TaskStatusConstants.Approved, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(group => group.LatestApprovedAt)
                    .FirstOrDefault();

                if (approvedGroup == null)
                    continue;

                var exportAssignment = SelectExportAssignment(approvedGroup.Assignments);
                if (exportAssignment == null)
                    continue;

                var latestAnnotation = GetLatestValidAnnotation(exportAssignment.Annotations)
                    ?? approvedGroup.Assignments
                        .SelectMany(a => a.Annotations ?? Enumerable.Empty<Annotation>())
                        .OrderByDescending(a => a.CreatedAt)
                        .FirstOrDefault(a => TryParseAnnotationJson(a.DataJSON, out _));

                if (latestAnnotation == null || !TryParseAnnotationJson(latestAnnotation.DataJSON, out var annotationData))
                    continue;

                exportRecords.Add(new ProjectExportItemResponse
                {
                    DataItemId = dataItem.Id,
                    BucketId = dataItem.BucketId,
                    FileName = ExtractExportFileName(dataItem.StorageUrl),
                    StorageUrl = dataItem.StorageUrl,
                    UploadedAt = dataItem.UploadedDate,
                    AnnotatorEmail = exportAssignment.Annotator?.Email ?? "Unknown",
                    ReviewerEmail = exportAssignment.Reviewer?.Email ?? string.Empty,
                    ApprovedAt = exportAssignment.SubmittedAt,
                    AnnotationCount = CountExportedAnnotations(annotationData),
                    AnnotationData = annotationData
                });
            }

            return exportRecords;
        }

        public ProjectService(
            IProjectRepository projectRepository,
            IUserRepository userRepository,
            IRepository<UserProjectStat> statsRepo,
            IAssignmentRepository assignmentRepo,
            IActivityLogRepository activityLogRepo,
            IRepository<ProjectFlag> flagRepo,
            IDisputeRepository disputeRepo,
            IAppNotificationService notification,
            IWorkflowEmailService workflowEmailService)
        {
            _projectRepository = projectRepository;
            _userRepository = userRepository;
            _statsRepo = statsRepo;
            _assignmentRepo = assignmentRepo;
            _activityLogRepo = activityLogRepo;
            _flagRepo = flagRepo;
            _disputeRepo = disputeRepo;
            _notification = notification;
            _workflowEmailService = workflowEmailService;
        }

        public async Task<object> GetUserProjectsByUserIdAsync(string userId)
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null) throw new Exception("User not found.");
            if (user.Role == UserRoles.Manager || user.Role == UserRoles.Admin)
            {
                return await GetProjectsByManagerAsync(userId);
            }
            else
            {
                return await GetAssignedProjectsAsync(userId);
            }
        }

        private static string GetFlagDescription(string flagType)
        {
            return flagType switch
            {
                FlagTypeConstants.CorruptedImage => "Image file is corrupted or cannot be opened",
                FlagTypeConstants.NoMatchingLabel => "No existing label category matches this image",
                FlagTypeConstants.DataQualityIssue => "Image quality is too low for accurate annotation",
                FlagTypeConstants.AmbiguousContent => "Content is unclear or ambiguous",
                FlagTypeConstants.DuplicateImage => "This image is a duplicate of another in the project",
                FlagTypeConstants.OutOfScope => "Image does not belong to the project's scope",
                FlagTypeConstants.IncorrectAnnotation => "Existing annotation contains errors",
                FlagTypeConstants.MissingParts => "Image appears to be truncated or missing parts",
                _ => "Flag reason not specified"
            };
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

        private static ReviewLog? GetLatestCurrentSubmissionReviewLog(Assignment assignment)
        {
            return GetCurrentSubmissionReviewLogs(assignment.ReviewLogs, assignment.SubmittedAt)
                .OrderByDescending(log => log.CreatedAt)
                .FirstOrDefault();
        }

        private static bool IsResolvedAnnotatorStatus(string? status)
        {
            return string.Equals(status, TaskStatusConstants.Approved, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(status, TaskStatusConstants.Rejected, StringComparison.OrdinalIgnoreCase);
        }

        private static bool WasAssignmentGroupSubmitted(IEnumerable<Assignment> assignments)
        {
            return assignments.Any(a =>
                a.SubmittedAt.HasValue ||
                string.Equals(a.Status, TaskStatusConstants.Submitted, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a.Status, TaskStatusConstants.Approved, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a.Status, TaskStatusConstants.Rejected, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a.Status, "Escalated", StringComparison.OrdinalIgnoreCase));
        }

        private sealed class ReviewerDecisionSnapshot
        {
            public string ReviewerId { get; init; } = string.Empty;
            public string ReviewerName { get; init; } = string.Empty;
            public string Verdict { get; init; } = string.Empty;
        }

        private sealed class AssignmentGroupAccuracySnapshot
        {
            public string GroupKey { get; init; } = string.Empty;
            public int ProjectId { get; init; }
            public string AnnotatorId { get; init; } = string.Empty;
            public string AnnotatorName { get; init; } = string.Empty;
            public string FinalStatus { get; init; } = TaskStatusConstants.Assigned;
            public int RejectCount { get; init; }
            public bool HasPendingDispute { get; init; }
            public bool WasSubmitted { get; init; }
            public List<ReviewerDecisionSnapshot> LatestCycleReviewerDecisions { get; init; } = new();
        }

        private static List<AssignmentGroupAccuracySnapshot> BuildAssignmentGroupAccuracySnapshots(
            IEnumerable<Assignment> assignments,
            IEnumerable<Dispute> projectDisputes)
        {
            return assignments
                .GroupBy(BuildAssignmentGroupKey)
                .Select(group =>
                {
                    var groupedAssignments = group.ToList();
                    var latestCycleReviewerDecisions = groupedAssignments
                        .Select(assignment => new
                        {
                            Assignment = assignment,
                            LatestLog = GetLatestCurrentSubmissionReviewLog(assignment)
                        })
                        .Where(item => item.LatestLog != null && !string.IsNullOrWhiteSpace(item.LatestLog.ReviewerId))
                        .Select(item => new ReviewerDecisionSnapshot
                        {
                            ReviewerId = item.LatestLog!.ReviewerId,
                            ReviewerName = item.Assignment.Reviewer?.FullName
                                ?? item.Assignment.Reviewer?.Email
                                ?? item.LatestLog.ReviewerId,
                            Verdict = item.LatestLog.Verdict
                        })
                        .ToList();

                    return new AssignmentGroupAccuracySnapshot
                    {
                        GroupKey = group.Key,
                        ProjectId = groupedAssignments.First().ProjectId,
                        AnnotatorId = groupedAssignments.First().AnnotatorId,
                        AnnotatorName = groupedAssignments.First().Annotator?.FullName
                            ?? groupedAssignments.First().Annotator?.Email
                            ?? groupedAssignments.First().AnnotatorId,
                        FinalStatus = GetAggregatedAnnotatorStatus(groupedAssignments),
                        RejectCount = groupedAssignments.Select(a => a.RejectCount).DefaultIfEmpty(0).Max(),
                        HasPendingDispute = HasPendingDisputeForGroup(groupedAssignments, projectDisputes),
                        WasSubmitted = WasAssignmentGroupSubmitted(groupedAssignments),
                        LatestCycleReviewerDecisions = latestCycleReviewerDecisions
                    };
                })
                .ToList();
        }

        private static string GetAggregatedAnnotatorStatus(IEnumerable<Assignment> assignments)
        {
            var groupedAssignments = assignments.ToList();

            if (!groupedAssignments.Any())
                return TaskStatusConstants.Assigned;

            if (groupedAssignments.Any(a => string.Equals(a.Status, "Escalated", StringComparison.OrdinalIgnoreCase)))
                return "Escalated";

            if (groupedAssignments.Any(a => string.Equals(a.Status, TaskStatusConstants.InProgress, StringComparison.OrdinalIgnoreCase)))
                return TaskStatusConstants.InProgress;

            if (groupedAssignments.Any(a => string.Equals(a.Status, TaskStatusConstants.Submitted, StringComparison.OrdinalIgnoreCase)))
                return TaskStatusConstants.Submitted;

            int approvedCount = groupedAssignments.Count(a => string.Equals(a.Status, TaskStatusConstants.Approved, StringComparison.OrdinalIgnoreCase));
            int rejectedCount = groupedAssignments.Count(a => string.Equals(a.Status, TaskStatusConstants.Rejected, StringComparison.OrdinalIgnoreCase));

            if (approvedCount > 0 || rejectedCount > 0)
            {
                if (approvedCount > rejectedCount)
                    return TaskStatusConstants.Approved;

                if (rejectedCount > approvedCount)
                    return TaskStatusConstants.Rejected;

                return "Escalated";
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

        private static string NormalizeReviewerAssignmentStatus(Assignment template)
        {
            if (string.Equals(template.Status, TaskStatusConstants.Approved, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(template.Status, TaskStatusConstants.Rejected, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(template.Status, "Escalated", StringComparison.OrdinalIgnoreCase))
            {
                return TaskStatusConstants.Submitted;
            }

            return template.Status;
        }

        private static string BuildAssignmentGroupKey(Assignment assignment)
        {
            return $"{assignment.ProjectId}:{assignment.DataItemId}:{assignment.AnnotatorId}";
        }

        private static bool HasPendingDisputeForGroup(
            IEnumerable<Assignment> assignments,
            IEnumerable<Dispute> disputes)
        {
            var assignmentIds = assignments
                .Select(a => a.Id)
                .ToHashSet();

            return disputes.Any(d =>
                assignmentIds.Contains(d.AssignmentId) &&
                string.Equals(d.Status, "Pending", StringComparison.OrdinalIgnoreCase));
        }

        private static ReviewerFeedbackResponse MapReviewerFeedback(Assignment assignment, ReviewLog reviewLog)
        {
            return new ReviewerFeedbackResponse
            {
                ReviewLogId = reviewLog.Id,
                ReviewerId = reviewLog.ReviewerId,
                ReviewerName = reviewLog.Reviewer?.FullName
                    ?? reviewLog.Reviewer?.Email
                    ?? assignment.Reviewer?.FullName
                    ?? assignment.Reviewer?.Email
                    ?? reviewLog.ReviewerId,
                Decision = reviewLog.Decision,
                Verdict = reviewLog.Verdict,
                Comment = reviewLog.Comment,
                ErrorCategories = reviewLog.ErrorCategory,
                ReviewedAt = reviewLog.CreatedAt,
                ScorePenalty = reviewLog.ScorePenalty,
                IsApproved = reviewLog.IsApproved,
                IsAudited = reviewLog.IsAudited,
                AuditResult = reviewLog.AuditResult
            };
        }

        private static List<ReviewerFeedbackResponse> BuildLatestReviewerFeedbacks(IEnumerable<Assignment> assignments)
        {
            return assignments
                .Select(assignment => new
                {
                    Assignment = assignment,
                    LatestLog = GetCurrentSubmissionReviewLogs(assignment.ReviewLogs, assignment.SubmittedAt)
                        .OrderByDescending(log => log.CreatedAt)
                        .FirstOrDefault()
                })
                .Where(item => item.LatestLog != null)
                .Select(item => MapReviewerFeedback(item.Assignment, item.LatestLog!))
                .OrderByDescending(item => item.ReviewedAt)
                .ToList();
        }

        private static List<ReviewerFeedbackResponse> BuildReviewerFeedbackHistory(IEnumerable<Assignment> assignments)
        {
            return assignments
                .SelectMany(
                    assignment => assignment.ReviewLogs ?? Enumerable.Empty<ReviewLog>(),
                    (assignment, reviewLog) => new { Assignment = assignment, ReviewLog = reviewLog })
                .OrderByDescending(item => item.ReviewLog.CreatedAt)
                .Select(item => MapReviewerFeedback(item.Assignment, item.ReviewLog))
                .ToList();
        }

        private static ProjectCompletionReviewItemResponse BuildCompletionReviewItem(
            DataItem dataItem,
            IEnumerable<Assignment> groupedAssignments)
        {
            var assignments = groupedAssignments.ToList();
            var representativeAssignment = SelectRepresentativeAnnotatorAssignment(assignments);
            var latestAnnotation = GetLatestValidAnnotation(
                assignments.SelectMany(a => a.Annotations ?? Enumerable.Empty<Annotation>()).ToList());
            var latestFeedbacks = BuildLatestReviewerFeedbacks(assignments);
            var reviewHistory = BuildReviewerFeedbackHistory(assignments);

            return new ProjectCompletionReviewItemResponse
            {
                AssignmentId = representativeAssignment.Id,
                DataItemId = dataItem.Id,
                DataItemUrl = dataItem.StorageUrl,
                AnnotatorId = representativeAssignment.AnnotatorId,
                AnnotatorName = representativeAssignment.Annotator?.FullName
                    ?? representativeAssignment.Annotator?.Email
                    ?? representativeAssignment.AnnotatorId,
                Status = GetAggregatedAnnotatorStatus(assignments),
                RejectCount = assignments.Select(a => a.RejectCount).DefaultIfEmpty(0).Max(),
                ReviewEventCount = reviewHistory.Count,
                ReviewerCount = assignments
                    .Select(a => a.ReviewerId)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count(),
                SubmittedAt = assignments
                    .Where(a => a.SubmittedAt.HasValue)
                    .OrderByDescending(a => a.SubmittedAt)
                    .FirstOrDefault()
                    ?.SubmittedAt,
                AnnotationData = latestAnnotation?.DataJSON,
                ManagerDecision = assignments
                    .Select(a => a.ManagerDecision)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
                ManagerComment = assignments
                    .Select(a => a.ManagerComment)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
                ReviewerFeedbacks = latestFeedbacks,
                ReviewHistory = reviewHistory
            };
        }

        public async Task<int> UploadDirectDataItemsAsync(int projectId, List<(Stream Content, string Extension)> files, string webRootPath)
        {
            var project = await _projectRepository.GetByIdAsync(projectId);
            if (project == null) throw new Exception("Project not found");

            if (files == null || !files.Any()) throw new Exception("No files provided.");

            var uploadFolder = Path.Combine(webRootPath, "uploads", projectId.ToString());
            if (!Directory.Exists(uploadFolder))
            {
                Directory.CreateDirectory(uploadFolder);
            }

            var storageUrls = new List<string>();

            foreach (var file in files)
            {
                var fileName = Guid.NewGuid().ToString() + file.Extension;
                var filePath = Path.Combine(uploadFolder, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.Content.CopyToAsync(stream);
                }

                var fileUrl = $"/uploads/{projectId}/{fileName}";
                storageUrls.Add(fileUrl);
            }

            await ImportDataItemsAsync(projectId, storageUrls);
            return storageUrls.Count;
        }

        public async Task<ProjectDetailResponse> CreateProjectAsync(string managerId, CreateProjectRequest request)
        {
            var manager = await _userRepository.GetByIdAsync(managerId);
            if (manager == null) throw new Exception("User not found");

            if (manager.Role != UserRoles.Manager)
                throw new Exception("BR-MNG-01: Only a Manager can create and manage labeling projects");

            var startDate = request.StartDate ?? DateTime.UtcNow;
            var deadline = request.Deadline ?? DateTime.UtcNow.AddDays(30);
            var endDate = request.EndDate ?? deadline;

            int penaltyUnit = request.PenaltyUnit > 0 ? request.PenaltyUnit : 10;

            var project = new Project
            {
                ManagerId = managerId,
                Name = request.Name,
                Description = request.Description ?? "",
                StartDate = startDate,
                EndDate = endDate,
                Deadline = deadline,
                CreatedDate = DateTime.UtcNow,
                AllowGeometryTypes = request.AllowGeometryTypes ?? "Rectangle",
                AnnotationGuide = request.AnnotationGuide ?? "",
                MaxTaskDurationHours = request.MaxTaskDurationHours > 0 ? request.MaxTaskDurationHours : 24,
                PenaltyUnit = penaltyUnit,
                Status = ProjectStatusConstants.Draft
            };

            if (request.ReviewChecklist != null && request.ReviewChecklist.Any())
            {
                foreach (var item in request.ReviewChecklist)
                {
                    project.ChecklistItems.Add(new ReviewChecklistItem
                    {
                        Code = item.Code,
                        Description = item.Description,
                        Weight = item.Weight,
                        IsCritical = item.Weight >= 3
                    });
                }
            }

            foreach (var label in request.LabelClasses)
            {
                project.LabelClasses.Add(new LabelClass
                {
                    Name = label.Name,
                    Color = label.Color,
                    GuideLine = label.GuideLine,
                    ExampleImageUrl = label.ExampleImageUrl,
                    IsDefault = label.IsDefault,
                    DefaultChecklist = (label.Checklist != null && label.Checklist.Any())
                                            ? JsonSerializer.Serialize(label.Checklist)
                                            : "[]"
                });
            }

            await _projectRepository.AddAsync(project);
            await _projectRepository.SaveChangesAsync();

            foreach (var flagType in FlagTypeConstants.DefaultFlags)
            {
                var flag = new ProjectFlag
                {
                    ProjectId = project.Id,
                    FlagType = flagType,
                    Description = GetFlagDescription(flagType),
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                };
                project.ProjectFlags.Add(flag);
                await _flagRepo.AddAsync(flag);
            }
            await _flagRepo.SaveChangesAsync();

            await _activityLogRepo.AddAsync(new ActivityLog
            {
                UserId = managerId,
                ActionType = "CreateProject",
                EntityName = "Project",
                EntityId = project.Id.ToString(),
                Description = $"Created a new project: {project.Name}",
                Timestamp = DateTime.UtcNow
            });
            await _activityLogRepo.SaveChangesAsync();

            return new ProjectDetailResponse
            {
                Id = project.Id,
                Name = project.Name,
                Description = project.Description,
                Deadline = project.Deadline,
                AllowGeometryTypes = project.AllowGeometryTypes,
                ManagerId = project.ManagerId,
                ManagerName = manager.FullName,
                ManagerEmail = manager.Email,
                Labels = project.LabelClasses.Select(l => new LabelResponse
                {
                    Id = l.Id,
                    Name = l.Name,
                    Color = l.Color,
                    GuideLine = l.GuideLine,
                    ExampleImageUrl = l.ExampleImageUrl,
                    IsDefault = l.IsDefault,
                    Checklist = !string.IsNullOrEmpty(l.DefaultChecklist)
                                ? JsonSerializer.Deserialize<List<string>>(l.DefaultChecklist, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<string>()
                                : new List<string>()
                }).ToList(),
                TotalDataItems = 0,
                UnassignedDataItemCount = 0,
                ProcessedItems = 0,
                Status = ProjectStatusConstants.NewDisplay,
                IsAwaitingManagerConfirmation = false,
                CanManagerConfirmCompletion = false
            };
        }

        public async Task UpdateProjectAsync(int projectId, UpdateProjectRequest request, string actingUserId)
        {
            var project = await _projectRepository.GetByIdAsync(projectId);
            if (project == null) throw new Exception("Project not found");

            var actor = await _userRepository.GetByIdAsync(actingUserId);
            if (actor == null) throw new Exception("User not found");
            if (actor.Role == UserRoles.Admin)
                throw new Exception("BR-ADM-18: Admin is not allowed to modify project information");
            if (project.ManagerId != actingUserId)
                throw new UnauthorizedAccessException("Only the project manager can update this project.");

            project.Name = request.Name;
            if (!string.IsNullOrEmpty(request.Description)) project.Description = request.Description;
            if (!string.IsNullOrEmpty(request.AllowGeometryTypes))
            {
                project.AllowGeometryTypes = request.AllowGeometryTypes;
            }

            if (request.Deadline.HasValue) project.Deadline = request.Deadline.Value;
            if (request.StartDate.HasValue) project.StartDate = request.StartDate.Value;
            if (request.EndDate.HasValue) project.EndDate = request.EndDate.Value;

            if (request.AnnotationGuide != null)
            {
                project.AnnotationGuide = request.AnnotationGuide;
            }

            if (request.MaxTaskDurationHours.HasValue)
            {
                project.MaxTaskDurationHours = request.MaxTaskDurationHours.Value;
            }

            if (request.PenaltyUnit > 0)
            {
                project.PenaltyUnit = request.PenaltyUnit;
            }

            if (request.ReviewChecklist != null)
            {
                project.ChecklistItems.Clear();
                foreach (var item in request.ReviewChecklist)
                {
                    project.ChecklistItems.Add(new ReviewChecklistItem
                    {
                        ProjectId = projectId,
                        Code = item.Code,
                        Description = item.Description,
                        Weight = item.Weight,
                        IsCritical = item.Weight >= 3
                    });
                }
            }

            _projectRepository.Update(project);
            await _projectRepository.SaveChangesAsync();

            await _activityLogRepo.AddAsync(new ActivityLog
            {
                UserId = project.ManagerId,
                ActionType = "UpdateProject",
                EntityName = "Project",
                EntityId = project.Id.ToString(),
                Description = $"Updated project details: {project.Name}",
                Timestamp = DateTime.UtcNow
            });
            await _activityLogRepo.SaveChangesAsync();
        }

        public async Task<List<AnnotatorProjectStatsResponse>> GetAssignedProjectsAsync(string annotatorId)
        {
            var projects = await _projectRepository.GetProjectsByAnnotatorAsync(annotatorId);
            var result = new List<AnnotatorProjectStatsResponse>();

            foreach (var p in projects)
            {
                var myAssignments = p.DataItems
                    .SelectMany(d => d.Assignments)
                    .Where(a => a.AnnotatorId == annotatorId)
                    .ToList();
                var groupedAssignments = myAssignments
                    .GroupBy(a => a.DataItemId)
                    .Select(group => new
                    {
                        Status = GetAggregatedAnnotatorStatus(group),
                        Representative = SelectRepresentativeAnnotatorAssignment(group)
                    })
                    .ToList();
                var total = groupedAssignments.Count;
                var completed = groupedAssignments.Count(g =>
                    string.Equals(g.Status, TaskStatusConstants.Approved, StringComparison.OrdinalIgnoreCase));
                var nextTask = groupedAssignments
                    .Where(g =>
                        string.Equals(g.Status, TaskStatusConstants.InProgress, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(g.Status, TaskStatusConstants.Rejected, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(g.Status, TaskStatusConstants.Assigned, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(g => GetAnnotatorStatusPriority(g.Status))
                    .ThenBy(g => g.Representative.Id)
                    .Select(g => g.Representative)
                    .FirstOrDefault();
                int approvedProjectItems = ProjectWorkflowStatusHelper.CountApprovedDataItems(p);
                bool allAnnotatorWorkApproved = total > 0 && total == completed;
                bool isAwaitingManagerConfirmation = allAnnotatorWorkApproved &&
                    ProjectWorkflowStatusHelper.IsAwaitingManagerConfirmation(p, p.DataItems.Count, approvedProjectItems);

                string status = ProjectStatusConstants.Active;
                if (string.Equals(p.Status, ProjectStatusConstants.Completed, StringComparison.OrdinalIgnoreCase))
                {
                    status = ProjectStatusConstants.Completed;
                }
                else if (DateTime.UtcNow > p.Deadline)
                {
                    status = ProjectStatusConstants.ExpiredDisplay;
                }
                else if (isAwaitingManagerConfirmation)
                {
                    status = ProjectStatusConstants.AwaitingManagerConfirmation;
                }

                result.Add(new AnnotatorProjectStatsResponse
                {
                    ProjectId = p.Id,
                    ProjectName = p.Name,
                    TotalImages = total,
                    CompletedImages = completed,
                    Status = status,
                    IsAwaitingManagerConfirmation = isAwaitingManagerConfirmation,
                    Deadline = p.Deadline,
                    AssignmentId = nextTask?.Id
                });
            }

            return result;
        }

        public async Task ImportDataItemsAsync(int projectId, List<string> storageUrls)
        {
            var project = await _projectRepository.GetByIdAsync(projectId);
            if (project == null) throw new Exception("Project not found");

            var currentTotalItems = await _projectRepository.GetProjectDataItemsCountAsync(projectId);
            int bucketSize = 50;

            var newDataItems = new List<DataItem>();
            foreach (var url in storageUrls)
            {
                currentTotalItems++;
                int bucketId = (currentTotalItems - 1) / bucketSize + 1;

                var dataItem = new DataItem
                {
                    ProjectId = projectId,
                    StorageUrl = url,
                    UploadedDate = DateTime.UtcNow,
                    Status = TaskStatusConstants.New,
                    BucketId = bucketId,
                    MetaData = "{}",
                    Assignments = new List<Assignment>()
                };
                newDataItems.Add(dataItem);
            }

            await _projectRepository.AddDataItemsAsync(newDataItems);
            await _projectRepository.SaveChangesAsync();

            if (string.Equals(project.Status, ProjectStatusConstants.Draft, StringComparison.OrdinalIgnoreCase))
            {
                project.Status = ProjectStatusConstants.Active;
                _projectRepository.Update(project);
                await _projectRepository.SaveChangesAsync();
            }

            await _activityLogRepo.AddAsync(new ActivityLog
            {
                UserId = project.ManagerId,
                ActionType = "ImportData",
                EntityName = "Project",
                EntityId = project.Id.ToString(),
                Description = $"Imported {storageUrls.Count} data items into project {project.Name}",
                Timestamp = DateTime.UtcNow
            });
            await _activityLogRepo.SaveChangesAsync();
        }

        public async Task<ProjectDetailResponse?> GetProjectDetailsAsync(int projectId)
        {
            var project = await _projectRepository.GetProjectWithDetailsAsync(projectId);
            if (project == null) return null;

            var allAssignments = project.DataItems.SelectMany(d => d.Assignments).ToList();
            int total = project.DataItems.Count;
            int unassignedCount = project.DataItems.Count(d => d.Status == TaskStatusConstants.New);
            int done = ProjectWorkflowStatusHelper.CountApprovedDataItems(project);
            int progressPercent = (total > 0) ? (int)((double)done / total * 100) : 0;
            bool hasStarted = total > 0 && project.DataItems.Any(d => d.Status != TaskStatusConstants.New);
            string currentStatus = ProjectWorkflowStatusHelper.ResolveManagerFacingStatus(project, total, done, hasStarted);
            bool isAwaitingManagerConfirmation = ProjectWorkflowStatusHelper.IsAwaitingManagerConfirmation(project, total, done);
            bool canManagerConfirmCompletion = ProjectWorkflowStatusHelper.CanManagerConfirmCompletion(project, total, done);

            var annotators = allAssignments
                .Where(a => a.Annotator != null)
                .GroupBy(a => a.AnnotatorId)
                .Select(g => new MemberResponse
                {
                    Id = g.Key,
                    FullName = g.First().Annotator!.FullName ?? g.First().Annotator!.Email,
                    Email = g.First().Annotator!.Email,
                    Role = g.First().Annotator!.Role,
                    TasksAssigned = g.GroupBy(a => a.DataItemId).Count(),
                    TasksCompleted = g.GroupBy(a => a.DataItemId)
                        .Count(group => string.Equals(
                            GetAggregatedAnnotatorStatus(group),
                            TaskStatusConstants.Approved,
                            StringComparison.OrdinalIgnoreCase)),
                    Progress = g.GroupBy(a => a.DataItemId).Any()
                        ? Math.Round(
                            (decimal)g.GroupBy(a => a.DataItemId)
                                .Count(group => string.Equals(
                                    GetAggregatedAnnotatorStatus(group),
                                    TaskStatusConstants.Approved,
                                    StringComparison.OrdinalIgnoreCase)) /
                            g.GroupBy(a => a.DataItemId).Count() * 100,
                            2)
                        : 0
                }).ToList();

            var reviewers = allAssignments
                .Where(a => a.Reviewer != null)
                .GroupBy(a => a.ReviewerId)
                .Select(g => new MemberResponse
                {
                    Id = g.Key!,
                    FullName = g.First().Reviewer!.FullName ?? g.First().Reviewer!.Email,
                    Email = g.First().Reviewer!.Email,
                    Role = g.First().Reviewer!.Role,
                    TasksAssigned = g.Count(),
                    TasksCompleted = g.Count(a => a.Status == TaskStatusConstants.Approved || a.Status == TaskStatusConstants.Rejected),
                    Progress = g.Count() > 0
                        ? Math.Round((decimal)g.Count(a => a.Status == TaskStatusConstants.Approved || a.Status == TaskStatusConstants.Rejected) / g.Count() * 100, 2)
                        : 0
                }).ToList();

            var members = annotators.Concat(reviewers)
                .GroupBy(m => m.Id)
                .Select(g => g.First())
                .ToList();

            return new ProjectDetailResponse
            {
                Id = project.Id,
                Name = project.Name,
                Description = project.Description,
                Deadline = project.Deadline,
                AllowGeometryTypes = project.AllowGeometryTypes,
                ManagerId = project.ManagerId,
                ManagerName = project.Manager?.FullName ?? "Unknown",
                ManagerEmail = project.Manager?.Email ?? "",
                Labels = project.LabelClasses.Select(l => new LabelResponse
                {
                    Id = l.Id,
                    Name = l.Name,
                    Color = l.Color,
                    GuideLine = l.GuideLine,
                    ExampleImageUrl = l.ExampleImageUrl,
                    IsDefault = l.IsDefault,
                    Checklist = !string.IsNullOrEmpty(l.DefaultChecklist)
                                ? JsonSerializer.Deserialize<List<string>>(l.DefaultChecklist, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<string>()
                                : new List<string>()
                }).ToList(),
                TotalDataItems = total,
                UnassignedDataItemCount = unassignedCount,
                ProcessedItems = done,
                Progress = progressPercent,
                Status = currentStatus,
                IsAwaitingManagerConfirmation = isAwaitingManagerConfirmation,
                CanManagerConfirmCompletion = canManagerConfirmCompletion,
                Members = members
            };
        }

        public async Task<List<ProjectSummaryResponse>> GetProjectsByManagerAsync(string managerId)
        {
            var projects = await _projectRepository.GetProjectsByManagerIdAsync(managerId);
            var summaries = new List<ProjectSummaryResponse>();

            foreach (var project in projects)
            {
                int totalItems = project.DataItems.Count;
                int approvedCount = ProjectWorkflowStatusHelper.CountApprovedDataItems(project);

                decimal progress = totalItems > 0 ? (decimal)approvedCount / totalItems * 100 : 0;
                bool hasStarted = totalItems > 0 && project.DataItems.Any(d => d.Status != TaskStatusConstants.New);
                string currentStatus = ProjectWorkflowStatusHelper.ResolveManagerFacingStatus(project, totalItems, approvedCount, hasStarted);
                bool isAwaitingManagerConfirmation = ProjectWorkflowStatusHelper.IsAwaitingManagerConfirmation(project, totalItems, approvedCount);
                bool canManagerConfirmCompletion = ProjectWorkflowStatusHelper.CanManagerConfirmCompletion(project, totalItems, approvedCount);

                int pendingPenaltyCount = project.DataItems
                    .SelectMany(dataItem => dataItem.Assignments
                        .GroupBy(assignment => assignment.AnnotatorId)
                        .Select(group => GetAggregatedAnnotatorStatus(group)))
                    .Count(status => string.Equals(status, "Escalated", StringComparison.OrdinalIgnoreCase));

                int rejectedImageCount = project.DataItems
                    .SelectMany(dataItem => dataItem.Assignments
                        .GroupBy(assignment => assignment.AnnotatorId)
                        .Select(group => GetAggregatedAnnotatorStatus(group)))
                    .Count(status => string.Equals(status, TaskStatusConstants.Rejected, StringComparison.OrdinalIgnoreCase));

                var projectDisputes = await _disputeRepo.GetDisputesByProjectAsync(project.Id) ?? new List<Dispute>();
                var pendingDisputeCount = projectDisputes
                    .Count(dispute => string.Equals(dispute.Status, "Pending", StringComparison.OrdinalIgnoreCase));

                int priorityIssueCount = pendingDisputeCount + pendingPenaltyCount + rejectedImageCount;

                summaries.Add(new ProjectSummaryResponse
                {
                    Id = project.Id,
                    Name = project.Name,
                    Deadline = project.Deadline,
                    TotalDataItems = totalItems,
                    Status = currentStatus,
                    Progress = progress,
                    TotalMembers = project.DataItems
                        .SelectMany(d => d.Assignments)
                        .SelectMany(a => new[] { a.AnnotatorId, a.ReviewerId })
                        .Where(id => !string.IsNullOrEmpty(id))
                        .Distinct()
                        .Count(),
                    PendingDisputeCount = pendingDisputeCount,
                    PendingPenaltyCount = pendingPenaltyCount,
                    RejectedImageCount = rejectedImageCount,
                    PriorityIssueCount = priorityIssueCount,
                    HasPriorityIssue = priorityIssueCount > 0,
                    DefaultActionTab = pendingDisputeCount > 0 || pendingPenaltyCount > 0
                        ? "disputes"
                        : "datasets",
                    IsAwaitingManagerConfirmation = isAwaitingManagerConfirmation,
                    CanManagerConfirmCompletion = canManagerConfirmCompletion
                });
            }

            return summaries
                .OrderByDescending(summary => summary.HasPriorityIssue)
                .ThenByDescending(summary => summary.PendingDisputeCount)
                .ThenByDescending(summary => summary.PendingPenaltyCount)
                .ThenByDescending(summary => summary.RejectedImageCount)
                .ThenByDescending(summary => summary.Id)
                .ToList();
        }

        public async Task DeleteProjectAsync(int projectId)
        {
            var project = await _projectRepository.GetByIdAsync(projectId);
            if (project == null) throw new Exception("Project not found");

            var managerId = project.ManagerId;
            var projectName = project.Name;

            _projectRepository.Delete(project);
            await _projectRepository.SaveChangesAsync();

            await _activityLogRepo.AddAsync(new ActivityLog
            {
                UserId = managerId,
                ActionType = "DeleteProject",
                EntityName = "Project",
                EntityId = projectId.ToString(),
                Description = $"Deleted project: {projectName}",
                Timestamp = DateTime.UtcNow
            });
            await _activityLogRepo.SaveChangesAsync();
        }

        public async Task<byte[]> ExportProjectDataAsync(int projectId, string userId)
        {
            var project = await _projectRepository.GetProjectForExportAsync(projectId);
            if (project == null) throw new Exception("Project not found");
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null) throw new Exception("User not found");
            if (user.Role != UserRoles.Admin && project.ManagerId != userId)
                throw new Exception("Unauthorized to export this project.");

            var totalItems = project.DataItems.Count;
            if (totalItems == 0)
                throw new InvalidOperationException("No data items available in this project to export.");

            var completedItems = project.DataItems.Count(d => d.Status == TaskStatusConstants.Approved);
            if (completedItems < totalItems)
            {
                double currentProgress = Math.Round((double)completedItems / totalItems * 100, 2);
                throw new InvalidOperationException($"BR-MNG-12: Export is only allowed when all assignments are Approved. Current progress: {currentProgress}% ({completedItems}/{totalItems} items).");
            }

            var exportRecords = BuildApprovedExportRecords(project);
            if (!exportRecords.Any())
                throw new InvalidOperationException("No approved annotation data available for export.");

            var exportedAt = DateTime.UtcNow;

            var exportData = new ProjectExportResponse
            {
                Project = new ProjectExportProjectInfoResponse
                {
                    Id = project.Id,
                    Name = project.Name,
                    Description = project.Description,
                    GuidelineVersion = project.GuidelineVersion,
                    Deadline = project.Deadline,
                    ExportedAt = exportedAt,
                    TotalDataItems = totalItems,
                    ExportedItems = exportRecords.Count,
                    Labels = project.LabelClasses
                        .Select(l => new ProjectExportLabelResponse
                        {
                            Id = l.Id,
                            Name = l.Name,
                            Color = l.Color
                        })
                        .ToList()
                },
                Items = exportRecords
            };

            var json = JsonSerializer.Serialize(exportData, new JsonSerializerOptions { WriteIndented = true });

            await _activityLogRepo.AddAsync(new ActivityLog
            {
                UserId = userId,
                ActionType = "ExportProject",
                EntityName = "Project",
                EntityId = projectId.ToString(),
                Description = $"Exported data for project: {project.Name}",
                Timestamp = DateTime.UtcNow
            });
            await _activityLogRepo.SaveChangesAsync();

            return Encoding.UTF8.GetBytes(json);
        }

        public async Task<ProjectStatisticsResponse> GetProjectStatisticsAsync(int projectId)
        {
            var project = await _projectRepository.GetProjectWithStatsDataAsync(projectId);
            if (project == null) throw new Exception("Project not found");

            var projectUserStats = (await _statsRepo.FindAsync(s => s.ProjectId == projectId)).ToList();

            var allAssignments = project.DataItems.SelectMany(d => d.Assignments).ToList();
            var allReviewLogs = allAssignments.SelectMany(a => a.ReviewLogs ?? new List<ReviewLog>()).ToList();
            var projectDisputes = await _disputeRepo.GetDisputesByProjectAsync(projectId);
            var assignmentGroups = BuildAssignmentGroupAccuracySnapshots(allAssignments, projectDisputes);
            var totalReviewed = allReviewLogs.Count;
            var totalRejectedLogs = allReviewLogs.Count(l => IsRejectedVerdict(l.Verdict));
            var totalItems = project.DataItems.Count;
            int finalCorrect = assignmentGroups.Count(group =>
                string.Equals(group.FinalStatus, TaskStatusConstants.Approved, StringComparison.OrdinalIgnoreCase));
            int firstPassCorrect = assignmentGroups.Count(group =>
                string.Equals(group.FinalStatus, TaskStatusConstants.Approved, StringComparison.OrdinalIgnoreCase) &&
                group.RejectCount == 0);
            int totalSubmittedTasks = assignmentGroups.Count(group => group.WasSubmitted);
            int totalReworks = assignmentGroups.Count(group => group.RejectCount > 0);
            double projectAccuracy = totalItems > 0
                ? Math.Round((double)firstPassCorrect / totalItems * 100, 2)
                : 0;
            double finalAccuracy = totalItems > 0
                ? Math.Round((double)finalCorrect / totalItems * 100, 2)
                : 0;
            double reworkRate = totalSubmittedTasks > 0
                ? Math.Round((double)totalReworks / totalSubmittedTasks * 100, 2)
                : 0;

            var stats = new ProjectStatisticsResponse
            {
                ProjectId = project.Id,
                ProjectName = project.Name,
                ProjectStatus = ProjectWorkflowStatusHelper.ResolveManagerFacingStatus(
                    project,
                    project.DataItems.Count,
                    ProjectWorkflowStatusHelper.CountApprovedDataItems(project),
                    project.DataItems.Any(d => d.Status != TaskStatusConstants.New)),
                IsAwaitingManagerConfirmation = ProjectWorkflowStatusHelper.IsAwaitingManagerConfirmation(
                    project,
                    project.DataItems.Count,
                    ProjectWorkflowStatusHelper.CountApprovedDataItems(project)),
                CanManagerConfirmCompletion = ProjectWorkflowStatusHelper.CanManagerConfirmCompletion(
                    project,
                    project.DataItems.Count,
                    ProjectWorkflowStatusHelper.CountApprovedDataItems(project)),
                TotalItems = project.DataItems.Count,
                CompletedItems = project.DataItems.Count(d => d.Status == TaskStatusConstants.Approved),
                TotalAssignments = allAssignments.Count,
                PendingAssignments = project.DataItems.Count(d => d.Status == TaskStatusConstants.New) +
                                 allAssignments.Count(a => a.Status == TaskStatusConstants.New || a.Status == TaskStatusConstants.Assigned || a.Status == TaskStatusConstants.InProgress),
                SubmittedAssignments = allAssignments.Count(a => a.Status == TaskStatusConstants.Submitted),
                ApprovedAssignments = allAssignments.Count(a => a.Status == TaskStatusConstants.Approved),
                RejectedAssignments = allAssignments.Count(a => a.Status == TaskStatusConstants.Rejected),
                RejectionRate = totalReviewed > 0
                    ? Math.Round((double)totalRejectedLogs / totalReviewed * 100, 2)
                    : 0,
                ErrorBreakdown = allReviewLogs
                    .Where(l => IsRejectedVerdict(l.Verdict) && !string.IsNullOrEmpty(l.ErrorCategory))
                    .GroupBy(l => l.ErrorCategory!)
                    .ToDictionary(g => g.Key, g => g.Count()),
                ProjectAccuracy = projectAccuracy,
                FinalCorrect = finalCorrect,
                FirstPassCorrect = firstPassCorrect,
                TotalReworks = totalReworks,
                TotalSubmittedTasks = totalSubmittedTasks,
                FinalAccuracy = finalAccuracy,
                FirstPassAccuracy = projectAccuracy,
                ReworkRate = reworkRate
            };

            if (stats.TotalItems > 0)
            {
                stats.ProgressPercentage = Math.Round((decimal)stats.CompletedItems / stats.TotalItems * 100, 2);
            }

            stats.AnnotatorPerformances = assignmentGroups
                .GroupBy(group => group.AnnotatorId)
                .Select(g =>
                {
                    var annotatorId = g.Key;
                    var userStat = projectUserStats.FirstOrDefault(s => s.UserId == annotatorId);
                    var groupedTasks = g.ToList();
                    int approvedTasks = groupedTasks.Count(group =>
                        string.Equals(group.FinalStatus, TaskStatusConstants.Approved, StringComparison.OrdinalIgnoreCase));
                    int rejectedTasks = groupedTasks.Count(group =>
                        string.Equals(group.FinalStatus, TaskStatusConstants.Rejected, StringComparison.OrdinalIgnoreCase));
                    int resolvedTasks = approvedTasks + rejectedTasks;
                    int firstPassApproved = groupedTasks.Count(group =>
                        string.Equals(group.FinalStatus, TaskStatusConstants.Approved, StringComparison.OrdinalIgnoreCase) &&
                        group.RejectCount == 0);
                    int annotatorSubmittedTasks = groupedTasks.Count(group => group.WasSubmitted);
                    int reworkCount = groupedTasks.Count(group => group.RejectCount > 0);
                    double finalAnnotatorAccuracy = resolvedTasks > 0
                        ? Math.Round((double)approvedTasks / resolvedTasks * 100, 2)
                        : 0;
                    double firstPassAccuracy = groupedTasks.Count > 0
                        ? Math.Round((double)firstPassApproved / groupedTasks.Count * 100, 2)
                        : 0;
                    double annotatorReworkRate = annotatorSubmittedTasks > 0
                        ? Math.Round((double)reworkCount / annotatorSubmittedTasks * 100, 2)
                        : 0;
                    double? averageQualityScore = userStat != null && userStat.TotalReviewedTasks > 0
                        ? userStat.AverageQualityScore
                        : null;

                    return new AnnotatorPerformance
                    {
                        AnnotatorId = annotatorId,
                        AnnotatorName = g.FirstOrDefault()?.AnnotatorName ?? "Unknown",
                        TasksAssigned = groupedTasks.Count,
                        TasksCompleted = approvedTasks,
                        TasksRejected = rejectedTasks,
                        AverageDurationSeconds = 0,
                        AverageQualityScore = averageQualityScore,
                        TotalCriticalErrors = userStat?.TotalCriticalErrors ?? 0,
                        AnnotatorAccuracy = finalAnnotatorAccuracy,
                        ResolvedTasks = resolvedTasks,
                        FirstPassCorrect = firstPassApproved,
                        ReworkCount = reworkCount,
                        TotalSubmittedTasks = annotatorSubmittedTasks,
                        FinalAccuracy = finalAnnotatorAccuracy,
                        FirstPassAccuracy = firstPassAccuracy,
                        ReworkRate = annotatorReworkRate
                    };
                }).ToList();

            var reviewerIds = allAssignments
                .Where(a => !string.IsNullOrEmpty(a.ReviewerId))
                .Select(a => a.ReviewerId!)
                .Concat(allReviewLogs
                    .Where(log => !string.IsNullOrWhiteSpace(log.ReviewerId))
                    .Select(log => log.ReviewerId!))
                .Distinct()
                .ToList();

            var reviewLogsByReviewer = allReviewLogs
                .Where(rl => !string.IsNullOrEmpty(rl.ReviewerId))
                .GroupBy(rl => rl.ReviewerId!)
                .ToDictionary(g => g.Key, g => g.Count());

            var reviewerDecisionStats = assignmentGroups
                .Where(group => !group.HasPendingDispute && IsResolvedAnnotatorStatus(group.FinalStatus))
                .SelectMany(group => group.LatestCycleReviewerDecisions.Select(decision => new
                {
                    decision.ReviewerId,
                    decision.ReviewerName,
                    decision.Verdict,
                    group.FinalStatus
                }))
                .Where(item => !string.IsNullOrWhiteSpace(item.ReviewerId))
                .GroupBy(item => item.ReviewerId)
                .ToDictionary(
                    group => group.Key,
                    group => new
                    {
                        ReviewerName = group.Select(item => item.ReviewerName)
                            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? group.Key,
                        TotalManagerDecisions = group.Count(),
                        CorrectDecisions = group.Count(item =>
                            (IsApprovedVerdict(item.Verdict) &&
                             string.Equals(item.FinalStatus, TaskStatusConstants.Approved, StringComparison.OrdinalIgnoreCase)) ||
                            (IsRejectedVerdict(item.Verdict) &&
                             string.Equals(item.FinalStatus, TaskStatusConstants.Rejected, StringComparison.OrdinalIgnoreCase)))
                    });

            stats.ReviewerPerformances = reviewerIds.Select(reviewerId =>
            {
                var reviewer = allAssignments.FirstOrDefault(a => a.ReviewerId == reviewerId)?.Reviewer;
                var reviewerStat = projectUserStats.FirstOrDefault(s => s.UserId == reviewerId);
                int statReviewsDone = reviewerStat?.TotalReviewsDone ?? 0;
                int logReviewsDone = reviewLogsByReviewer.ContainsKey(reviewerId) ? reviewLogsByReviewer[reviewerId] : 0;
                int totalReviewsDone = Math.Max(statReviewsDone, logReviewsDone);
                var decisionStats = reviewerDecisionStats.TryGetValue(reviewerId, out var reviewerDecision)
                    ? reviewerDecision
                    : null;
                int correctDecisions = decisionStats?.CorrectDecisions ?? 0;
                int totalMgrDecisions = decisionStats?.TotalManagerDecisions ?? 0;

                double? reviewerAccuracy = totalMgrDecisions > 0
                    ? Math.Round((double)correctDecisions / totalMgrDecisions * 100, 2)
                    : null;

                return new ReviewerPerformance
                {
                    ReviewerId = reviewerId,
                    ReviewerName = decisionStats?.ReviewerName ?? reviewer?.FullName ?? reviewer?.Email ?? "Unknown",
                    TotalReviews = totalReviewsDone,
                    CorrectDecisions = correctDecisions,
                    TotalManagerDecisions = totalMgrDecisions,
                    ReviewerAccuracy = reviewerAccuracy
                };
            }).ToList();

            var labelCounts = await _projectRepository.GetProjectLabelCountsAsync(projectId);

            stats.LabelDistributions = project.LabelClasses.Select(lc => new LabelDistribution
            {
                ClassName = lc.Name,
                Count = labelCounts.ContainsKey(lc.Id) ? labelCounts[lc.Id] : 0
            }).ToList();

            return stats;
        }

        public async Task<ManagerStatsResponse> GetManagerStatsAsync(string managerId)
        {
            var projects = await _projectRepository.GetProjectsByManagerIdAsync(managerId);
            var managedUsers = await _userRepository.FindAsync(u => u.ManagerId == managerId);
            var totalMembers = managedUsers.Count();

            return new ManagerStatsResponse
            {
                TotalProjects = projects.Count,
                ActiveProjects = projects.Count(p => p.Deadline >= DateTime.UtcNow),
                TotalDataItems = projects.Sum(p => p.DataItems.Count),
                TotalMembers = totalMembers
            };
        }

        public async Task<List<Core.DTOs.Responses.BucketResponse>> GetBucketsAsync(int projectId, string userId)
        {
            var dataItems = await _projectRepository.GetProjectDataItemsAsync(projectId);

            if (!dataItems.Any()) return new List<Core.DTOs.Responses.BucketResponse>();
            var buckets = dataItems
                .GroupBy(d => d.BucketId)
                .OrderBy(g => g.Key)
                .Select(g => new Core.DTOs.Responses.BucketResponse
                {
                    BucketId = g.Key,
                    Name = $"Lô số {g.Key}",
                    TotalItems = g.Count(),
                    CompletedItems = 0,
                    Status = "New"
                }).ToList();

            return buckets;
        }

        public async Task<List<AnnotatorProjectStatsResponse>> GetAssignedProjectsForUserAsync(string userId)
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null) return new List<AnnotatorProjectStatsResponse>();

            if (user.Role == UserRoles.Reviewer)
            {
                return await GetAssignedProjectsAsReviewerAsync(userId);
            }
            else if (user.Role == UserRoles.Annotator)
            {
                return await GetAssignedProjectsAsync(userId);
            }

            return new List<AnnotatorProjectStatsResponse>();
        }

        private async Task<List<AnnotatorProjectStatsResponse>> GetAssignedProjectsAsReviewerAsync(string reviewerId)
        {
            var assignedProjectIds = await _assignmentRepo.GetProjectIdsByReviewerAsync(reviewerId);
            if (!assignedProjectIds.Any()) return new List<AnnotatorProjectStatsResponse>();

            var projects = await _projectRepository.GetProjectsByIdsAsync(assignedProjectIds);
            var result = new List<AnnotatorProjectStatsResponse>();

            foreach (var p in projects)
            {
                var myAssignments = p.DataItems
                    .SelectMany(d => d.Assignments)
                    .Where(a => a.ReviewerId == reviewerId)
                    .ToList();

                var total = myAssignments.Count;
                var completed = myAssignments.Count(a => a.Status == TaskStatusConstants.Approved || a.Status == TaskStatusConstants.Rejected);
                var pending = myAssignments.Count(a => a.Status == TaskStatusConstants.Submitted);
                int approvedProjectItems = ProjectWorkflowStatusHelper.CountApprovedDataItems(p);
                bool allReviewerWorkResolved = total > 0 && total == completed;
                bool isAwaitingManagerConfirmation = allReviewerWorkResolved &&
                    ProjectWorkflowStatusHelper.IsAwaitingManagerConfirmation(p, p.DataItems.Count, approvedProjectItems);

                string status = ProjectStatusConstants.Active;
                if (string.Equals(p.Status, ProjectStatusConstants.Completed, StringComparison.OrdinalIgnoreCase))
                {
                    status = ProjectStatusConstants.Completed;
                }
                else if (DateTime.UtcNow > p.Deadline)
                {
                    status = ProjectStatusConstants.ExpiredDisplay;
                }
                else if (isAwaitingManagerConfirmation)
                {
                    status = ProjectStatusConstants.AwaitingManagerConfirmation;
                }

                result.Add(new AnnotatorProjectStatsResponse
                {
                    ProjectId = p.Id,
                    ProjectName = p.Name,
                    TotalImages = total,
                    CompletedImages = completed,
                    Status = status,
                    IsAwaitingManagerConfirmation = isAwaitingManagerConfirmation,
                    Deadline = p.Deadline,
                    PendingReview = pending
                });
            }

            return result;
        }

        public async Task<List<ProjectSummaryResponse>> GetAllProjectsForAdminAsync()
        {
            var projects = await _projectRepository.GetAllProjectsForAdminStatsAsync();

            return projects.Select(p =>
            {
                int totalItems = p.DataItems.Count;
                int approvedCount = ProjectWorkflowStatusHelper.CountApprovedDataItems(p);

                decimal progress = totalItems > 0 ? (decimal)approvedCount / totalItems * 100 : 0;
                bool hasStarted = totalItems > 0 && p.DataItems.Any(d => d.Status != TaskStatusConstants.New);
                string currentStatus = ProjectWorkflowStatusHelper.ResolveManagerFacingStatus(p, totalItems, approvedCount, hasStarted);

                return new ProjectSummaryResponse
                {
                    Id = p.Id,
                    Name = p.Name,
                    Deadline = p.Deadline,
                    TotalDataItems = totalItems,
                    Status = currentStatus,
                    Progress = progress,
                    TotalMembers = p.DataItems
                                    .SelectMany(d => d.Assignments)
                                    .SelectMany(a => new[] { a.AnnotatorId, a.ReviewerId })
                                    .Where(id => !string.IsNullOrEmpty(id))
                                    .Distinct()
                                    .Count(),
                    IsAwaitingManagerConfirmation = ProjectWorkflowStatusHelper.IsAwaitingManagerConfirmation(p, totalItems, approvedCount),
                    CanManagerConfirmCompletion = ProjectWorkflowStatusHelper.CanManagerConfirmCompletion(p, totalItems, approvedCount)
                };
            }).ToList();
        }

        public async Task AssignReviewersAsync(AssignReviewersRequest request)
        {
            var project = await _projectRepository.GetProjectForExportAsync(request.ProjectId);
            if (project == null)
                throw new Exception("Project not found.");

            var allAssignments = project.DataItems.SelectMany(d => d.Assignments).ToList();

            if (!allAssignments.Any())
                throw new Exception("No tasks found in this project. Please assign tasks to annotators first.");
            var validReviewers = (await _userRepository.FindAsync(u =>
                request.ReviewerIds.Contains(u.Id) &&
                u.Role == UserRoles.Reviewer)).ToList();

            if (validReviewers.Count != request.ReviewerIds.Count)
                throw new Exception("One or more provided reviewer IDs are invalid or lack the required role.");

            var groupedAssignments = allAssignments
                .GroupBy(a => new { a.DataItemId, a.AnnotatorId })
                .ToList();

            int createdAssignments = 0;
            int updatedAssignments = 0;

            foreach (var group in groupedAssignments)
            {
                var assignmentsForItem = group.ToList();
                var existingReviewerIds = assignmentsForItem
                    .Select(a => a.ReviewerId)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var reusableUnassigned = assignmentsForItem
                    .FirstOrDefault(a => string.IsNullOrWhiteSpace(a.ReviewerId));
                var template = SelectRepresentativeAnnotatorAssignment(assignmentsForItem);

                foreach (var reviewer in validReviewers)
                {
                    if (existingReviewerIds.Contains(reviewer.Id))
                    {
                        continue;
                    }

                    if (reusableUnassigned != null)
                    {
                        reusableUnassigned.ReviewerId = reviewer.Id;
                        _assignmentRepo.Update(reusableUnassigned);
                        existingReviewerIds.Add(reviewer.Id);
                        updatedAssignments++;
                        reusableUnassigned = null;
                        continue;
                    }

                    var clonedAssignment = new Assignment
                    {
                        ProjectId = template.ProjectId,
                        DataItemId = template.DataItemId,
                        AnnotatorId = template.AnnotatorId,
                        ReviewerId = reviewer.Id,
                        AssignedDate = template.AssignedDate,
                        SubmittedAt = template.SubmittedAt,
                        DurationSeconds = template.DurationSeconds,
                        RejectCount = 0,
                        IsEscalated = false,
                        Status = NormalizeReviewerAssignmentStatus(template),
                        Annotations = template.Annotations?
                            .OrderByDescending(a => a.CreatedAt)
                            .Take(1)
                            .Select(a => new Annotation
                            {
                                DataJSON = a.DataJSON,
                                CreatedAt = a.CreatedAt,
                                ClassId = a.ClassId
                            })
                            .ToList() ?? new List<Annotation>()
                    };

                    await _assignmentRepo.AddAsync(clonedAssignment);
                    existingReviewerIds.Add(reviewer.Id);
                    createdAssignments++;
                }
            }

            await _assignmentRepo.SaveChangesAsync();

            await _activityLogRepo.AddAsync(new ActivityLog
            {
                UserId = project.ManagerId,
                ActionType = "AssignReviewers",
                EntityName = "Project",
                EntityId = project.Id.ToString(),
                Description = $"Ensured {validReviewers.Count} reviewers are assigned to every annotator/data-item pair in project {project.Name}. Added {createdAssignments} reviewer assignments and updated {updatedAssignments} existing assignments.",
                Timestamp = DateTime.UtcNow
            });

            await _activityLogRepo.SaveChangesAsync();

            var manager = await _userRepository.GetByIdAsync(project.ManagerId);
            var totalTasksPerReviewer = createdAssignments + updatedAssignments;
            var annotatorCount = groupedAssignments
                .Select(g => g.Key.AnnotatorId)
                .Distinct()
                .Count();

            foreach (var reviewer in validReviewers)
            {
                await RunProjectSideEffectSafelyAsync(
                    project.ManagerId,
                    "AssignReviewersNotificationError",
                    project.Id.ToString(),
                    $"Reviewer assignment notification failed for reviewer {reviewer.Id} in project {project.Name}.",
                    () => _notification.SendNotificationAsync(
                        reviewer.Id,
                        $"You have been assigned as a Reviewer in project \"{project.Name}\". " +
                        $"You have tasks from {annotatorCount} annotator(s) waiting for your review.",
                        "Info"));

                if (manager != null)
                {
                    await RunProjectSideEffectSafelyAsync(
                        project.ManagerId,
                        "AssignReviewersEmailError",
                        project.Id.ToString(),
                        $"Reviewer assignment email failed for reviewer {reviewer.Id} in project {project.Name}.",
                        () => _workflowEmailService.SendReviewerAssignmentEmailAsync(
                            project,
                            manager,
                            reviewer,
                            totalTasksPerReviewer,
                            annotatorCount));
                }
            }
        }

        public async Task CompleteProjectAsync(int projectId, string managerId)
        {
            var project = await _projectRepository.GetProjectWithDetailsAsync(projectId);
            if (project == null) throw new Exception("Project not found.");
            if (project.ManagerId != managerId) throw new UnauthorizedAccessException("You are not the manager of this project.");

            var allAssignments = project.DataItems.SelectMany(d => d.Assignments).ToList();

            if (allAssignments.Any() && allAssignments.Any(a => a.Status != TaskStatusConstants.Approved))
            {
                throw new Exception("BR-MNG-33: Cannot complete project: All tasks must be Approved before completing. Manager approval required.");
            }

            if (project.Status != ProjectStatusConstants.Active)
            {
                throw new Exception("BR-MNG-33: Only Active projects can be completed. Please activate the project first.");
            }

            project.Status = ProjectStatusConstants.Completed;
            _projectRepository.Update(project);

            await _activityLogRepo.AddAsync(new ActivityLog
            {
                UserId = managerId,
                ActionType = "CompleteProject",
                EntityName = "Project",
                EntityId = project.Id.ToString(),
                Description = $"Manager approved and completed project: {project.Name}",
                Timestamp = DateTime.UtcNow
            });

            await _projectRepository.SaveChangesAsync();
            await _activityLogRepo.SaveChangesAsync();

            var manager = await _userRepository.GetByIdAsync(managerId) ?? new User
            {
                Id = managerId,
                FullName = "Project Manager",
                Email = string.Empty,
                Role = UserRoles.Manager
            };

            var participantIds = allAssignments
                .SelectMany(a => new[] { a.AnnotatorId, a.ReviewerId })
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var participants = (await _userRepository.GetAllAsync())
                .Where(user => participantIds.Contains(user.Id, StringComparer.OrdinalIgnoreCase))
                .ToList();

            var projectStats = (await _statsRepo.GetAllAsync())
                .Where(stat => stat.ProjectId == projectId)
                .ToList();

            await RunProjectSideEffectSafelyAsync(
                managerId,
                "CompleteProjectEmailError",
                project.Id.ToString(),
                $"Project completion emails failed for project {project.Name}.",
                () => _workflowEmailService.SendProjectCompletedEmailsAsync(
                    project,
                    manager,
                    participants,
                    allAssignments,
                    projectStats));
        }

        public async Task<ProjectCompletionReviewResponse> GetProjectCompletionReviewAsync(int projectId, string managerId)
        {
            var project = await _projectRepository.GetProjectWithStatsDataAsync(projectId);
            if (project == null) throw new Exception("Project not found.");
            if (project.ManagerId != managerId) throw new UnauthorizedAccessException("You are not the manager of this project.");

            int totalItems = project.DataItems.Count;
            int approvedItems = ProjectWorkflowStatusHelper.CountApprovedDataItems(project);
            bool hasStarted = totalItems > 0 && project.DataItems.Any(d => d.Status != TaskStatusConstants.New);
            string currentStatus = ProjectWorkflowStatusHelper.ResolveManagerFacingStatus(project, totalItems, approvedItems, hasStarted);
            bool isAwaitingManagerConfirmation = ProjectWorkflowStatusHelper.IsAwaitingManagerConfirmation(project, totalItems, approvedItems);
            bool isCompleted = string.Equals(project.Status, ProjectStatusConstants.Completed, StringComparison.OrdinalIgnoreCase);

            if (!isAwaitingManagerConfirmation && !isCompleted)
            {
                throw new InvalidOperationException("Completion review is only available when the project is waiting for manager confirmation or has already been completed.");
            }

            var items = project.DataItems
                .OrderBy(dataItem => dataItem.Id)
                .SelectMany(dataItem => dataItem.Assignments
                    .GroupBy(BuildAssignmentGroupKey)
                    .Where(group => string.Equals(
                        GetAggregatedAnnotatorStatus(group),
                        TaskStatusConstants.Approved,
                        StringComparison.OrdinalIgnoreCase))
                    .Select(group => BuildCompletionReviewItem(dataItem, group)))
                .OrderByDescending(item => item.RejectCount)
                .ThenByDescending(item => item.ReviewEventCount)
                .ThenBy(item => item.DataItemId)
                .ToList();

            return new ProjectCompletionReviewResponse
            {
                ProjectId = project.Id,
                ProjectName = project.Name,
                Status = currentStatus,
                IsAwaitingManagerConfirmation = isAwaitingManagerConfirmation,
                CanManagerConfirmCompletion = ProjectWorkflowStatusHelper.CanManagerConfirmCompletion(project, totalItems, approvedItems),
                TotalDataItems = totalItems,
                ApprovedItems = approvedItems,
                ReturnedItems = items.Count(item => item.RejectCount > 0),
                Items = items
            };
        }

        public async Task ReturnProjectItemForReworkAsync(int projectId, int assignmentId, string managerId, string comment)
        {
            var normalizedComment = comment?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedComment))
            {
                throw new InvalidOperationException("Please provide a clear reason before returning this image for rework.");
            }

            var project = await _projectRepository.GetProjectWithStatsDataAsync(projectId);
            if (project == null) throw new Exception("Project not found.");
            if (project.ManagerId != managerId) throw new UnauthorizedAccessException("You are not the manager of this project.");

            int totalItems = project.DataItems.Count;
            int approvedItems = ProjectWorkflowStatusHelper.CountApprovedDataItems(project);
            bool isAwaitingManagerConfirmation = ProjectWorkflowStatusHelper.IsAwaitingManagerConfirmation(project, totalItems, approvedItems);
            bool isCompleted = string.Equals(project.Status, ProjectStatusConstants.Completed, StringComparison.OrdinalIgnoreCase);

            if (!isAwaitingManagerConfirmation && !isCompleted)
            {
                throw new InvalidOperationException("This project is not currently in the manager completion-review phase.");
            }

            var targetDataItem = project.DataItems.FirstOrDefault(dataItem =>
                dataItem.Assignments.Any(assignment => assignment.Id == assignmentId));

            if (targetDataItem == null)
            {
                throw new InvalidOperationException("The selected image could not be found in this project.");
            }

            var targetGroup = targetDataItem.Assignments
                .GroupBy(BuildAssignmentGroupKey)
                .FirstOrDefault(group => group.Any(assignment => assignment.Id == assignmentId));

            if (targetGroup == null)
            {
                throw new InvalidOperationException("The selected image group could not be resolved.");
            }

            var groupedAssignments = targetGroup.ToList();
            if (!string.Equals(GetAggregatedAnnotatorStatus(groupedAssignments), TaskStatusConstants.Approved, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("This image is no longer in a manager-confirmable state.");
            }

            int nextRejectCount = groupedAssignments.Select(a => a.RejectCount).DefaultIfEmpty(0).Max() + 1;
            string annotatorId = groupedAssignments.First().AnnotatorId;
            var reviewerIds = groupedAssignments
                .Select(a => a.ReviewerId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            await _projectRepository.ExecuteInTransactionAsync(async () =>
            {
                project.Status = ProjectStatusConstants.Active;
                project.EndDate = null;
                _projectRepository.Update(project);

                targetDataItem.Status = TaskStatusConstants.Rejected;

                foreach (var assignment in groupedAssignments)
                {
                    assignment.Status = TaskStatusConstants.Rejected;
                    assignment.IsEscalated = false;
                    assignment.RejectCount = nextRejectCount;
                    assignment.ManagerDecision = "Reject";
                    assignment.ManagerComment = normalizedComment;
                    _assignmentRepo.Update(assignment);
                }

                await _activityLogRepo.AddAsync(new ActivityLog
                {
                    UserId = managerId,
                    ActionType = "ReturnProjectItemForRework",
                    EntityName = "Project",
                    EntityId = project.Id.ToString(),
                    Description = $"Manager returned data item #{targetDataItem.Id} in project {project.Name} for rework during completion review. Reason: {normalizedComment}",
                    Timestamp = DateTime.UtcNow
                });

                await _projectRepository.SaveChangesAsync();
                await _assignmentRepo.SaveChangesAsync();
                await _activityLogRepo.SaveChangesAsync();
            });

            await RunProjectSideEffectSafelyAsync(
                managerId,
                "CompletionReviewReturnAnnotatorNotificationError",
                project.Id.ToString(),
                $"Failed to notify annotator {annotatorId} about a completion-review return in project {project.Name}.",
                () => _notification.SendNotificationAsync(
                    annotatorId,
                    $"Manager returned image #{targetDataItem.Id} in project \"{project.Name}\" for rework during final confirmation. Reason: {normalizedComment}",
                    "Warning"));

            foreach (var reviewerId in reviewerIds)
            {
                await RunProjectSideEffectSafelyAsync(
                    managerId,
                    "CompletionReviewReturnReviewerNotificationError",
                    project.Id.ToString(),
                    $"Failed to notify reviewer {reviewerId} about a completion-review return in project {project.Name}.",
                    () => _notification.SendNotificationAsync(
                        reviewerId!,
                        $"Manager reopened image #{targetDataItem.Id} in project \"{project.Name}\" during final confirmation. The item will return to review after the annotator resubmits it.",
                        "Info"));
            }
        }

        private async Task RunProjectSideEffectSafelyAsync(
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
                await TryAddActivityLogAsync(new ActivityLog
                {
                    UserId = actorUserId,
                    ActionType = actionType,
                    EntityName = "Project",
                    EntityId = entityId,
                    Description = $"{description} {ex.Message}",
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        private async Task TryAddActivityLogAsync(ActivityLog activityLog)
        {
            try
            {
                await _activityLogRepo.AddAsync(activityLog);
                await _activityLogRepo.SaveChangesAsync();
            }
            catch
            {
            }
        }

        public async Task ActivateProjectAsync(int projectId, string managerId)
        {
            var project = await _projectRepository.GetProjectWithDetailsAsync(projectId);
            if (project == null) throw new Exception("Project not found.");
            if (project.ManagerId != managerId) throw new UnauthorizedAccessException("You are not the manager of this project.");

            if (project.Status != ProjectStatusConstants.Draft)
            {
                throw new Exception("Only Draft projects can be activated.");
            }

            if (!project.DataItems.Any())
            {
                throw new Exception("Cannot activate project without data items. Please upload data first.");
            }

            project.Status = ProjectStatusConstants.Active;
            _projectRepository.Update(project);

            await _activityLogRepo.AddAsync(new ActivityLog
            {
                UserId = managerId,
                ActionType = "ActivateProject",
                EntityName = "Project",
                EntityId = project.Id.ToString(),
                Description = $"Manager activated project: {project.Name}",
                Timestamp = DateTime.UtcNow
            });

            await _projectRepository.SaveChangesAsync();
            await _activityLogRepo.SaveChangesAsync();
        }

        public async Task ArchiveProjectAsync(int projectId, string managerId)
        {
            var project = await _projectRepository.GetProjectWithDetailsAsync(projectId);
            if (project == null) throw new Exception("Project not found.");
            if (project.ManagerId != managerId) throw new UnauthorizedAccessException("You are not the manager of this project.");

            if (project.Status == ProjectStatusConstants.Draft)
            {
                throw new Exception("Cannot archive a Draft project. Please complete or activate it first.");
            }

            project.Status = ProjectStatusConstants.Archived;
            _projectRepository.Update(project);

            await _activityLogRepo.AddAsync(new ActivityLog
            {
                UserId = managerId,
                ActionType = "ArchiveProject",
                EntityName = "Project",
                EntityId = project.Id.ToString(),
                Description = $"Manager archived project: {project.Name}",
                Timestamp = DateTime.UtcNow
            });

            await _projectRepository.SaveChangesAsync();
            await _activityLogRepo.SaveChangesAsync();
        }

        public async Task<byte[]> ExportProjectCsvAsync(int projectId, string userId)
        {
            var project = await _projectRepository.GetProjectForExportAsync(projectId);
            if (project == null) throw new Exception("Project not found.");
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null) throw new Exception("User not found");
            if (user.Role != UserRoles.Admin && project.ManagerId != userId)
                throw new Exception("Unauthorized to export this project.");

            var totalItems = project.DataItems.Count;
            if (totalItems == 0)
                throw new InvalidOperationException("No data items available in this project to export.");

            var completedItems = project.DataItems.Count(d => d.Status == TaskStatusConstants.Approved);
            if (completedItems < totalItems)
            {
                double progressPercent = Math.Round((double)completedItems / totalItems * 100, 2);
                throw new InvalidOperationException($"BR-MNG-12: Export is only allowed when all assignments are Approved. Current progress: {progressPercent}% ({completedItems}/{totalItems} items).");
            }

            var exportRecords = BuildApprovedExportRecords(project);
            if (!exportRecords.Any())
                throw new InvalidOperationException("No approved annotation data available for export.");

            var builder = new System.Text.StringBuilder();
            builder.AppendLine("ProjectId,ProjectName,DataItemId,FileName,StorageUrl,BucketId,UploadedAt,AnnotatorEmail,ReviewerEmail,ApprovedAt,AnnotationCount,AnnotationData");

            foreach (var record in exportRecords)
            {
                var annotationJson = JsonSerializer.Serialize(record.AnnotationData).Replace("\"", "\"\"");
                var approvedAt = record.ApprovedAt?.ToString("O") ?? string.Empty;
                builder.AppendLine(
                    $"{project.Id}," +
                    $"\"{project.Name.Replace("\"", "\"\"")}\"," +
                    $"{record.DataItemId}," +
                    $"\"{record.FileName.Replace("\"", "\"\"")}\"," +
                    $"\"{record.StorageUrl.Replace("\"", "\"\"")}\"," +
                    $"{record.BucketId}," +
                    $"{record.UploadedAt:O}," +
                    $"\"{record.AnnotatorEmail.Replace("\"", "\"\"")}\"," +
                    $"\"{record.ReviewerEmail.Replace("\"", "\"\"")}\"," +
                    $"{approvedAt}," +
                    $"{record.AnnotationCount}," +
                    $"\"{annotationJson}\"");
            }

            await _activityLogRepo.AddAsync(new ActivityLog
            {
                UserId = userId,
                ActionType = "ExportProjectCsv",
                EntityName = "Project",
                EntityId = projectId.ToString(),
                Description = $"Exported CSV data for project: {project.Name}",
                Timestamp = DateTime.UtcNow
            });
            await _activityLogRepo.SaveChangesAsync();

            return System.Text.Encoding.UTF8.GetBytes(builder.ToString());
        }

        public async Task RemoveUserFromProjectAsync(int projectId, string userId)
        {
            var project = await _projectRepository.GetProjectWithDetailsAsync(projectId);
            if (project == null) throw new Exception("Project not found.");

            var removedUser = await _userRepository.GetByIdAsync(userId);
            var allAssignments = project.DataItems.SelectMany(d => d.Assignments).ToList();
            var affectedAssignments = new List<Assignment>();

            if (removedUser?.Role == UserRoles.Reviewer)
            {
                affectedAssignments = allAssignments
                    .Where(a => a.ReviewerId == userId &&
                                (a.Status == TaskStatusConstants.Assigned ||
                                 a.Status == TaskStatusConstants.InProgress ||
                                 a.Status == TaskStatusConstants.Submitted))
                    .ToList();
            }
            else
            {
                affectedAssignments = allAssignments
                    .Where(a => a.AnnotatorId == userId &&
                                (a.Status == TaskStatusConstants.Assigned ||
                                 a.Status == TaskStatusConstants.InProgress))
                    .ToList();
            }

            if (affectedAssignments.Any())
            {
                foreach (var assignment in affectedAssignments)
                {
                    if (removedUser?.Role == UserRoles.Reviewer)
                    {
                        assignment.ReviewerId = null;
                        _assignmentRepo.Update(assignment);
                    }
                    else
                    {
                        _assignmentRepo.Delete(assignment);

                        var dataItem = project.DataItems.FirstOrDefault(d => d.Id == assignment.DataItemId);
                        if (dataItem != null && dataItem.Assignments.Count <= 1)
                        {
                            dataItem.Status = TaskStatusConstants.New;
                        }
                    }
                }

                await _assignmentRepo.SaveChangesAsync();
                await _projectRepository.SaveChangesAsync();
            }

            await _activityLogRepo.AddAsync(new ActivityLog
            {
                UserId = project.ManagerId,
                ActionType = "RemoveUser",
                EntityName = "Project",
                EntityId = projectId.ToString(),
                Description = $"Removed user {userId} from project and updated {affectedAssignments.Count} pending assignments.",
                Timestamp = DateTime.UtcNow
            });
            await _activityLogRepo.SaveChangesAsync();

            if (removedUser != null)
            {
                await _notification.SendNotificationAsync(
                    userId,
                    $"You have been removed from project \"{project.Name}\" by the manager. {affectedAssignments.Count} pending assignment(s) were revoked.",
                    "Warning"
                );
            }
        }

        public async Task ToggleUserLockAsync(int projectId, string userId, bool lockStatus, string managerId)
        {
            var project = await _projectRepository.GetProjectWithDetailsAsync(projectId);
            if (project == null) throw new Exception("Project not found");
            if (project.ManagerId != managerId) throw new UnauthorizedAccessException("Only the project manager can toggle user lock status.");

            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null) throw new Exception("User not found");

            var allAssignments = project.DataItems.SelectMany(d => d.Assignments).ToList();
            bool isReviewer = user.Role == UserRoles.Reviewer;
            var userAssignments = isReviewer
                ? allAssignments.Where(a => a.ReviewerId == userId).ToList()
                : allAssignments.Where(a => a.AnnotatorId == userId).ToList();
            var userStats = await _statsRepo.FindAsync(s => s.UserId == userId && s.ProjectId == projectId);

            if (lockStatus)
            {
                foreach (var assignment in userAssignments)
                {
                    if (isReviewer)
                    {
                        if (assignment.Status == TaskStatusConstants.Assigned ||
                            assignment.Status == TaskStatusConstants.InProgress ||
                            assignment.Status == TaskStatusConstants.Submitted)
                        {
                            assignment.ReviewerId = null;
                            _assignmentRepo.Update(assignment);
                        }
                    }
                    else if (assignment.Status == TaskStatusConstants.Assigned ||
                             assignment.Status == TaskStatusConstants.InProgress)
                    {
                        _assignmentRepo.Delete(assignment);
                    }
                }

                foreach (var stat in userStats)
                {
                    stat.IsLocked = true;
                    stat.Date = DateTime.UtcNow;
                }

                await _activityLogRepo.AddAsync(new ActivityLog
                {
                    UserId = managerId,
                    ActionType = "LockUser",
                    EntityName = "Project",
                    EntityId = projectId.ToString(),
                    Description = $"Locked {user.Role} {userId} in project {project.Name}. Updated {userAssignments.Count} assignments.",
                    Timestamp = DateTime.UtcNow
                });

                await _notification.SendNotificationAsync(
                    userId,
                    $"Your access to project \"{project.Name}\" has been locked by the manager. You are no longer allowed to work on this project.",
                    "Warning");
            }
            else
            {
                foreach (var stat in userStats)
                {
                    stat.IsLocked = false;
                    stat.Date = DateTime.UtcNow;
                }

                await _activityLogRepo.AddAsync(new ActivityLog
                {
                    UserId = managerId,
                    ActionType = "UnlockUser",
                    EntityName = "Project",
                    EntityId = projectId.ToString(),
                    Description = $"Unlocked {user.Role} {userId} in project {project.Name}. Access restored.",
                    Timestamp = DateTime.UtcNow
                });

                await _notification.SendNotificationAsync(
                    userId,
                    $"Your access to project \"{project.Name}\" has been restored by the manager.",
                    "Info");
            }

            await _statsRepo.SaveChangesAsync();
            await _assignmentRepo.SaveChangesAsync();
            await _activityLogRepo.SaveChangesAsync();
        }
    }
}

