using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Core.DTOs.Requests
{

    public class CreateDisputeRequest
    {
        [Required]
        [JsonPropertyName("assignmentId")]
        public int AssignmentId { get; set; }

        [Required]
        [JsonPropertyName("reason")]
        public string Reason { get; set; } = string.Empty;
    }

    public class ResolveDisputeRequest
    {
        [Required]
        [JsonPropertyName("disputeId")]
        public int DisputeId { get; set; }

        [Required]
        [JsonPropertyName("isAccepted")]
        public bool IsAccepted { get; set; }

        [JsonPropertyName("managerComment")]
        public string? ManagerComment { get; set; }
    }
}