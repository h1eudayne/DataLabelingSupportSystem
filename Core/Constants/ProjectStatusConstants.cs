namespace Core.Constants
{
    public static class ProjectStatusConstants
    {
        public const string Draft = "Draft";
        public const string Active = "Active";
        public const string Completed = "Completed";
        public const string Archived = "Archived";
        public const string AwaitingManagerConfirmation = "AwaitingManagerConfirmation";
        public const string NewDisplay = "New";
        public const string InProgressDisplay = "InProgress";
        public const string ExpiredDisplay = "Expired";

        public static readonly List<string> AllStatuses = new List<string>
        {
            Draft, Active, Completed, Archived, AwaitingManagerConfirmation
        };

        public static bool IsValid(string status)
        {
            return AllStatuses.Contains(status);
        }
    }
}
