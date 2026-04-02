namespace Core.DTOs.Responses
{
    public class NotificationPayload
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string? ReferenceType { get; set; }
        public string? ReferenceId { get; set; }
        public string? ActionKey { get; set; }
        public JsonElement? Metadata { get; set; }
        public bool IsRead { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
