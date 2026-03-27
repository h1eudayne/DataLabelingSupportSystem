namespace Core.DTOs.Responses
{
    
    
    
    
    public class DisputeResolutionDetailsResponse
    {
        public int DisputeId { get; set; }
        public int AssignmentId { get; set; }
        public string AnnotatorId { get; set; } = string.Empty;
        public string AnnotatorName { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? ManagerComment { get; set; }
        public string ManagerName { get; set; } = string.Empty;
        public string? ReviewerComment { get; set; }
        public string? ReviewerVerdict { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public int? ProjectId { get; set; }
        public string? ProjectName { get; set; }
        public string? DataItemUrl { get; set; }
        public string? AssignmentStatus { get; set; }
        public bool WasOverturned { get; set; } 
        public string ResolutionSummary { get; set; } = string.Empty;
    }
}
