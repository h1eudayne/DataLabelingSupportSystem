namespace Core.DTOs.Responses
{
    public class DisputeResponse
    {
        public int Id { get; set; }
        public int AssignmentId { get; set; }
        public string AnnotatorId { get; set; } = string.Empty;
        public string? AnnotatorName { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? ManagerComment { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public int? ProjectId { get; set; }
        public string? ProjectName { get; set; }
        public string? DataItemUrl { get; set; }
        public string? AssignmentStatus { get; set; }
        public string? ReviewerName { get; set; }
        public List<ReviewerFeedbackResponse> ReviewerFeedbacks { get; set; } = new List<ReviewerFeedbackResponse>();
    }
}
