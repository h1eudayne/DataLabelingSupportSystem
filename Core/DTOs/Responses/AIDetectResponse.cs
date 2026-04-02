namespace Core.DTOs.Responses
{
    /// <summary>
    /// Response from the GECO2 AI detection service.
    /// Contains detected object bounding boxes and a processed result image.
    /// </summary>
    public class AIDetectResponse
    {
        /// <summary>Total number of detected objects.</summary>
        public int Count { get; set; }

        /// <summary>
        /// URL to the result image with bounding boxes drawn by the AI.
        /// This is a temporary URL hosted by HuggingFace Spaces.
        /// </summary>
        public string? ResultImageUrl { get; set; }

        /// <summary>
        /// Raw detection boxes parsed directly from the HuggingFace / Gradio response when available.
        /// </summary>
        public List<AIDetectionBoxResponse> Detections { get; set; } = new();

        /// <summary>Time taken to process the request in milliseconds.</summary>
        public long ProcessingTimeMs { get; set; }

        /// <summary>
        /// Diagnostic metadata describing what the HuggingFace / Gradio upstream returned.
        /// Useful for distinguishing transport success from model-level zero detections.
        /// </summary>
        public AIDetectionDiagnosticsResponse? Diagnostics { get; set; }

        /// <summary>
        /// Threshold value used for this detection (0.05-0.95).
        /// </summary>
        public float ThresholdUsed { get; set; }

        /// <summary>
        /// All threshold values attempted for this request, in order.
        /// </summary>
        public List<float> ThresholdAttempts { get; set; } = new();

        /// <summary>
        /// Indicates whether segmentation masks were enabled.
        /// </summary>
        public bool MasksEnabled { get; set; }

        /// <summary>
        /// Human-readable status message.
        /// </summary>
        public string Message { get; set; } = string.Empty;
    }
}
