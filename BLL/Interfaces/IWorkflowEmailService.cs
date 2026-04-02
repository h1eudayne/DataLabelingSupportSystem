using Core.Entities;

namespace BLL.Interfaces
{
    public interface IWorkflowEmailService
    {
        Task SendWelcomeEmailAsync(User user, User? manager, string? temporaryPassword = null);
        Task SendAnnotatorAssignmentEmailAsync(Project project, User manager, User annotator, int taskCount, int reviewerCount);
        Task SendReviewerAssignmentEmailAsync(Project project, User manager, User reviewer, int taskCount, int annotatorCount);
        Task SendForgotPasswordRequestEmailsAsync(User requester, IReadOnlyCollection<User> admins);
        Task SendAdminPasswordResetEmailAsync(User user, string temporaryPassword);
        Task SendDisputeCreatedEmailsAsync(
            Project project,
            User annotator,
            Assignment assignment,
            IReadOnlyCollection<User> reviewers,
            User? manager,
            string reason);
        Task SendDisputeResolutionEmailsAsync(
            Project project,
            User manager,
            User annotator,
            Assignment assignment,
            IReadOnlyCollection<User> reviewers,
            IReadOnlyCollection<ReviewLog> reviewLogs,
            bool isAccepted,
            string managerComment);
        Task SendEscalationTriggeredEmailsAsync(
            Project project,
            User manager,
            User annotator,
            Assignment assignment,
            IReadOnlyCollection<User> reviewers,
            IReadOnlyCollection<ReviewLog> reviewLogs,
            string escalationType,
            int rejectCount);
        Task SendEscalationResolvedEmailsAsync(
            Project project,
            User manager,
            Assignment assignment,
            User? originalAnnotator,
            User? newAnnotator,
            IReadOnlyCollection<User> reviewers,
            IReadOnlyCollection<ReviewLog> reviewLogs,
            string action,
            string? managerComment,
            string escalationType,
            int rejectCount);
        Task SendProjectCompletedEmailsAsync(
            Project project,
            User manager,
            IReadOnlyCollection<User> participants,
            IReadOnlyCollection<Assignment> assignments,
            IReadOnlyCollection<UserProjectStat> projectStats);
    }
}
