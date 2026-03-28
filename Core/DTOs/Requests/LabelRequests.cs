using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Core.DTOs.Requests
{
    public class CreateLabelRequest
    {
        [Required]
        [JsonPropertyName("projectId")]
        public int ProjectId { get; set; }

        [Required]
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("color")]
        public string Color { get; set; } = "#FFFFFF";

        [JsonPropertyName("guideLine")]
        public string? GuideLine { get; set; }

        [JsonPropertyName("exampleImageUrl")]
        public string? ExampleImageUrl { get; set; }

        [JsonPropertyName("checklist")]
        public List<string>? Checklist { get; set; }

        [JsonPropertyName("isDefault")]
        public bool IsDefault { get; set; } = false;
    }

    public class UpdateLabelRequest
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("color")]
        public string Color { get; set; } = string.Empty;

        [JsonPropertyName("guideLine")]
        public string? GuideLine { get; set; }

        [JsonPropertyName("exampleImageUrl")]
        public string? ExampleImageUrl { get; set; }

        [JsonPropertyName("checklist")]
        public List<string>? Checklist { get; set; }

        [JsonPropertyName("isDefault")]
        public bool IsDefault { get; set; } = false;
    }
}
