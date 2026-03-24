using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Core.DTOs.Requests
{
    public class ReviewRequest : IValidatableObject
    {
        [Required]
        public int AssignmentId { get; set; }

        [Required]
        public bool IsApproved { get; set; }

        public string? Comment { get; set; }

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
        public int ReviewLogId { get; set; }

        [Required]
        public bool IsCorrectDecision { get; set; }

        public string? AuditComment { get; set; }
    }
}