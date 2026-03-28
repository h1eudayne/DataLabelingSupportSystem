namespace Core.DTOs.Responses
{
    public class LabelResponse
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;
        public string? GuideLine { get; set; }
        public string? ExampleImageUrl { get; set; }
        public List<string> Checklist { get; set; } = new List<string>();
        public bool IsDefault { get; set; } = false;
    }

    
    
    
    public class LabelUsageResponse
    {
        public int LabelId { get; set; }
        public string LabelName { get; set; } = string.Empty;
        public int UsageCount { get; set; }
        public int AffectedTasksCount { get; set; }
        public string WarningMessage { get; set; } = string.Empty;
        public bool RequiresConfirmation { get; set; }
    }
}