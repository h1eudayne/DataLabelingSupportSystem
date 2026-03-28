using BLL.Interfaces;
using Core.DTOs.Requests;
using Core.DTOs.Responses;
using DAL.Interfaces;
using Core.Entities;
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

        public LabelService(
            ILabelRepository labelRepo,
            IAssignmentRepository assignmentRepo,
            IActivityLogService logService,
            IRepository<Annotation> annotationRepo,
            IProjectRepository projectRepo)
        {
            _labelRepo = labelRepo;
            _assignmentRepo = assignmentRepo;
            _logService = logService;
            _annotationRepo = annotationRepo;
            _projectRepo = projectRepo;
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
                    ? $"Warning: This label is currently being used in {count} annotation(s). Editing will reset those tasks."
                    : "This label is not currently in use.",
                AffectedTasksCount = count,
                RequiresConfirmation = count > 0
            };
        }

        public async Task<LabelResponse> CreateLabelAsync(string userId, CreateLabelRequest request)
        {
            if (await _labelRepo.ExistsInProjectAsync(request.ProjectId, request.Name))
                throw new Exception("Label name already exists in this project.");

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

        public async Task<LabelResponse> UpdateLabelAsync(string userId, int labelId, UpdateLabelRequest request)
        {
            var label = await _labelRepo.GetByIdAsync(labelId);
            if (label == null) throw new Exception("Label not found");

            bool isCriticalChange = label.Name != request.Name || label.GuideLine != request.GuideLine;
            string oldName = label.Name;

            label.Name = request.Name;
            label.Color = request.Color;
            label.GuideLine = request.GuideLine;
            label.ExampleImageUrl = request.ExampleImageUrl;
            label.IsDefault = request.IsDefault;

            if (request.Checklist != null)
            {
                label.DefaultChecklist = request.Checklist.Any()
                                         ? JsonSerializer.Serialize(request.Checklist)
                                         : "[]";
            }

            if (isCriticalChange)
            {
                label.Version = IncrementVersion(label.Version);
            }
            label.UpdatedAt = DateTime.UtcNow;

            _labelRepo.Update(label);
            await _labelRepo.SaveChangesAsync();

            await _logService.LogActionAsync(userId, "UpdateLabel", "LabelClass", label.Id.ToString(), $"Updated label '{request.Name}' in Project {label.ProjectId} (v{label.Version})");

            if (isCriticalChange)
            {
                int activeTasks = await _assignmentRepo.CountActiveTasksAsync(label.ProjectId);

                if (activeTasks > 0)
                {
                    await _assignmentRepo.ResetAssignmentsByProjectAsync(
                        label.ProjectId,
                        $"AUTO-RESET: Label '{oldName}' updated to '{request.Name}'. Please review."
                    );

                    await _logService.LogActionAsync(userId, "ResetTasks", "Project", label.ProjectId.ToString(), $"Reset {activeTasks} tasks because Label '{oldName}' was updated.");
                }
            }

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

        public async Task DeleteLabelAsync(string userId, int labelId)
        {
            var label = await _labelRepo.GetByIdAsync(labelId);
            if (label == null) throw new Exception("Label not found");

            int activeTasks = await _assignmentRepo.CountActiveTasksAsync(label.ProjectId);

            if (activeTasks > 0)
            {
                await _assignmentRepo.ResetAssignmentsByProjectAsync(
                    label.ProjectId,
                    $"AUTO-RESET: Label '{label.Name}' was deleted. Please review annotations."
                );

                await _logService.LogActionAsync(userId, "ResetTasks", "Project", label.ProjectId.ToString(), $"Reset {activeTasks} tasks because Label '{label.Name}' was deleted.");
            }

            _labelRepo.Delete(label);
            await _labelRepo.SaveChangesAsync();

            await _logService.LogActionAsync(userId, "DeleteLabel", "LabelClass", labelId.ToString(), $"Deleted label '{label.Name}' from Project {label.ProjectId}");
        }
    }
}
