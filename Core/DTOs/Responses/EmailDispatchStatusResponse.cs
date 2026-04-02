namespace Core.DTOs.Responses
{
    public class EmailDispatchStatusResponse
    {
        public string Message { get; set; } = string.Empty;
        public bool EmailDelivered { get; set; } = true;
        public bool NotificationDelivered { get; set; } = true;
        public string? EmailDeliveryMode { get; set; }
        public string? EmailDeliveryTarget { get; set; }
    }
}
