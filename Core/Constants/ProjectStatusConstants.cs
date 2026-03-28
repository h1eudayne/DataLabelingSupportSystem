namespace Core.Constants
{
    
    
    
    public static class ProjectStatusConstants
    {
        public const string Draft = "Draft";
        public const string Active = "Active";
        public const string Completed = "Completed";
        public const string Archived = "Archived";

        public static readonly List<string> AllStatuses = new List<string>
        {
            Draft, Active, Completed, Archived
        };

        public static bool IsValid(string status)
        {
            return AllStatuses.Contains(status);
        }
    }
}
