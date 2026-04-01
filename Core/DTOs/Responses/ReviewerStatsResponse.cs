namespace Core.DTOs.Responses
{
    public class ReviewerStatsResponse
    {
        public string ReviewerId { get; set; } = string.Empty;
        public string ReviewerName { get; set; } = string.Empty;
        public int TotalReviews { get; set; }
        public int TotalApproved { get; set; }
        public int TotalRejected { get; set; }
        public int TotalOverridden { get; set; }

        public int TotalDisputes { get; set; }
        public double ApprovalRate { get; set; }
        public double RejectionRate { get; set; }
        public double OverrideRate { get; set; }

        public double DisputeRate { get; set; }
        public double? KQSScore { get; set; }
        public int TotalAuditedReviews { get; set; }
        public int CorrectDecisions { get; set; }
        public int TotalManagerDecisions { get; set; }
        public double? AuditAccuracy { get; set; }
        public DateTime LastUpdated { get; set; }
        public List<ProjectReviewSummary> ProjectSummaries { get; set; } = new List<ProjectReviewSummary>();
    }

    public class ProjectReviewSummary
    {
        public int ProjectId { get; set; }
        public string ProjectName { get; set; } = string.Empty;
        public int TotalReviews { get; set; }
        public int Approved { get; set; }
        public int Rejected { get; set; }
        public double ApprovalRate { get; set; }
        public int TotalManagerDecisions { get; set; }
        public double? KQSScore { get; set; }
    }
}
