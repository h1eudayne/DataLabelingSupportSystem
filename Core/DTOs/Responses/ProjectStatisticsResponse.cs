namespace Core.DTOs.Responses
{
    public class ProjectStatisticsResponse
    {
        public int ProjectId { get; set; }

        public string ProjectName { get; set; } = string.Empty;

        public string ProjectStatus { get; set; } = string.Empty;

        public bool IsAwaitingManagerConfirmation { get; set; }

        public bool CanManagerConfirmCompletion { get; set; }

        public int TotalItems { get; set; }

        public int CompletedItems { get; set; }

        public decimal ProgressPercentage { get; set; }

        public int TotalAssignments { get; set; }

        public int PendingAssignments { get; set; }

        public int SubmittedAssignments { get; set; }

        public int ApprovedAssignments { get; set; }

        public int RejectedAssignments { get; set; }

        public decimal CostIncurred { get; set; }

        public double RejectionRate { get; set; }

        public Dictionary<string, int> ErrorBreakdown { get; set; } = new();

        public List<AnnotatorPerformance> AnnotatorPerformances { get; set; } = new();

        public List<LabelDistribution> LabelDistributions { get; set; } = new();

        public double ProjectAccuracy { get; set; }

        public int FinalCorrect { get; set; }

        public int FirstPassCorrect { get; set; }

        public int TotalReworks { get; set; }

        public int TotalSubmittedTasks { get; set; }

        public double FinalAccuracy { get; set; }

        public double FirstPassAccuracy { get; set; }

        public double ReworkRate { get; set; }

        public List<ReviewerPerformance> ReviewerPerformances { get; set; } = new();
    }

    public class AnnotatorPerformance
    {
        public string AnnotatorId { get; set; } = string.Empty;

        public string AnnotatorName { get; set; } = string.Empty;

        public int TasksAssigned { get; set; }

        public int TasksCompleted { get; set; }

        public int TasksRejected { get; set; }

        public double AverageDurationSeconds { get; set; }

        public double? AverageQualityScore { get; set; }

        public int TotalCriticalErrors { get; set; }

        public double AnnotatorAccuracy { get; set; }

        public int ResolvedTasks { get; set; }

        public int FirstPassCorrect { get; set; }

        public int ReworkCount { get; set; }

        public int TotalSubmittedTasks { get; set; }

        public double FinalAccuracy { get; set; }

        public double FirstPassAccuracy { get; set; }

        public double ReworkRate { get; set; }
    }

    public class ReviewerPerformance
    {
        public string ReviewerId { get; set; } = string.Empty;
        public string ReviewerName { get; set; } = string.Empty;
        public int TotalReviews { get; set; }
        public int CorrectDecisions { get; set; }
        public int TotalManagerDecisions { get; set; }

        public double? ReviewerAccuracy { get; set; }
    }

    public class LabelDistribution
    {
        public string ClassName { get; set; } = string.Empty;

        public int Count { get; set; }
    }
}
