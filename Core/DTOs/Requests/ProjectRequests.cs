using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Core.DTOs.Requests
{
    public class CreateProjectRequest
    {
        [Required]
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("deadline")]
        public DateTime? Deadline { get; set; }

        [JsonPropertyName("penaltyUnit")]
        public int PenaltyUnit { get; set; } = 10;

        [JsonPropertyName("allowGeometryTypes")]
        public string AllowGeometryTypes { get; set; } = "Rectangle";

        [JsonPropertyName("annotationGuide")]
        public string? AnnotationGuide { get; set; }

        [JsonPropertyName("maxTaskDurationHours")]
        public int MaxTaskDurationHours { get; set; } = 24;

        [JsonPropertyName("startDate")]
        public DateTime? StartDate { get; set; }

        [JsonPropertyName("endDate")]
        public DateTime? EndDate { get; set; }

        [JsonPropertyName("reviewChecklist")]
        public List<ChecklistItemRequest>? ReviewChecklist { get; set; }

        [JsonPropertyName("labelClasses")]
        public List<LabelClassRequest> LabelClasses { get; set; } = new List<LabelClassRequest>();
    }

    public class UpdateProjectRequest
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("penaltyUnit")]
        public int PenaltyUnit { get; set; }

        [JsonPropertyName("annotationGuide")]
        public string? AnnotationGuide { get; set; }

        [JsonPropertyName("allowGeometryTypes")]
        public string? AllowGeometryTypes { get; set; }

        [JsonPropertyName("maxTaskDurationHours")]
        public int? MaxTaskDurationHours { get; set; }

        [JsonPropertyName("deadline")]
        public DateTime? Deadline { get; set; }

        [JsonPropertyName("startDate")]
        public DateTime? StartDate { get; set; }

        [JsonPropertyName("endDate")]
        public DateTime? EndDate { get; set; }

        [JsonPropertyName("reviewChecklist")]
        public List<ChecklistItemRequest>? ReviewChecklist { get; set; }
    }

    public class LabelClassRequest
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("color")]
        public string Color { get; set; } = "#000000";

        [JsonPropertyName("guideLine")]
        public string GuideLine { get; set; } = string.Empty;

        [JsonPropertyName("checklist")]
        public List<string>? Checklist { get; set; }

        [JsonPropertyName("exampleImageUrl")]
        public string? ExampleImageUrl { get; set; }

        [JsonPropertyName("isDefault")]
        public bool IsDefault { get; set; } = false;
    }

    public class ImportDataRequest
    {
        [JsonPropertyName("storageUrls")]
        public List<string> StorageUrls { get; set; } = new List<string>();
    }

    public class ChecklistItemRequest
    {
        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("weight")]
        public int Weight { get; set; } = 1;
    }
    public class AssignReviewersRequest
    {
        [Required]
        [JsonPropertyName("projectId")]
        public int ProjectId { get; set; }

        [Required]
        [MinLength(1, ErrorMessage = "At least one reviewer must be selected.")]
        [JsonPropertyName("reviewerIds")]
        public List<string> ReviewerIds { get; set; } = new List<string>();
    }
}
