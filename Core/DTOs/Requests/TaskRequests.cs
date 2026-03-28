using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Core.DTOs.Requests
{
    public class AssignTaskRequest
    {
        [Required]
        [JsonPropertyName("projectId")]
        public int ProjectId { get; set; }

        [Required]
        [JsonPropertyName("annotatorId")]
        public string AnnotatorId { get; set; } = string.Empty;

        [JsonPropertyName("reviewerIds")]
        public List<string> ReviewerIds { get; set; } = new List<string>();

        [Required]
        [JsonPropertyName("quantity")]
        public int Quantity { get; set; }

        [JsonPropertyName("reviewerId")]
        public string? ReviewerId { get; set; }
    }

    public class SubmitAnnotationRequest
    {
        [Required]
        [JsonPropertyName("assignmentId")]
        public int AssignmentId { get; set; }

        [JsonPropertyName("dataJSON")]
        public string DataJSON { get; set; } = string.Empty;

        [JsonPropertyName("classId")]
        public int? ClassId { get; set; }
    }

    public class SubmitMultipleTasksRequest
    {
        [Required]
        [JsonPropertyName("assignmentIds")]
        public List<int> AssignmentIds { get; set; } = new List<int>();
    }

    public class AnnotationItem
    {
        [JsonPropertyName("labelClassId")]
        public int LabelClassId { get; set; }

        [JsonPropertyName("valueJson")]
        public string ValueJson { get; set; } = string.Empty;
    }

    public class AnnotationDetail
    {
        [JsonPropertyName("labelClassId")]
        public int LabelClassId { get; set; }

        [JsonPropertyName("valueJson")]
        public string ValueJson { get; set; } = string.Empty;
    }
    public class AssignTeamRequest
    {
        [Required]
        [JsonPropertyName("projectId")]
        public int ProjectId { get; set; }

        [Required]
        [MinLength(1, ErrorMessage = "At least one annotator must be selected.")]
        [JsonPropertyName("annotatorIds")]
        public List<string> AnnotatorIds { get; set; } = new List<string>();

        [JsonPropertyName("reviewerIds")]
        public List<string> ReviewerIds { get; set; } = new List<string>();

        [Required]
        [Range(1, int.MaxValue, ErrorMessage = "Total quantity must be greater than 0.")]
        [JsonPropertyName("totalQuantity")]
        public int TotalQuantity { get; set; }
    }
}
