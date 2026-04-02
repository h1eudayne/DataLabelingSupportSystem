namespace Core.DTOs.Responses
{
    /// <summary>
    /// Diagnostic metadata describing what the HuggingFace / Gradio upstream
    /// actually returned for a GECO2 inference request.
    /// </summary>
    public class AIDetectionDiagnosticsResponse
    {
        public bool ProviderRequestSubmitted { get; set; }

        public bool ProviderResultReceived { get; set; }

        public bool CompleteEventReceived { get; set; }

        public bool PreviewImageReturned { get; set; }

        public bool RawDetectionStateReturned { get; set; }

        public bool RawDetectionsReturned { get; set; }

        public string ProviderUrl { get; set; } = string.Empty;

        public string? PredictEndpoint { get; set; }

        public string? EventId { get; set; }

        public int OutputItemsCount { get; set; }

        public string ResultSource { get; set; } = string.Empty;
    }
}
