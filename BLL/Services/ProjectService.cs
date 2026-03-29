using BLL.Interfaces;
using DAL.Interfaces;
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
        private readonly IAppNotificationService _notification;

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

        public ProjectService(
            IProjectRepository projectRepository,
            IUserRepository userRepository,
            IRepository<UserProjectStat> statsRepo,
            IAssignmentRepository assignmentRepo,
            IActivityLogRepository activityLogRepo,
            IRepository<ProjectFlag> flagRepo,
            IAppNotificationService notification)
        {
            _projectRepository = projectRepository;
            _userRepository = userRepository;
            _statsRepo = statsRepo;
            _assignmentRepo = assignmentRepo;
            _activityLogRepo = activityLogRepo;
            _flagRepo = flagRepo;
            _notification = notification;
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

        // ĐÃ FIX 3-LAYER: Nhận Stream thay vì IFormFile
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
                ProcessedItems = 0
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
                var total = myAssignments.Count;
                var completed = myAssignments.Count(a => a.Status == TaskStatusConstants.Submitted || a.Status == TaskStatusConstants.Approved);
                var nextTask = myAssignments
                    .OrderByDescending(a => a.Status == TaskStatusConstants.InProgress)
                    .ThenByDescending(a => a.Status == TaskStatusConstants.Rejected)
                    .ThenByDescending(a => a.Status == TaskStatusConstants.Assigned)
                    .FirstOrDefault(a => a.Status == TaskStatusConstants.InProgress || a.Status == TaskStatusConstants.Rejected || a.Status == TaskStatusConstants.Assigned);

                string status = "Active";
                if (DateTime.UtcNow > p.Deadline) status = "Expired";
                else if (total > 0 && total == completed) status = "Completed";

                result.Add(new AnnotatorProjectStatsResponse
                {
                    ProjectId = p.Id,
                    ProjectName = p.Name,
                    TotalImages = total,
                    CompletedImages = completed,
                    Status = status,
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
            int done = project.DataItems.Count(d => d.Status == TaskStatusConstants.Approved);
            int progressPercent = (total > 0) ? (int)((double)done / total * 100) : 0;

            var annotators = allAssignments
                .Where(a => a.Annotator != null)
                .GroupBy(a => a.AnnotatorId)
                .Select(g => new MemberResponse
                {
                    Id = g.Key,
                    FullName = g.First().Annotator.FullName ?? g.First().Annotator.Email,
                    Email = g.First().Annotator.Email,
                    Role = g.First().Annotator.Role,
                    TasksAssigned = g.Count(),
                    TasksCompleted = g.Count(a => a.Status == TaskStatusConstants.Approved),
                    Progress = g.Count() > 0
                        ? Math.Round((decimal)g.Count(a => a.Status == TaskStatusConstants.Approved) / g.Count() * 100, 2)
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
                Members = members
            };
        }

        public async Task<List<ProjectSummaryResponse>> GetProjectsByManagerAsync(string managerId)
        {
            var projects = await _projectRepository.GetProjectsByManagerIdAsync(managerId);

            return projects.Select(p =>
            {
                int totalItems = p.DataItems.Count;
                int approvedCount = p.DataItems.Count(d =>
                    d.Status == TaskStatusConstants.Approved ||
                    (d.Assignments != null && d.Assignments.Any(a => a.Status == TaskStatusConstants.Approved))
                );

                decimal progress = totalItems > 0 ? (decimal)approvedCount / totalItems * 100 : 0;

                string currentStatus = "New";
                if (totalItems > 0 && approvedCount == totalItems)
                {
                    currentStatus = "Completed";
                }
                else if (DateTime.UtcNow > p.Deadline)
                {
                    currentStatus = "Expired";
                }
                else if (totalItems > 0 && p.DataItems.Any(d => d.Status != TaskStatusConstants.New))
                {
                    currentStatus = "InProgress";
                }

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
                                    .Count()
                };
            }).ToList();
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
            var completedItems = project.DataItems.Count(d => d.Status == TaskStatusConstants.Approved);
            if (totalItems > 0 && completedItems < totalItems)
            {
                double currentProgress = Math.Round((double)completedItems / totalItems * 100, 2);
                throw new InvalidOperationException($"BR-MNG-12: Export is only allowed when all assignments are Approved. Current progress: {currentProgress}% ({completedItems}/{totalItems} items).");
            }

            var dataItems = project.DataItems
                    .Select(d => new
                    {
                        DataItemId = d.Id,
                        StorageUrl = d.StorageUrl,
                        ItemStatus = d.Status,
                        Assignments = d.Assignments.Select(a => new
                        {
                            AssignmentId = a.Id,
                            Annotator = a.Annotator?.Email ?? "Unknown",
                            Reviewer = a.Reviewer?.Email ?? "None",
                            TaskStatus = a.Status,
                            AssignedDate = a.AssignedDate,
                            SubmittedAt = a.SubmittedAt,
                            ReviewComment = a.ReviewLogs?
                                .OrderByDescending(r => r.CreatedAt)
                                .FirstOrDefault()?.Comment,
                            Annotation = SafeSerializeAnnotations(a.Annotations)
                        }).ToList()
                    })
                    .ToList();

            double progressPercent = totalItems > 0 ? Math.Round((double)completedItems / totalItems * 100, 2) : 0;
            string currentStatus = DateTime.UtcNow > project.Deadline ? "Expired" : (totalItems > 0 && totalItems == completedItems ? "Completed" : "Active");

            var projectMembers = project.DataItems
                .SelectMany(d => d.Assignments)
                .Where(a => a.Annotator != null)
                .Select(a => new { UserId = a.AnnotatorId, Role = a.Annotator.Role, Email = a.Annotator.Email })
                .Distinct()
                .ToList();

            var exportData = new
            {
                ProjectId = project.Id,
                ProjectName = project.Name,
                ExportedAt = DateTime.UtcNow,
                Description = project.Description,
                Status = currentStatus,
                Deadline = project.Deadline,
                Progress = progressPercent,
                TotalDataItems = totalItems,
                Labels = project.LabelClasses.Select(l => new { l.Id, l.Name, l.Color }),
                Members = projectMembers,
                TotalImages = dataItems.Count,
                Data = dataItems
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
            var totalReviewed = allReviewLogs.Count;
            var totalRejectedLogs = allReviewLogs.Count(l => l.Verdict == "Rejected" || l.Verdict == "Reject");

            var totalFirstPassCorrect = projectUserStats
                .Where(s => s.TotalFirstPassCorrect > 0)
                .Sum(s => s.TotalFirstPassCorrect);
            var totalItems = project.DataItems.Count;
            double projectAccuracy = totalItems > 0
                ? Math.Round((double)totalFirstPassCorrect / totalItems * 100, 2)
                : 0;

            var stats = new ProjectStatisticsResponse
            {
                ProjectId = project.Id,
                ProjectName = project.Name,
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
                    .Where(l => (l.Verdict == "Rejected" || l.Verdict == "Reject") && !string.IsNullOrEmpty(l.ErrorCategory))
                    .GroupBy(l => l.ErrorCategory!)
                    .ToDictionary(g => g.Key, g => g.Count()),
                ProjectAccuracy = projectAccuracy
            };

            if (stats.TotalItems > 0)
            {
                stats.ProgressPercentage = Math.Round((decimal)stats.CompletedItems / stats.TotalItems * 100, 2);
            }

            stats.AnnotatorPerformances = allAssignments
                .GroupBy(a => a.AnnotatorId)
                .Select(g =>
                {
                    var annotatorId = g.Key;
                    var userStat = projectUserStats.FirstOrDefault(s => s.UserId == annotatorId);
                    double annotatorAccuracy = 0;
                    if (userStat != null && userStat.TotalManagerDecisions > 0)
                    {
                        annotatorAccuracy = Math.Round((double)userStat.TotalCorrectByManager / userStat.TotalManagerDecisions * 100, 2);
                    }
                    else
                    {
                        var assignmentIds = g.Select(a => a.Id).ToHashSet();
                        var logsForAnnotator = allReviewLogs.Where(rl => assignmentIds.Contains(rl.AssignmentId)).ToList();
                        if (logsForAnnotator.Count > 0)
                        {
                            var approvedLogs = logsForAnnotator.Count(rl => rl.Verdict == "Approved" || rl.Verdict == "Approve");
                            annotatorAccuracy = Math.Round((double)approvedLogs / logsForAnnotator.Count * 100, 2);
                        }
                    }

                    return new AnnotatorPerformance
                    {
                        AnnotatorId = annotatorId,
                        AnnotatorName = g.FirstOrDefault()?.Annotator?.FullName ?? "Unknown",
                        TasksAssigned = g.Count(),
                        TasksCompleted = g.Count(a => a.Status == TaskStatusConstants.Approved),
                        TasksRejected = g.Count(a => a.Status == TaskStatusConstants.Rejected),
                        AverageDurationSeconds = 0,
                        AverageQualityScore = userStat?.AverageQualityScore ?? 100,
                        TotalCriticalErrors = userStat?.TotalCriticalErrors ?? 0,
                        AnnotatorAccuracy = annotatorAccuracy
                    };
                }).ToList();

            var reviewerIds = allAssignments
                .Where(a => !string.IsNullOrEmpty(a.ReviewerId))
                .Select(a => a.ReviewerId!)
                .Distinct()
                .ToList();

            var reviewLogsByReviewer = allReviewLogs
                .Where(rl => !string.IsNullOrEmpty(rl.ReviewerId))
                .GroupBy(rl => rl.ReviewerId!)
                .ToDictionary(g => g.Key, g => g.Count());

            stats.ReviewerPerformances = reviewerIds.Select(reviewerId =>
            {
                var reviewerStat = projectUserStats.FirstOrDefault(s => s.UserId == reviewerId);
                var reviewer = allAssignments.FirstOrDefault(a => a.ReviewerId == reviewerId)?.Reviewer;
                int statReviewsDone = reviewerStat?.TotalReviewsDone ?? 0;
                int logReviewsDone = reviewLogsByReviewer.ContainsKey(reviewerId) ? reviewLogsByReviewer[reviewerId] : 0;
                int totalReviewsDone = Math.Max(statReviewsDone, logReviewsDone);
                int correctDecisions = reviewerStat?.TotalReviewerCorrectByManager ?? 0;
                int totalMgrDecisions = reviewerStat?.TotalReviewerManagerDecisions ?? 0;
                double reviewerAccuracy = totalMgrDecisions > 0
                    ? Math.Round((double)correctDecisions / totalMgrDecisions * 100, 2)
                    : 0;

                return new ReviewerPerformance
                {
                    ReviewerId = reviewerId,
                    ReviewerName = reviewer?.FullName ?? reviewer?.Email ?? "Unknown",
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

                string status = "Active";
                if (DateTime.UtcNow > p.Deadline) status = "Expired";
                else if (total > 0 && total == completed) status = "Completed";

                result.Add(new AnnotatorProjectStatsResponse
                {
                    ProjectId = p.Id,
                    ProjectName = p.Name,
                    TotalImages = total,
                    CompletedImages = completed,
                    Status = status,
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
                int approvedCount = p.DataItems.Count(d =>
                    d.Status == TaskStatusConstants.Approved ||
                    (d.Assignments != null && d.Assignments.Any(a => a.Status == TaskStatusConstants.Approved))
                );

                decimal progress = totalItems > 0 ? (decimal)approvedCount / totalItems * 100 : 0;

                string currentStatus = "New";

                if (totalItems > 0 && approvedCount == totalItems)
                {
                    currentStatus = "Completed";
                }
                else if (DateTime.UtcNow > p.Deadline)
                {
                    currentStatus = "Expired";
                }
                else if (totalItems > 0 && p.DataItems.Any(d => d.Status != TaskStatusConstants.New))
                {
                    currentStatus = "InProgress";
                }

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
                                    .Count()
                };
            }).ToList();
        }

        public async Task AssignReviewersAsync(AssignReviewersRequest request)
        {
            var project = await _projectRepository.GetProjectWithDetailsAsync(request.ProjectId);
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

            int totalReviewers = validReviewers.Count;
            int index = 0;

            foreach (var assignment in allAssignments)
            {
                assignment.ReviewerId = validReviewers[index % totalReviewers].Id;
                _assignmentRepo.Update(assignment);
                index++;
            }

            await _assignmentRepo.SaveChangesAsync();

            await _activityLogRepo.AddAsync(new ActivityLog
            {
                UserId = project.ManagerId,
                ActionType = "AssignReviewers",
                EntityName = "Project",
                EntityId = project.Id.ToString(),
                Description = $"Assigned {totalReviewers} reviewers to project {project.Name} across {allAssignments.Count} tasks.",
                Timestamp = DateTime.UtcNow
            });

            await _activityLogRepo.SaveChangesAsync();
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
            var project = await _projectRepository.GetProjectWithDetailsAsync(projectId);
            if (project == null) throw new Exception("Project not found.");
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null) throw new Exception("User not found");
            if (user.Role != UserRoles.Admin && project.ManagerId != userId)
                throw new Exception("Unauthorized to export this project.");

            var totalItems = project.DataItems.Count;
            var completedItems = project.DataItems.Count(d => d.Status == TaskStatusConstants.Approved);
            if (totalItems > 0 && completedItems < totalItems)
            {
                double progressPercent = Math.Round((double)completedItems / totalItems * 100, 2);
                throw new InvalidOperationException($"BR-MNG-12: Export is only allowed when all assignments are Approved. Current progress: {progressPercent}% ({completedItems}/{totalItems} items).");
            }

            var approvedTasks = project.DataItems
                .SelectMany(d => d.Assignments)
                .Where(a => a.Status == TaskStatusConstants.Approved)
                .ToList();

            if (!approvedTasks.Any())
                throw new Exception("No approved data available for export.");

            var builder = new System.Text.StringBuilder();
            builder.AppendLine("AssignmentId,DataItemId,AnnotatorId,AnnotationData,ApprovedDate");

            foreach (var task in approvedTasks)
            {
                var annotationJson = System.Text.Json.JsonSerializer.Serialize(task.Annotations);

                var safeJson = annotationJson.Replace("\"", "\"\"");

                builder.AppendLine($"{task.Id},{task.DataItemId},{task.AnnotatorId},\"{safeJson}\",{task.SubmittedAt}");
            }

            return System.Text.Encoding.UTF8.GetBytes(builder.ToString());
        }

        public async Task RemoveUserFromProjectAsync(int projectId, string userId)
        {
            var project = await _projectRepository.GetProjectWithDetailsAsync(projectId);
            if (project == null) throw new Exception("Project not found.");

            var pendingAssignments = project.DataItems
                .SelectMany(d => d.Assignments)
                .Where(a => a.AnnotatorId == userId && a.Status == TaskStatusConstants.Assigned)
                .ToList();

            if (pendingAssignments.Any())
            {
                foreach (var assignment in pendingAssignments)
                {
                    _assignmentRepo.Delete(assignment);

                    var dataItem = project.DataItems.FirstOrDefault(d => d.Id == assignment.DataItemId);
                    if (dataItem != null && dataItem.Assignments.Count <= 1)
                    {
                        dataItem.Status = TaskStatusConstants.New;
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
                Description = $"Removed user {userId} from project and revoked {pendingAssignments.Count} pending tasks.",
                Timestamp = DateTime.UtcNow
            });
            await _activityLogRepo.SaveChangesAsync();

            var removedUser = await _userRepository.GetByIdAsync(userId);
            if (removedUser != null && removedUser.Role == UserRoles.Annotator)
            {
                await _notification.SendNotificationAsync(
                    userId,
                    $"You have been removed from project \"{project.Name}\". {pendingAssignments.Count} pending task(s) have been revoked.",
                    "Warning"
                );
            }
        }

        public async Task ToggleUserLockAsync(int projectId, string userId, bool lockStatus, string managerId)
        {
            var project = await _projectRepository.GetProjectWithDetailsAsync(projectId);
            if (project == null) throw new Exception("Project not found");
            if (project.ManagerId != managerId) throw new UnauthorizedAccessException("Only the project manager can toggle user lock status.");

            var allAssignments = project.DataItems.SelectMany(d => d.Assignments).ToList();
            var userAssignments = allAssignments.Where(a => a.AnnotatorId == userId).ToList();
            var userStats = await _statsRepo.FindAsync(s => s.UserId == userId && s.ProjectId == projectId);

            if (lockStatus)
            {
                foreach (var assignment in userAssignments)
                {
                    if (assignment.Status == TaskStatusConstants.Assigned ||
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
                    Description = $"Locked reviewer {userId} in project {project.Name}. Revoked {userAssignments.Count} pending assignments.",
                    Timestamp = DateTime.UtcNow
                });
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
                    Description = $"Unlocked reviewer {userId} in project {project.Name}. Reviewer access restored.",
                    Timestamp = DateTime.UtcNow
                });
            }

            await _statsRepo.SaveChangesAsync();
            await _assignmentRepo.SaveChangesAsync();
            await _activityLogRepo.SaveChangesAsync();
        }
    }
}