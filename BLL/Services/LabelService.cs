using BLL.Interfaces;
using Core.DTOs.Requests;
using Core.DTOs.Responses;
using Core.Interfaces;
using Core.Entities;
using Core.Constants;
using System.Text.Json;

namespace BLL.Services
{
    public class LabelService : ILabelService
    {
        private readonly ILabelRepository _labelRepo;
        private readonly IAssignmentRepository _assignmentRepo;
        private readonly IActivityLogService _logService;
        private readonly IRepository<Annotation> _annotationRepo;
        private readonly IProjectRepository _projectRepo;
        private readonly IAppNotificationService _notification;

        public LabelService(
            ILabelRepository labelRepo,
            IAssignmentRepository assignmentRepo,
            IActivityLogService logService,
            IRepository<Annotation> annotationRepo,
            IProjectRepository projectRepo,
            IAppNotificationService notification)
        {
            _labelRepo = labelRepo;
            _assignmentRepo = assignmentRepo;
            _logService = logService;
            _annotationRepo = annotationRepo;
            _projectRepo = projectRepo;
            _notification = notification;
        }

        public async Task<List<LabelResponse>> GetLabelsByProjectIdAsync(int projectId)
        {
            var labels = await _labelRepo.FindAsync(l => l.ProjectId == projectId);
            return labels.Select(l => new LabelResponse
            {
                Id = l.Id,
                Name = l.Name,
                Color = l.Color,
                GuideLine = l.GuideLine,
                ExampleImageUrl = l.ExampleImageUrl,
                IsDefault = l.IsDefault,
                Checklist = !string.IsNullOrEmpty(l.DefaultChecklist)
                            ? JsonSerializer.Deserialize<List<string>>(l.DefaultChecklist) ?? new List<string>()
                            : new List<string>()
            }).ToList();
        }

        public async Task<LabelUsageResponse> CheckLabelUsageAsync(int labelId)
        {
            var annotations = await _annotationRepo.FindAsync(a => a.ClassId == labelId);
            var count = annotations.Count();

            var label = await _labelRepo.GetByIdAsync(labelId);

            return new LabelUsageResponse
            {
                LabelId = labelId,
                LabelName = label?.Name ?? "Unknown",
                UsageCount = count,
                WarningMessage = count > 0
                    ? $"This label has already been used in {count} annotation(s). It can still be edited, but it cannot be deleted after annotators start drawing."
                    : "This label has not been used yet. It can be deleted safely.",
                AffectedTasksCount = count,
                RequiresConfirmation = count > 0
            };
        }

