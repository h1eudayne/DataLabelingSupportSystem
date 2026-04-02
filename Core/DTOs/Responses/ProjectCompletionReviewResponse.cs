namespace Core.DTOs.Responses
{
    public class ProjectCompletionReviewResponse
    {
        public int ProjectId { get; set; }
        public string ProjectName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public bool IsAwaitingManagerConfirmation { get; set; }
        public bool CanManagerConfirmCompletion { get; set; }
        public int TotalDataItems { get; set; }
        public int ApprovedItems { get; set; }
        public int ReturnedItems { get; set; }
        public List<ProjectCompletionReviewItemResponse> Items { get; set; } = new();
    }

    public class ProjectCompletionReviewItemResponse
    {
        public int AssignmentId { get; set; }
        public int DataItemId { get; set; }
        public string? DataItemUrl { get; set; }
        public string AnnotatorId { get; set; } = string.Empty;
        public string AnnotatorName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int RejectCount { get; set; }
        public int ReviewEventCount { get; set; }
        public int ReviewerCount { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public string? AnnotationData { get; set; }
        public string? ManagerDecision { get; set; }
        public string? ManagerComment { get; set; }
        public List<ReviewerFeedbackResponse> ReviewerFeedbacks { get; set; } = new();
        public List<ReviewerFeedbackResponse> ReviewHistory { get; set; } = new();
    }
}
