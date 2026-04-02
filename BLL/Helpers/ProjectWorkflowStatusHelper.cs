using Core.Constants;
using Core.Entities;

namespace BLL.Helpers
{
    public static class ProjectWorkflowStatusHelper
    {
        public static int CountApprovedDataItems(Project? project)
        {
            if (project?.DataItems == null)
            {
                return 0;
            }

            return project.DataItems.Count(dataItem =>
                string.Equals(dataItem.Status, TaskStatusConstants.Approved, StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsAwaitingManagerConfirmation(Project? project, int totalItems, int approvedItems)
        {
            if (project == null)
            {
                return false;
            }

            if (string.Equals(project.Status, ProjectStatusConstants.Completed, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(project.Status, ProjectStatusConstants.Archived, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return totalItems > 0 && approvedItems >= totalItems;
        }

        public static bool CanManagerConfirmCompletion(Project? project, int totalItems, int approvedItems)
        {
            return project != null &&
                   string.Equals(project.Status, ProjectStatusConstants.Active, StringComparison.OrdinalIgnoreCase) &&
                   IsAwaitingManagerConfirmation(project, totalItems, approvedItems);
        }

        public static string ResolveManagerFacingStatus(Project? project, int totalItems, int approvedItems, bool hasStarted)
        {
            if (project == null)
            {
                return ProjectStatusConstants.NewDisplay;
            }

            if (string.Equals(project.Status, ProjectStatusConstants.Archived, StringComparison.OrdinalIgnoreCase))
            {
                return ProjectStatusConstants.Archived;
            }

            if (string.Equals(project.Status, ProjectStatusConstants.Completed, StringComparison.OrdinalIgnoreCase))
            {
                return ProjectStatusConstants.Completed;
            }

            if (IsAwaitingManagerConfirmation(project, totalItems, approvedItems))
            {
                return ProjectStatusConstants.AwaitingManagerConfirmation;
            }

            if (DateTime.UtcNow > project.Deadline)
            {
                return ProjectStatusConstants.ExpiredDisplay;
            }

            if (hasStarted)
            {
                return ProjectStatusConstants.InProgressDisplay;
            }

            return ProjectStatusConstants.NewDisplay;
        }
    }
}
