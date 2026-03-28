namespace Core.DTOs.Responses
{
    public class AnnotatorTaskGroupResponse
    {
        public string AnnotatorId { get; set; } = string.Empty;
        public string AnnotatorName { get; set; } = string.Empty;
        public int TotalSubmitted { get; set; }
        public int ReviewedCount { get; set; }
        public int PendingReviewCount { get; set; }
        public double ProgressPercentage { get; set; }
        public List<TaskResponse> Tasks { get; set; } = new List<TaskResponse>();
    }

    public class ReviewQueueResponse
    {
        public int ProjectId { get; set; }
        public string ProjectName { get; set; } = string.Empty;
        public List<AnnotatorTaskGroupResponse> AnnotatorGroups { get; set; } = new List<AnnotatorTaskGroupResponse>();
        public string RecommendedAnnotatorId { get; set; } = string.Empty;
        public string RecommendedAnnotatorName { get; set; } = string.Empty;
        public int TotalPendingTasks { get; set; }
        public int TotalReviewedTasks { get; set; }
    }
}
