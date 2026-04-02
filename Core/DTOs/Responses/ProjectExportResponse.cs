using System.Text.Json;

namespace Core.DTOs.Responses
{
    public class ProjectExportResponse
    {
        public ProjectExportProjectInfoResponse Project { get; set; } = new();
        public List<ProjectExportItemResponse> Items { get; set; } = new();
    }

    public class ProjectExportProjectInfoResponse
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string GuidelineVersion { get; set; } = string.Empty;
        public DateTime Deadline { get; set; }
        public DateTime ExportedAt { get; set; }
        public int TotalDataItems { get; set; }
        public int ExportedItems { get; set; }
        public List<ProjectExportLabelResponse> Labels { get; set; } = new();
    }

    public class ProjectExportLabelResponse
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;
    }

    public class ProjectExportItemResponse
    {
        public int DataItemId { get; set; }
        public int BucketId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string StorageUrl { get; set; } = string.Empty;
        public DateTime UploadedAt { get; set; }
        public string AnnotatorEmail { get; set; } = string.Empty;
        public string ReviewerEmail { get; set; } = string.Empty;
        public DateTime? ApprovedAt { get; set; }
        public int AnnotationCount { get; set; }
        public JsonElement AnnotationData { get; set; }
    }
}
