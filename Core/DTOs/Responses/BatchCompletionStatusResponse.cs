namespace Core.DTOs.Responses
{
    
    
    
    
    public class BatchCompletionStatusResponse
    {
        public int ProjectId { get; set; }
        public string ProjectName { get; set; } = string.Empty;
        public List<AnnotatorBatchStatus> AnnotatorBatches { get; set; } = new List<AnnotatorBatchStatus>();
        public string RecommendedAnnotatorId { get; set; } = string.Empty;
        public string RecommendedAnnotatorName { get; set; } = string.Empty;
        public bool IsProjectComplete { get; set; }
        public int TotalAnnotators { get; set; }
        public int CompletedAnnotators { get; set; }
    }

    public class AnnotatorBatchStatus
    {
        public string AnnotatorId { get; set; } = string.Empty;
        public string AnnotatorName { get; set; } = string.Empty;
        public int TotalSubmitted { get; set; }
        public int Approved { get; set; }
        public int Rejected { get; set; }
        public int PendingReview { get; set; }
        public bool IsComplete { get; set; }
        public double CompletionPercentage { get; set; }
        public bool IsLocked { get; set; }
        public DateTime? LastActivityAt { get; set; }
    }
}
