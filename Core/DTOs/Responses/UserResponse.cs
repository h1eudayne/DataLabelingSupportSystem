namespace Core.DTOs.Responses
{
    public class UserProjectSummaryResponse
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }

    public class UserResponse
    {
        public string Id { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string Role { get; set; } = null!;
        public string AvatarUrl { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public string? ManagerId { get; set; }
        public string? ManagerName { get; set; }
        public string? ManagerEmail { get; set; }
        public int TotalProjects { get; set; }
        public int UnfinishedProjectCount { get; set; }
        public List<UserProjectSummaryResponse> UnfinishedProjects { get; set; } = new();
        public bool HasPendingGlobalBanRequest { get; set; }
    }

    public class ToggleUserStatusResponse
    {
        public string Message { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public bool RequiresManagerApproval { get; set; }
        public int? GlobalBanRequestId { get; set; }
    }

    public class PagedResponse<T>
    {
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public object? Stats { get; set; }
        public List<T> Items { get; set; } = new();
    }
}