        public async Task<LabelResponse> CreateLabelAsync(string userId, CreateLabelRequest request)
        {
            if (await _labelRepo.ExistsInProjectAsync(request.ProjectId, request.Name))
                throw new InvalidOperationException("Label name already exists in this project.");

            var project = await _projectRepo.GetByIdAsync(request.ProjectId);
            var guidelineVersion = project?.GuidelineVersion ?? "1.0";

            var label = new LabelClass
            {
                ProjectId = request.ProjectId,
                Name = request.Name,
                Color = request.Color,
                GuideLine = request.GuideLine,
                ExampleImageUrl = request.ExampleImageUrl,
                IsDefault = request.IsDefault,
                DefaultChecklist = (request.Checklist != null && request.Checklist.Any())
                                    ? JsonSerializer.Serialize(request.Checklist)
                                    : "[]",

                Version = guidelineVersion,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _labelRepo.AddAsync(label);
            await _labelRepo.SaveChangesAsync();

            await _logService.LogActionAsync(
                userId,
                "CreateLabel",
                "LabelClass",
                label.Id.ToString(),
                $"Created label '{label.Name}' for Project {label.ProjectId} (v{label.Version})"
            );

            return new LabelResponse
            {
                Id = label.Id,
                Name = label.Name,
                Color = label.Color,
                GuideLine = label.GuideLine,
                ExampleImageUrl = label.ExampleImageUrl,
                IsDefault = label.IsDefault,
                Checklist = request.Checklist ?? new List<string>()
            };
        }

        private static string IncrementVersion(string version)
        {
            var parts = version.Split('.');
            if (parts.Length == 2 && int.TryParse(parts[1], out int minor))
            {
                return $"{parts[0]}.{minor + 1}";
            }
            return $"{version}.1";
        }

        private static string SerializeChecklist(List<string>? checklist)
        {
            return checklist != null && checklist.Any()
                ? JsonSerializer.Serialize(checklist)
                : "[]";
        }

        private static string NormalizeText(string? value)
        {
            return value?.Trim() ?? string.Empty;
        }

        private static bool IsCriticalLabelChange(LabelClass label, UpdateLabelRequest request)
        {
            var requestedChecklist = SerializeChecklist(request.Checklist);
            var currentChecklist = string.IsNullOrWhiteSpace(label.DefaultChecklist)
                ? "[]"
                : label.DefaultChecklist;

            return !string.Equals(label.Name, request.Name, StringComparison.Ordinal) ||
                   !string.Equals(NormalizeText(label.GuideLine), NormalizeText(request.GuideLine), StringComparison.Ordinal) ||
                   !string.Equals(NormalizeText(label.ExampleImageUrl), NormalizeText(request.ExampleImageUrl), StringComparison.Ordinal) ||
                   !string.Equals(NormalizeText(currentChecklist), NormalizeText(requestedChecklist), StringComparison.Ordinal) ||
                   label.IsDefault != request.IsDefault;
        }

        private static Annotation? GetLatestAnnotationUsingLabel(IEnumerable<Assignment> assignments, int labelId)
        {
            return assignments
                .Select(assignment => assignment.Annotations?
                    .OrderByDescending(annotation => annotation.CreatedAt)
                    .FirstOrDefault())
                .Where(annotation => annotation != null)
                .Select(annotation => annotation!)
                .OrderByDescending(annotation => annotation.CreatedAt)
                .FirstOrDefault(annotation =>
                {
                    var parsedPayload = AnnotationPayloadHelper.Parse(annotation.DataJSON);
                    return AnnotationPayloadHelper.UsesLabel(parsedPayload, labelId);
                });
        }

        private static string BuildLabelRelabelReason(string oldName, string newName)
        {
            if (string.Equals(oldName, newName, StringComparison.Ordinal))
            {
                return $"Manager updated label \"{newName}\". Annotations for this edited label were removed; other existing annotations stay visible as reference.";
            }

            return $"Manager updated label \"{oldName}\" to \"{newName}\". Annotations for this edited label were removed; other existing annotations stay visible as reference.";
        }

        private async Task TryLogActionAsync(string userId, string actionType, string entityName, string entityId, string description)
        {
            try
            {
                await _logService.LogActionAsync(userId, actionType, entityName, entityId, description);
            }
            catch
            {
            }
        }

        private async Task TryNotifyUserAsync(string userId, string message, string type = "Info")
        {
            try
            {
                await _notification.SendNotificationAsync(userId, message, type);
            }
            catch
            {
            }
        }

        private static LabelResponse BuildLabelResponse(LabelClass label)
        {
            return new LabelResponse
            {
                Id = label.Id,
                Name = label.Name,
                Color = label.Color,
                GuideLine = label.GuideLine,
                ExampleImageUrl = label.ExampleImageUrl,
                IsDefault = label.IsDefault,
                Checklist = !string.IsNullOrEmpty(label.DefaultChecklist)
                    ? JsonSerializer.Deserialize<List<string>>(label.DefaultChecklist, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<string>()
                    : new List<string>()
            };
        }

        private async Task<(int AffectedTaskGroups, bool ReopenedProject, HashSet<string> AffectedAnnotatorIds, string ProjectName)> ReopenAffectedAssignmentsForEditedLabelAsync(
            LabelClass label,
            string oldName)
        {
            var assignments = await _assignmentRepo.GetAssignmentsByProjectWithDetailsAsync(label.ProjectId);
            if (!assignments.Any())
            {
                return (0, false, new HashSet<string>(StringComparer.OrdinalIgnoreCase), string.Empty);
            }

            var relabelReason = BuildLabelRelabelReason(oldName, label.Name);
            var affectedDataItemIds = new HashSet<int>();
            var affectedAnnotatorIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int affectedTaskGroups = 0;

            foreach (var assignmentGroup in assignments
                .GroupBy(assignment => new { assignment.ProjectId, assignment.DataItemId, assignment.AnnotatorId }))
            {
                var groupedAssignments = assignmentGroup.ToList();
                var latestRelevantAnnotation = GetLatestAnnotationUsingLabel(groupedAssignments, label.Id);
                if (latestRelevantAnnotation == null)
                {
                    continue;
                }

                var relabelPayload = AnnotationPayloadHelper.CreateRelabelPayload(
                    AnnotationPayloadHelper.Parse(latestRelevantAnnotation.DataJSON),
                    label.Id,
                    relabelReason);
                var relabelDataJson = AnnotationPayloadHelper.Serialize(relabelPayload);

                foreach (var assignment in groupedAssignments)
                {
                    await _annotationRepo.AddAsync(new Annotation
                    {
                        AssignmentId = assignment.Id,
                        DataJSON = relabelDataJson,
                        CreatedAt = DateTime.UtcNow,
                        ClassId = label.Id
                    });

                    assignment.Status = TaskStatusConstants.Rejected;
                    assignment.ManagerDecision = null;
                    assignment.ManagerComment = relabelReason;
                    _assignmentRepo.Update(assignment);
                }

                affectedTaskGroups++;
                affectedDataItemIds.Add(assignmentGroup.Key.DataItemId);
                if (!string.IsNullOrWhiteSpace(assignmentGroup.Key.AnnotatorId))
                {
                    affectedAnnotatorIds.Add(assignmentGroup.Key.AnnotatorId);
                }
            }

            if (affectedTaskGroups == 0)
            {
                return (0, false, affectedAnnotatorIds, string.Empty);
            }

            var project = await _projectRepo.GetByIdAsync(label.ProjectId);
            bool reopenedProject = false;
            var projectName = project?.Name ?? $"Project #{label.ProjectId}";
            if (project != null)
            {
                if (string.Equals(project.Status, ProjectStatusConstants.Completed, StringComparison.OrdinalIgnoreCase))
                {
                    project.Status = ProjectStatusConstants.Active;
                    reopenedProject = true;
                }

                _projectRepo.Update(project);
            }

            foreach (var assignment in assignments.Where(item => affectedDataItemIds.Contains(item.DataItemId)))
            {
                if (assignment.DataItem == null)
                {
                    continue;
                }

                assignment.DataItem.Status = TaskStatusConstants.Assigned;
            }

            await _annotationRepo.SaveChangesAsync();
            await _assignmentRepo.SaveChangesAsync();
            await _projectRepo.SaveChangesAsync();

            return (affectedTaskGroups, reopenedProject, affectedAnnotatorIds, projectName);
        }

        public async Task<LabelResponse> UpdateLabelAsync(string userId, int labelId, UpdateLabelRequest request)
        {
            var label = await _labelRepo.GetByIdAsync(labelId);
            if (label == null) throw new KeyNotFoundException("Label not found");

            bool isCriticalChange = IsCriticalLabelChange(label, request);
            string oldName = label.Name;
            int affectedTaskGroups = 0;
            bool reopenedProject = false;
            var affectedAnnotatorIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string projectName = $"Project #{label.ProjectId}";

            await _labelRepo.ExecuteInTransactionAsync(async () =>
            {
                label.Name = request.Name;
                label.Color = request.Color;
                label.GuideLine = request.GuideLine;
                label.ExampleImageUrl = request.ExampleImageUrl;
                label.IsDefault = request.IsDefault;

                label.DefaultChecklist = SerializeChecklist(request.Checklist);

                if (isCriticalChange)
                {
                    label.Version = IncrementVersion(label.Version);
                }
                label.UpdatedAt = DateTime.UtcNow;

                _labelRepo.Update(label);
                await _labelRepo.SaveChangesAsync();

                if (isCriticalChange)
                {
                    var resetResult = await ReopenAffectedAssignmentsForEditedLabelAsync(label, oldName);
                    affectedTaskGroups = resetResult.AffectedTaskGroups;
                    reopenedProject = resetResult.ReopenedProject;
                    affectedAnnotatorIds = resetResult.AffectedAnnotatorIds;
                    if (!string.IsNullOrWhiteSpace(resetResult.ProjectName))
                    {
                        projectName = resetResult.ProjectName;
                    }
                }
            });

            await TryLogActionAsync(
                userId,
                "UpdateLabel",
                "LabelClass",
                label.Id.ToString(),
                $"Updated label '{request.Name}' in Project {label.ProjectId} (v{label.Version})");

            if (reopenedProject)
            {
                await TryLogActionAsync(
                    userId,
                    "ReopenProject",
                    "Project",
                    label.ProjectId.ToString(),
                    $"Project {label.ProjectId} was reopened because label '{label.Name}' changed after completion.");
            }

            if (affectedTaskGroups > 0)
            {
                await TryLogActionAsync(
                    userId,
                    "ResetTasks",
                    "Project",
                    label.ProjectId.ToString(),
                    $"Reset {affectedTaskGroups} task groups because label '{oldName}' was updated to '{request.Name}'.");

                var notificationMessage =
                    $"Label \"{oldName}\" was updated to \"{request.Name}\" in project \"{projectName}\". Some of your images were returned for relabeling.";

                foreach (var annotatorId in affectedAnnotatorIds)
                {
                    await TryNotifyUserAsync(annotatorId, notificationMessage, "Warning");
                }
            }

            return BuildLabelResponse(label);
        }

        public async Task DeleteLabelAsync(string userId, int labelId)
        {
            var label = await _labelRepo.GetByIdAsync(labelId);
            if (label == null) throw new KeyNotFoundException("Label not found");

            var labelUsage = await CheckLabelUsageAsync(labelId);
            if (labelUsage.UsageCount > 0)
            {
                throw new InvalidOperationException(
                    $"Cannot delete label \"{label.Name}\" because annotators have already used it in {labelUsage.UsageCount} annotation(s). A manager can delete a label only before any annotator starts drawing.");
            }

            _labelRepo.Delete(label);
            await _labelRepo.SaveChangesAsync();

            await _logService.LogActionAsync(userId, "DeleteLabel", "LabelClass", labelId.ToString(), $"Deleted label '{label.Name}' from Project {label.ProjectId}");
        }
    }
}

