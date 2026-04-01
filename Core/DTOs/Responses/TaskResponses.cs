namespace Core.DTOs.Responses
{
    public class TaskResponse
    {
        public int AssignmentId { get; set; }

        public int DataItemId { get; set; }

        public string StorageUrl { get; set; } = string.Empty;

        public string ProjectName { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public string? RejectReason { get; set; }

        public DateTime Deadline { get; set; }

        public string? ReviewerId { get; set; }

        public string? ReviewerName { get; set; }

        public List<LabelResponse> Labels { get; set; } = new List<LabelResponse>();

        public List<object>? ExistingAnnotations { get; set; }
        public string? AnnotatorId { get; set; }
        public string? AnnotatorName { get; set; }
    }

    public class AnnotatorStatsResponse
    {
        public int TotalAssigned { get; set; }

        public int Pending { get; set; }

        public int Submitted { get; set; }

        public int Rejected { get; set; }

        public int Completed { get; set; }
    }

    public class ManagerStatsResponse
    {
        public int TotalProjects { get; set; }

        public int ActiveProjects { get; set; }

        public int TotalDataItems { get; set; }

        public decimal TotalBudget { get; set; }

        public int TotalMembers { get; set; }
    }

    public class ReviewerFeedbackResponse
    {
        public int? ReviewLogId { get; set; }
        public string ReviewerId { get; set; } = string.Empty;
        public string ReviewerName { get; set; } = string.Empty;
        public string? Decision { get; set; }
        public string Verdict { get; set; } = string.Empty;
        public string? Comment { get; set; }
        public string? ErrorCategories { get; set; }
        public DateTime ReviewedAt { get; set; }
        public int ScorePenalty { get; set; }
        public bool IsApproved { get; set; }
        public bool IsAudited { get; set; }
        public string? AuditResult { get; set; }
    }

    public class EscalatedReviewResponse
    {
        public int AssignmentId { get; set; }
        public int ProjectId { get; set; }
        public string ProjectName { get; set; } = string.Empty;
        public int DataItemId { get; set; }
        public string? DataItemUrl { get; set; }
        public string? AnnotationData { get; set; }
        public string AnnotatorId { get; set; } = string.Empty;
        public string AnnotatorName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string EscalationType { get; set; } = string.Empty;
        public string? ProjectType { get; set; }
        public string? GuidelineVersion { get; set; }
        public int RejectCount { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public List<ReviewerFeedbackResponse> ReviewerFeedbacks { get; set; } = new List<ReviewerFeedbackResponse>();
    }

    public class SubmitMultipleTasksResponse
    {
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }
}
