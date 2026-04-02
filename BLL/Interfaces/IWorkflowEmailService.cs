using Core.Entities;

namespace BLL.Interfaces
{
    public interface IWorkflowEmailService
    {
        Task SendWelcomeEmailAsync(User user, User? manager);
        Task SendAnnotatorAssignmentEmailAsync(Project project, User manager, User annotator, int taskCount, int reviewerCount);
        Task SendReviewerAssignmentEmailAsync(Project project, User manager, User reviewer, int taskCount, int annotatorCount);
        Task SendForgotPasswordRequestEmailsAsync(User requester, IReadOnlyCollection<User> admins);
        Task SendAdminPasswordResetEmailAsync(User user, string newPassword);
        Task SendDisputeResolutionEmailsAsync(
            Project project,
            User manager,
            User annotator,
            Assignment assignment,
            IReadOnlyCollection<User> reviewers,
            IReadOnlyCollection<ReviewLog> reviewLogs,
            bool isAccepted,
            string managerComment);
        Task SendProjectCompletedEmailsAsync(
            Project project,
            User manager,
            IReadOnlyCollection<User> participants,
            IReadOnlyCollection<Assignment> assignments,
            IReadOnlyCollection<UserProjectStat> projectStats);
    }
}
