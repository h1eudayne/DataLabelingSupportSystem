namespace Core.DTOs.Responses
{
    public class ProjectDetailResponse
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        public DateTime Deadline { get; set; }

        public string AllowGeometryTypes { get; set; } = string.Empty;

        public string ManagerId { get; set; } = string.Empty;

        public string ManagerName { get; set; } = string.Empty;

        public string ManagerEmail { get; set; } = string.Empty;

        public List<LabelResponse> Labels { get; set; } = new List<LabelResponse>();

        public int TotalDataItems { get; set; }

        /// <summary>
        /// Data items still in &quot;New&quot; status (no manager assignment yet). Matches task pick-up logic.
        /// </summary>
        public int UnassignedDataItemCount { get; set; }

        public int ProcessedItems { get; set; }

        public int Progress { get; set; }

        public string Status { get; set; } = string.Empty;

        public bool IsAwaitingManagerConfirmation { get; set; }

        public bool CanManagerConfirmCompletion { get; set; }

        public List<MemberResponse> Members { get; set; } = new List<MemberResponse>();
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
    }

    public class MemberResponse
    {
        public string Id { get; set; } = string.Empty;

        public string FullName { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string Role { get; set; } = string.Empty;

        public int TasksAssigned { get; set; }

        public int TasksCompleted { get; set; }

        public decimal Progress { get; set; }
    }

    public class ProjectSummaryResponse
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public DateTime Deadline { get; set; }

        public string Status { get; set; } = "Active";

        public int TotalDataItems { get; set; }

        public decimal Progress { get; set; }

        public int TotalMembers { get; set; }

        public int PendingDisputeCount { get; set; }

        public int PendingPenaltyCount { get; set; }

        public int RejectedImageCount { get; set; }

        public int PriorityIssueCount { get; set; }

        public bool HasPriorityIssue { get; set; }

        public string DefaultActionTab { get; set; } = "datasets";

        public bool IsAwaitingManagerConfirmation { get; set; }

        public bool CanManagerConfirmCompletion { get; set; }
    }
}
