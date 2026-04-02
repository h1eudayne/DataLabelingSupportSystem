using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Core.DTOs.Requests
{
    public class ReviewRequest : IValidatableObject
    {
        [Required]
        [JsonPropertyName("assignmentId")]
        public int AssignmentId { get; set; }

        [Required]
        [JsonPropertyName("isApproved")]
        public bool IsApproved { get; set; }

        [JsonPropertyName("comment")]
        public string? Comment { get; set; }

        [JsonPropertyName("errorCategory")]
        public string? ErrorCategory { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (IsApproved && !string.IsNullOrWhiteSpace(ErrorCategory))
            {
                yield return new ValidationResult(
                    "ErrorCategory must not be provided when IsApproved is true.",
                    new[] { nameof(ErrorCategory) });
            }

            if (!IsApproved && string.IsNullOrWhiteSpace(Comment))
            {
                yield return new ValidationResult(
                    "A Comment is required when rejecting a task (IsApproved is false).",
                    new[] { nameof(Comment) });
            }
        }
    }

    public class AuditReviewRequest
    {
        [Required]
        [JsonPropertyName("reviewLogId")]
        public int ReviewLogId { get; set; }

        [Required]
        [JsonPropertyName("isCorrectDecision")]
        public bool IsCorrectDecision { get; set; }

        [JsonPropertyName("auditComment")]
        public string? AuditComment { get; set; }
    }

    public class EscalationActionRequest
    {
        [Required]
        [JsonPropertyName("assignmentId")]
        public int AssignmentId { get; set; }

        [Required]
        [JsonPropertyName("action")]
        public string Action { get; set; }

        [JsonPropertyName("comment")]
        public string? Comment { get; set; }

        [JsonPropertyName("newAnnotatorId")]
        public string? NewAnnotatorId { get; set; }
    }
}
