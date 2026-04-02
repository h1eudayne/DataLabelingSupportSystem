using System.ComponentModel.DataAnnotations;

namespace Core.DTOs.Requests
{
    /// <summary>
    /// Request to detect objects in an image using AI (GECO2 few-shot detection).
    /// The user provides 1-3 exemplar bounding boxes as reference samples; 
    /// the AI then locates all similar objects in the image.
    /// </summary>
    public class AIDetectRequest
    {
        /// <summary>
        /// The assignment ID the detection belongs to, used for authorization checks.
        /// </summary>
        [Range(1, int.MaxValue, ErrorMessage = "AssignmentId is required.")]
        public int AssignmentId { get; set; }

        /// <summary>
        /// Optional image URL sent by the client.
        /// When provided, it must match the assignment image stored by the backend.
        /// </summary>
        public string? ImageUrl { get; set; }

        /// <summary>
        /// 1-3 exemplar bounding boxes drawn by the user to indicate what objects to find.
        /// </summary>
        [Required(ErrorMessage = "At least one exemplar bounding box is required.")]
        [MinLength(1, ErrorMessage = "At least one exemplar bounding box is required.")]
        [MaxLength(3, ErrorMessage = "Maximum 3 exemplar bounding boxes allowed.")]
        public List<ExemplarBox> Exemplars { get; set; } = new();

        /// <summary>
        /// Detection confidence threshold (0.05 to 0.95). Lower = more detections.
        /// Default: 0.33
        /// </summary>
        [Range(0.05, 0.95, ErrorMessage = "Threshold must be between 0.05 and 0.95.")]
        public float Threshold { get; set; } = 0.33f;

        /// <summary>
        /// Whether to return segmentation masks along with bounding boxes.
        /// Default: false (faster).
        /// </summary>
        public bool EnableMask { get; set; } = false;
    }

    /// <summary>
    /// A bounding box drawn by the user as exemplar reference for few-shot detection.
    /// Coordinates are in pixels relative to the original image dimensions.
    /// </summary>
    public class ExemplarBox : IValidatableObject
    {
        /// <summary>X coordinate of the top-left corner (pixels).</summary>
        public int Xmin { get; set; }

        /// <summary>Y coordinate of the top-left corner (pixels).</summary>
        public int Ymin { get; set; }

        /// <summary>X coordinate of the bottom-right corner (pixels).</summary>
        public int Xmax { get; set; }

        /// <summary>Y coordinate of the bottom-right corner (pixels).</summary>
        public int Ymax { get; set; }

        /// <summary>Optional label for this exemplar (e.g. "Fish", "Car").</summary>
        public string? Label { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (Xmin < 0 || Ymin < 0 || Xmax < 0 || Ymax < 0)
            {
                yield return new ValidationResult(
                    "Exemplar coordinates must be non-negative.",
                    new[] { nameof(Xmin), nameof(Ymin), nameof(Xmax), nameof(Ymax) });
            }

            if (Xmax <= Xmin)
            {
                yield return new ValidationResult(
                    "Xmax must be greater than Xmin.",
                    new[] { nameof(Xmin), nameof(Xmax) });
            }

            if (Ymax <= Ymin)
            {
                yield return new ValidationResult(
                    "Ymax must be greater than Ymin.",
                    new[] { nameof(Ymin), nameof(Ymax) });
            }
        }
    }
}
