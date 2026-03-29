using BLL.Interfaces;
using Core.Constants;
using Core.Entities;
using System.Net;
using System.Text;

namespace BLL.Services
{
    public class WorkflowEmailService : IWorkflowEmailService
    {
        private readonly IEmailService _emailService;

        public WorkflowEmailService(IEmailService emailService)
        {
            _emailService = emailService;
        }

        public async Task SendWelcomeEmailAsync(User user, User? manager)
        {
            var managerSummary = manager == null
                ? "Not assigned yet"
                : $"{Encode(manager.FullName)} ({Encode(manager.Email)})";

            var body = WrapEmail(
                "Welcome to Data Labeling Support System",
                $@"
                    <p>Hello {Encode(user.FullName)},</p>
                    <p>Your account has been created successfully. We are glad to have you on the project team.</p>
                    {BuildTable(new[]
                    {
                        ("Role", Encode(user.Role)),
                        ("Email", Encode(user.Email)),
                        ("Direct manager", managerSummary),
                        ("Activated at", FormatUtc(DateTime.UtcNow))
                    })}
                    <p>Please contact your manager or administrator if you need help with first-time access or onboarding.</p>");

            await SendEmailSafelyAsync(user.Email, "Welcome to Data Labeling Support System", body);
        }

        public async Task SendAnnotatorAssignmentEmailAsync(Project project, User manager, User annotator, int taskCount, int reviewerCount)
        {
            var reviewerSummary = reviewerCount > 0
                ? reviewerCount.ToString()
                : "No reviewer assigned yet";

            var body = WrapEmail(
                $"New tasks assigned in {Encode(project.Name)}",
                $@"
                    <p>Hello {Encode(annotator.FullName)},</p>
                    <p>You have received a new task assignment.</p>
                    {BuildTable(new[]
                    {
                        ("Project", Encode(project.Name)),
                        ("Manager", $"{Encode(manager.FullName)} ({Encode(manager.Email)})"),
                        ("Tasks assigned", taskCount.ToString()),
                        ("Reviewers per task", reviewerSummary),
                        ("Project deadline", FormatUtc(project.Deadline)),
                        ("Task duration limit", $"{project.MaxTaskDurationHours} hour(s)")
                    })}
                    <p>Please review the project guideline and complete the assigned work before the deadline.</p>");

            await SendEmailSafelyAsync(
                annotator.Email,
                $"[Task Assignment] {project.Name}",
                body);
        }

        public async Task SendReviewerAssignmentEmailAsync(Project project, User manager, User reviewer, int taskCount, int annotatorCount)
        {
            var body = WrapEmail(
                $"Review assignment in {Encode(project.Name)}",
                $@"
                    <p>Hello {Encode(reviewer.FullName)},</p>
                    <p>You have been assigned new review work.</p>
                    {BuildTable(new[]
                    {
                        ("Project", Encode(project.Name)),
                        ("Manager", $"{Encode(manager.FullName)} ({Encode(manager.Email)})"),
                        ("Assignments to review", taskCount.ToString()),
                        ("Annotators covered", annotatorCount.ToString()),
                        ("Project deadline", FormatUtc(project.Deadline))
                    })}
                    <p>Please review each task carefully and record a clear decision for every assigned item.</p>");

            await SendEmailSafelyAsync(
                reviewer.Email,
                $"[Review Assignment] {project.Name}",
                body);
        }

        public async Task SendForgotPasswordRequestEmailsAsync(User requester, IReadOnlyCollection<User> admins)
        {
            var managerSummary = string.IsNullOrWhiteSpace(requester.ManagerId)
                ? "Not assigned"
                : Encode(requester.ManagerId);

            var subject = "[Action Required] Password reset request";
            var body = WrapEmail(
                "Password reset request pending admin approval",
                $@"
                    <p>A user has requested a password reset. This flow now requires manual admin approval.</p>
                    {BuildTable(new[]
                    {
                        ("User", Encode(requester.FullName)),
                        ("Email", Encode(requester.Email)),
                        ("Role", Encode(requester.Role)),
                        ("ManagerId", managerSummary),
                        ("Requested at", FormatUtc(DateTime.UtcNow))
                    })}
                    <p>Please open User Management, verify the request, and reset the password manually if the request is valid.</p>");

            foreach (var admin in admins.Where(a => !string.IsNullOrWhiteSpace(a.Email)))
            {
                await SendEmailSafelyAsync(admin.Email, subject, body);
            }
        }

        public async Task SendAdminPasswordResetEmailAsync(User user, string newPassword)
        {
            var body = WrapEmail(
                "Your password has been reset by an administrator",
                $@"
                    <p>Hello {Encode(user.FullName)},</p>
                    <p>An administrator has reset your password after reviewing your request.</p>
                    {BuildTable(new[]
                    {
                        ("Login email", Encode(user.Email)),
                        ("Temporary password", Encode(newPassword)),
                        ("Reset at", FormatUtc(DateTime.UtcNow))
                    })}
                    <p>Please sign in with this temporary password and change it immediately for security.</p>");

            await SendEmailSafelyAsync(
                user.Email,
                "Your password has been reset by an administrator",
                body);
        }

        public async Task SendDisputeResolutionEmailsAsync(
            Project project,
            User manager,
            User annotator,
            Assignment assignment,
            IReadOnlyCollection<User> reviewers,
            IReadOnlyCollection<ReviewLog> reviewLogs,
            bool isAccepted,
            string managerComment)
        {
            int approvedVotes = reviewLogs.Count(log => IsApprovedVerdict(log.Verdict));
            int rejectedVotes = reviewLogs.Count(log => IsRejectedVerdict(log.Verdict));
            bool isTie = approvedVotes > 0 && approvedVotes == rejectedVotes;
            bool hasMixedVerdicts = approvedVotes > 0 && rejectedVotes > 0;
            string decisionLabel = isAccepted ? "Accepted" : "Rejected";
            string reviewerSummary = $"{approvedVotes} approve / {rejectedVotes} reject";

            var annotatorBody = WrapEmail(
                $"Dispute {decisionLabel.ToLowerInvariant()} for task #{assignment.Id}",
                $@"
                    <p>Hello {Encode(annotator.FullName)},</p>
                    <p>Your dispute for task <strong>#{assignment.Id}</strong> in project <strong>{Encode(project.Name)}</strong> has been <strong>{decisionLabel}</strong> by your manager.</p>
                    {BuildTable(new[]
                    {
                        ("Project", Encode(project.Name)),
                        ("Task", $"#{assignment.Id}"),
                        ("Manager", $"{Encode(manager.FullName)} ({Encode(manager.Email)})"),
                        ("Decision", decisionLabel),
                        ("Reviewer summary before decision", reviewerSummary),
                        ("Manager note", Encode(managerComment))
                    })}
                    {BuildDisputeContext(hasMixedVerdicts, isTie)}");

            await SendEmailSafelyAsync(
                annotator.Email,
                $"[Dispute Result] {project.Name} - Task #{assignment.Id}",
                annotatorBody);

            foreach (var reviewer in reviewers)
            {
                var reviewerLog = reviewLogs
                    .Where(log => log.ReviewerId == reviewer.Id)
                    .OrderByDescending(log => log.CreatedAt)
                    .FirstOrDefault();

                string reviewerVerdict = reviewerLog == null
                    ? "No recorded verdict"
                    : Encode(reviewerLog.Verdict);

                bool alignedWithManager = reviewerLog != null &&
                    ((isAccepted && IsApprovedVerdict(reviewerLog.Verdict)) ||
                     (!isAccepted && IsRejectedVerdict(reviewerLog.Verdict)));

                string alignmentSummary = reviewerLog == null
                    ? "No comparison available"
                    : alignedWithManager
                        ? "Your review aligned with the final manager decision."
                        : "Your review did not align with the final manager decision.";

                var reviewerBody = WrapEmail(
                    $"Final dispute decision for task #{assignment.Id}",
                    $@"
                        <p>Hello {Encode(reviewer.FullName)},</p>
                        <p>The manager has finalized a dispute for one of your reviewed tasks.</p>
                        {BuildTable(new[]
                        {
                            ("Project", Encode(project.Name)),
                            ("Task", $"#{assignment.Id}"),
                            ("Annotator", $"{Encode(annotator.FullName)} ({Encode(annotator.Email)})"),
                            ("Your verdict", reviewerVerdict),
                            ("Final manager decision", decisionLabel),
                            ("Manager note", Encode(managerComment)),
                            ("Reviewer summary before decision", reviewerSummary)
                        })}
                        <p>{Encode(alignmentSummary)}</p>
                        {BuildDisputeContext(hasMixedVerdicts, isTie)}");

                await SendEmailSafelyAsync(
                    reviewer.Email,
                    $"[Dispute Result] {project.Name} - Task #{assignment.Id}",
                    reviewerBody);
            }
        }

        public async Task SendProjectCompletedEmailsAsync(
            Project project,
            User manager,
            IReadOnlyCollection<User> participants,
            IReadOnlyCollection<Assignment> assignments,
            IReadOnlyCollection<UserProjectStat> projectStats)
        {
            var subject = $"[Project Completed] {project.Name}";
            var distinctItemCount = assignments.Select(a => a.DataItemId).Distinct().Count();
            var annotatorCount = assignments.Select(a => a.AnnotatorId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().Count();
            var reviewerCount = assignments.Select(a => a.ReviewerId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().Count();

            var managerBody = WrapEmail(
                $"Project completed: {Encode(project.Name)}",
                $@"
                    <p>Hello {Encode(manager.FullName)},</p>
                    <p>You have marked the project as completed successfully.</p>
                    {BuildTable(new[]
                    {
                        ("Project", Encode(project.Name)),
                        ("Completed at", FormatUtc(DateTime.UtcNow)),
                        ("Project deadline", FormatUtc(project.Deadline)),
                        ("Distinct data items", distinctItemCount.ToString()),
                        ("Total assignment records", assignments.Count.ToString()),
                        ("Annotators involved", annotatorCount.ToString()),
                        ("Reviewers involved", reviewerCount.ToString())
                    })}
                    <p>Personalized completion summaries have been sent to all project participants.</p>");

            await SendEmailSafelyAsync(manager.Email, subject, managerBody);

            foreach (var participant in participants
                .Where(user => user.Id != manager.Id)
                .GroupBy(user => user.Id)
                .Select(group => group.First()))
            {
                var body = BuildParticipantCompletionBody(project, manager, participant, assignments, projectStats);
                await SendEmailSafelyAsync(participant.Email, subject, body);
            }
        }

        private string BuildParticipantCompletionBody(
            Project project,
            User manager,
            User participant,
            IReadOnlyCollection<Assignment> assignments,
            IReadOnlyCollection<UserProjectStat> projectStats)
        {
            var userStats = projectStats.FirstOrDefault(stat => stat.UserId == participant.Id);

            if (participant.Role == UserRoles.Reviewer)
            {
                var reviewerAssignments = assignments
                    .Where(a => string.Equals(a.ReviewerId, participant.Id, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var reviewLogs = reviewerAssignments
                    .SelectMany(a => a.ReviewLogs ?? Enumerable.Empty<ReviewLog>())
                    .Where(log => string.Equals(log.ReviewerId, participant.Id, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                int approvedDecisions = reviewLogs.Count(log => IsApprovedVerdict(log.Verdict));
                int rejectedDecisions = reviewLogs.Count(log => IsRejectedVerdict(log.Verdict));
                int totalPenalty = reviewLogs.Sum(log => log.ScorePenalty);

                return WrapEmail(
                    $"Project completed: {Encode(project.Name)}",
                    $@"
                        <p>Hello {Encode(participant.FullName)},</p>
                        <p>The project has been completed. Below is your reviewer summary.</p>
                        {BuildTable(new[]
                        {
                            ("Project", Encode(project.Name)),
                            ("Manager", $"{Encode(manager.FullName)} ({Encode(manager.Email)})"),
                            ("Review assignments", reviewerAssignments.Count.ToString()),
                            ("Distinct data items reviewed", reviewerAssignments.Select(a => a.DataItemId).Distinct().Count().ToString()),
                            ("Approved decisions", approvedDecisions.ToString()),
                            ("Rejected decisions", rejectedDecisions.ToString()),
                            ("Penalty points issued", totalPenalty.ToString()),
                            ("Reviewer quality score", FormatNumber(userStats?.ReviewerQualityScore ?? 100)),
                            ("Manager-aligned decisions", $"{userStats?.TotalReviewerCorrectByManager ?? 0}/{userStats?.TotalReviewerManagerDecisions ?? 0}"),
                            ("Overrides", (userStats?.OverrideCount ?? 0).ToString()),
                            ("Disputes", (userStats?.DisputeCount ?? 0).ToString())
                        })}
                        <p>Thank you for completing the review work on this project.</p>");
            }

            var annotatorAssignments = assignments
                .Where(a => string.Equals(a.AnnotatorId, participant.Id, StringComparison.OrdinalIgnoreCase))
                .ToList();

            return WrapEmail(
                $"Project completed: {Encode(project.Name)}",
                $@"
                    <p>Hello {Encode(participant.FullName)},</p>
                    <p>The project has been completed. Below is your annotator summary.</p>
                    {BuildTable(new[]
                    {
                        ("Project", Encode(project.Name)),
                        ("Manager", $"{Encode(manager.FullName)} ({Encode(manager.Email)})"),
                        ("Assignments received", annotatorAssignments.Count.ToString()),
                        ("Distinct data items", annotatorAssignments.Select(a => a.DataItemId).Distinct().Count().ToString()),
                        ("Approved assignments", annotatorAssignments.Count(a => a.Status == TaskStatusConstants.Approved).ToString()),
                        ("Submitted assignments", annotatorAssignments.Count(a => a.Status == TaskStatusConstants.Submitted).ToString()),
                        ("Rejected assignments", annotatorAssignments.Count(a => a.Status == TaskStatusConstants.Rejected).ToString()),
                        ("Efficiency score", FormatNumber(userStats?.EfficiencyScore ?? 0)),
                        ("Average quality score", FormatNumber(userStats?.AverageQualityScore ?? 100)),
                        ("First-pass correct", (userStats?.TotalFirstPassCorrect ?? 0).ToString()),
                        ("Critical errors", (userStats?.TotalCriticalErrors ?? 0).ToString()),
                        ("Manager-confirmed decisions", $"{userStats?.TotalCorrectByManager ?? 0}/{userStats?.TotalManagerDecisions ?? 0}")
                    })}
                    <p>Thank you for your contribution to this project.</p>");
        }

        private static string BuildDisputeContext(bool hasMixedVerdicts, bool isTie)
        {
            if (isTie)
            {
                return "<p>This case had a tied reviewer outcome before the manager finalized the result.</p>";
            }

            if (hasMixedVerdicts)
            {
                return "<p>This case included conflicting reviewer decisions before the manager made the final call.</p>";
            }

            return string.Empty;
        }

        private async Task SendEmailSafelyAsync(string? toEmail, string subject, string htmlBody)
        {
            if (string.IsNullOrWhiteSpace(toEmail))
            {
                return;
            }

            try
            {
                await _emailService.SendEmailAsync(toEmail, subject, htmlBody);
            }
            catch
            {
            }
        }

        private static bool IsApprovedVerdict(string? verdict)
        {
            return string.Equals(verdict, "Approved", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(verdict, "Approve", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRejectedVerdict(string? verdict)
        {
            return string.Equals(verdict, "Rejected", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(verdict, "Reject", StringComparison.OrdinalIgnoreCase);
        }

        private static string WrapEmail(string title, string body)
        {
            return $@"
                <div style='font-family: Arial, sans-serif; color: #1f2937; line-height: 1.6;'>
                    <h2 style='margin-bottom: 8px;'>{Encode(title)}</h2>
                    {body}
                    <hr style='margin: 20px 0; border: none; border-top: 1px solid #e5e7eb;' />
                    <p style='color: #6b7280;'>This is an automated email from Data Labeling Support System.</p>
                </div>";
        }

        private static string BuildTable(IEnumerable<(string Label, string Value)> rows)
        {
            var builder = new StringBuilder();
            builder.Append("<table style='border-collapse: collapse; margin: 12px 0;'>");

            foreach (var (label, value) in rows)
            {
                builder.Append("<tr>");
                builder.Append($"<td style='padding: 6px 16px 6px 0; font-weight: 600; vertical-align: top;'>{Encode(label)}:</td>");
                builder.Append($"<td style='padding: 6px 0; vertical-align: top;'>{value}</td>");
                builder.Append("</tr>");
            }

            builder.Append("</table>");
            return builder.ToString();
        }

        private static string FormatUtc(DateTime? value)
        {
            return value.HasValue
                ? value.Value.ToString("yyyy-MM-dd HH:mm:ss 'UTC'")
                : "N/A";
        }

        private static string FormatNumber(double value)
        {
            return value.ToString("0.##");
        }

        private static string FormatNumber(float value)
        {
            return value.ToString("0.##");
        }

        private static string Encode(string? value)
        {
            return WebUtility.HtmlEncode(value ?? string.Empty);
        }
    }
}
