using BLL.Interfaces;
using Core.DTOs.Requests;
using Core.DTOs.Responses;
using Core.Interfaces;
using Core.Constants;
using Core.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using ClosedXML.Excel;

namespace BLL.Services
{
    public class UserService : IUserService
    {
        private static readonly char[] UppercasePasswordChars = "ABCDEFGHJKLMNPQRSTUVWXYZ".ToCharArray();
        private static readonly char[] LowercasePasswordChars = "abcdefghijkmnopqrstuvwxyz".ToCharArray();
        private static readonly char[] DigitPasswordChars = "23456789".ToCharArray();
        private static readonly char[] SpecialPasswordChars = "!@#$%^&*".ToCharArray();
        private static readonly char[] AllPasswordChars = UppercasePasswordChars
            .Concat(LowercasePasswordChars)
            .Concat(DigitPasswordChars)
            .Concat(SpecialPasswordChars)
            .ToArray();

        private readonly IUserRepository _userRepository;
        private readonly IRefreshTokenRepository _refreshTokenRepository;
        private readonly IConfiguration _configuration;
        private readonly IActivityLogService _logService;
        private readonly IAssignmentRepository _assignmentRepo;
        private readonly IProjectRepository _projectRepo;
        private readonly IRepository<GlobalUserBanRequest> _globalBanRequestRepo;
        private readonly IRepository<AppNotification> _appNotificationRepo;
        private readonly IAppNotificationService _notification;
        private readonly IWorkflowEmailService _workflowEmailService;
        public UserService(
            IUserRepository userRepository,
            IRefreshTokenRepository refreshTokenRepository,
            IConfiguration configuration,
            IAssignmentRepository assignmentRepo,
            IActivityLogService logService,
            IProjectRepository projectRepo,
            IRepository<GlobalUserBanRequest> globalBanRequestRepo,
            IRepository<AppNotification> appNotificationRepo,
            IAppNotificationService notification,
            IWorkflowEmailService workflowEmailService)
        {
            _userRepository = userRepository;
            _refreshTokenRepository = refreshTokenRepository;
            _configuration = configuration;
            _assignmentRepo = assignmentRepo;
            _logService = logService;
            _projectRepo = projectRepo;
            _globalBanRequestRepo = globalBanRequestRepo;
            _appNotificationRepo = appNotificationRepo;
            _notification = notification;
            _workflowEmailService = workflowEmailService;
        }

        public async Task<User> RegisterAsync(string fullName, string email, string password, string role, string? managerId = null)
        {
            var (user, manager) = await CreateUserRecordAsync(fullName, email, password, role, managerId);

            await RunUserSideEffectSafelyAsync(
                user.Id,
                "WelcomeEmailError",
                user.Id,
                $"Failed to send welcome email to {user.Email}.",
                () => _workflowEmailService.SendWelcomeEmailAsync(user, manager));

            return user;
        }

        public async Task<EmailDispatchStatusResponse> CreateManagedUserAsync(string adminId, string fullName, string email, string role, string? managerId = null)
        {
            var temporaryPassword = GenerateTemporaryPassword();
            var (user, manager) = await CreateUserRecordAsync(
                fullName,
                email,
                temporaryPassword,
                role,
                managerId,
                adminId,
                $"Admin created account for {email} with role {role}.");

            try
            {
                await _workflowEmailService.SendWelcomeEmailAsync(user, manager, temporaryPassword);

                var emailDeliveryMode = GetEmailDeliveryMode();
                var emailDeliveryTarget = GetEmailDeliveryTarget();
                return new EmailDispatchStatusResponse
                {
                    Message = IsPickupDirectoryDelivery(emailDeliveryMode)
                        ? $"User created successfully. In this Development environment, the welcome email containing the temporary password was written to the local mail-drop folder ({emailDeliveryTarget}) instead of being sent to the user's real inbox."
                        : "User created successfully and a temporary password was delivered by email.",
                    EmailDelivered = true,
                    NotificationDelivered = true,
                    EmailDeliveryMode = emailDeliveryMode,
                    EmailDeliveryTarget = emailDeliveryTarget
                };
            }
            catch (Exception ex)
            {
                await SafeLogUserSideEffectFailureAsync(
                    adminId,
                    "WelcomeEmailError",
                    user.Id,
                    $"User {user.Email} was created, but the temporary password email could not be delivered. {ex.Message}");

                return new EmailDispatchStatusResponse
                {
                    Message = "User was created, but the temporary password email could not be delivered. Please verify SMTP settings and use Admin reset password to generate a new temporary password.",
                    EmailDelivered = false,
                    NotificationDelivered = true,
                    EmailDeliveryMode = GetEmailDeliveryMode(),
                    EmailDeliveryTarget = GetEmailDeliveryTarget()
                };
            }
        }

        private async Task<(User User, User? Manager)> CreateUserRecordAsync(
            string fullName,
            string email,
            string password,
            string role,
            string? managerId = null,
            string? actorUserId = null,
            string? logDescription = null)
        {
            if (!UserRoles.IsValid(role))
                throw new Exception("Invalid role.");

            if (await _userRepository.IsEmailExistsAsync(email))
                throw new Exception("Email already exists.");

            if (role == UserRoles.Admin)
            {
                bool hasExistingAdmin = await _userRepository.HasAdminRoleAsync();
                if (hasExistingAdmin)
                {
                    throw new Exception("BR-ADM-27: Only one Admin account is allowed in the system. An Admin already exists.");
                }
            }

            var user = new User
            {
                FullName = fullName,
                Email = email,
                Role = role,
                ManagerId = managerId,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                IsEmailVerified = true
            };

            await _userRepository.AddAsync(user);
            await _userRepository.SaveChangesAsync();

            await _logService.LogActionAsync(
                actorUserId ?? user.Id,
                "CreateUser",
                "User",
                user.Id,
                logDescription ?? $"Account created with role {role}."
            );

            var manager = string.IsNullOrWhiteSpace(user.ManagerId)
                ? null
                : await _userRepository.GetByIdAsync(user.ManagerId);

            return (user, manager);
        }
        public async Task UpdateAvatarAsync(string userId, string avatarUrl)
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null) throw new Exception("User not found");

            user.AvatarUrl = avatarUrl;
            _userRepository.Update(user);
            await _userRepository.SaveChangesAsync();
            await _logService.LogActionAsync(userId, "UpdateAvatar", "User", userId, "User updated their avatar.");
        }

        public async Task<string> UploadAvatarAsync(string userId, Stream content, string originalFileName)
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null) throw new Exception("User not found");

            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "avatars");
            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }

            var fileExtension = Path.GetExtension(originalFileName);
            var uniqueFileName = $"{Guid.NewGuid()}{fileExtension}";
            var filePath = Path.Combine(uploadsFolder, uniqueFileName);

            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await content.CopyToAsync(fileStream);
            }

            var avatarUrl = $"/avatars/{uniqueFileName}";
            await UpdateAvatarAsync(userId, avatarUrl);
            return avatarUrl;
        }

        public async Task<(string? accessToken, string? refreshToken)> LoginAsync(string email, string password)
        {
            var user = await _userRepository.GetUserByEmailAsync(email);
            if (user == null) return (null, null);
            if (!user.IsActive)
            {
                throw new UnauthorizedAccessException("Account is deactivated or banned.");
            }
            if (string.IsNullOrEmpty(user.PasswordHash)) return (null, null);

            bool isValidPassword;
            try
            {
                isValidPassword = BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);
            }
            catch (BCrypt.Net.SaltParseException)
            {
                return (null, null);
            }

            if (!isValidPassword) return (null, null);

            await _refreshTokenRepository.RevokeAllUserTokensAsync(user.Id);

            var accessToken = GenerateJwtToken(user, expiresInMinutes: 30);
            var refreshTokenValue = await GenerateRefreshToken(user.Id);

            return (accessToken, refreshTokenValue);
        }

        public async Task<(string? accessToken, string? refreshToken)> RefreshTokenAsync(string refreshTokenString)
        {
            var refreshToken = await _refreshTokenRepository.GetByTokenAsync(refreshTokenString);

            if (refreshToken == null || !refreshToken.IsActive)
            {
                return (null, null);
            }

            var user = await _userRepository.GetByIdAsync(refreshToken.UserId);
            if (user == null || !user.IsActive)
            {
                return (null, null);
            }

            refreshToken.RevokedAt = DateTime.UtcNow;
            await _refreshTokenRepository.SaveChangesAsync();

            var newAccessToken = GenerateJwtToken(user, expiresInMinutes: 30);
            var newRefreshTokenValue = await GenerateRefreshToken(user.Id);

            return (newAccessToken, newRefreshTokenValue);
        }

        public async Task RevokeRefreshTokenAsync(string userId)
        {
            await _refreshTokenRepository.RevokeAllUserTokensAsync(userId);
        }

        private async Task<string> GenerateRefreshToken(string userId)
        {
            var randomBytes = new byte[64];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(randomBytes);

            var refreshToken = new RefreshToken
            {
                UserId = userId,
                Token = Convert.ToBase64String(randomBytes),
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                CreatedAt = DateTime.UtcNow
            };

            await _refreshTokenRepository.AddAsync(refreshToken);
            await _refreshTokenRepository.SaveChangesAsync();

            return refreshToken.Token;
        }

        public async Task<User?> GetUserByIdAsync(string id)
        {
            return await _userRepository.GetByIdAsync(id);
        }

        public async Task<User?> GetUserByEmailAsync(string email)
        {
            return await _userRepository.GetUserByEmailAsync(email);
        }

        public async Task<bool> IsEmailExistsAsync(string email)
        {
            return await _userRepository.IsEmailExistsAsync(email);
        }

        private static bool IsUnfinishedProject(Project project)
        {
            return !string.Equals(project.Status, ProjectStatusConstants.Completed, StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(project.Status, ProjectStatusConstants.Archived, StringComparison.OrdinalIgnoreCase);
        }

        private static Dictionary<string, HashSet<int>> BuildAnnotatorProjectMap(IEnumerable<Assignment> assignments)
        {
            return assignments
                .Where(a => !string.IsNullOrWhiteSpace(a.AnnotatorId))
                .GroupBy(a => a.AnnotatorId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(a => a.ProjectId).ToHashSet(),
                    StringComparer.OrdinalIgnoreCase);
        }

        private static Dictionary<string, HashSet<int>> BuildReviewerProjectMap(IEnumerable<Assignment> assignments)
        {
            return assignments
                .Where(a => !string.IsNullOrWhiteSpace(a.ReviewerId))
                .GroupBy(a => a.ReviewerId!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(a => a.ProjectId).ToHashSet(),
                    StringComparer.OrdinalIgnoreCase);
        }

        private static HashSet<int> GetProjectIdsForUser(
            User user,
            IReadOnlyDictionary<string, HashSet<int>> annotatorProjectMap,
            IReadOnlyDictionary<string, HashSet<int>> reviewerProjectMap,
            IReadOnlyCollection<Project> allProjects)
        {
            if (string.Equals(user.Role, UserRoles.Manager, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(user.Role, UserRoles.Admin, StringComparison.OrdinalIgnoreCase))
            {
                return allProjects
                    .Where(project => string.Equals(project.ManagerId, user.Id, StringComparison.OrdinalIgnoreCase))
                    .Select(project => project.Id)
                    .ToHashSet();
            }

            if (string.Equals(user.Role, UserRoles.Reviewer, StringComparison.OrdinalIgnoreCase))
            {
                return reviewerProjectMap.TryGetValue(user.Id, out var reviewerProjects)
                    ? reviewerProjects
                    : new HashSet<int>();
            }

            return annotatorProjectMap.TryGetValue(user.Id, out var annotatorProjects)
                ? annotatorProjects
                : new HashSet<int>();
        }

        private async Task<HashSet<string>> GetPendingGlobalBanUserIdsAsync(IEnumerable<string> userIds)
        {
            var normalizedUserIds = userIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!normalizedUserIds.Any())
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            var pendingRequests = await _globalBanRequestRepo.FindAsync(request =>
                normalizedUserIds.Contains(request.TargetUserId) &&
                request.Status == GlobalUserBanRequestStatusConstants.Pending);

            return pendingRequests
                .Select(request => request.TargetUserId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static List<UserProjectSummaryResponse> BuildProjectSummaries(IEnumerable<Project> projects)
        {
            return projects
                .OrderBy(project => project.Name)
                .Select(project => new UserProjectSummaryResponse
                {
                    Id = project.Id,
                    Name = project.Name ?? string.Empty,
                    Status = project.Status ?? string.Empty
                })
                .ToList();
        }

        private static UserResponse MapUserResponse(
            User user,
            IReadOnlyDictionary<string, User> userLookup,
            IReadOnlyDictionary<string, HashSet<int>> annotatorProjectMap,
            IReadOnlyDictionary<string, HashSet<int>> reviewerProjectMap,
            IReadOnlyCollection<Project> allProjects,
            IReadOnlySet<int> unfinishedProjectIds,
            IReadOnlySet<string> pendingGlobalBanUserIds)
        {
            var projectIds = GetProjectIdsForUser(user, annotatorProjectMap, reviewerProjectMap, allProjects);
            var unfinishedProjects = BuildProjectSummaries(
                allProjects.Where(project =>
                    projectIds.Contains(project.Id) &&
                    unfinishedProjectIds.Contains(project.Id)));

            var unfinishedProjectManagerIds = allProjects
                .Where(project =>
                    projectIds.Contains(project.Id) &&
                    unfinishedProjectIds.Contains(project.Id) &&
                    !string.IsNullOrWhiteSpace(project.ManagerId))
                .Select(project => project.ManagerId!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            User? manager = null;
            string? managerId = user.ManagerId;

            if (unfinishedProjectManagerIds.Count == 1)
            {
                managerId = unfinishedProjectManagerIds[0];
                userLookup.TryGetValue(managerId, out manager);
            }
            else if (unfinishedProjectManagerIds.Count == 0)
            {
                userLookup.TryGetValue(user.ManagerId ?? string.Empty, out manager);
            }
            else
            {
                managerId = null;
            }

            return new UserResponse
            {
                Id = user.Id,
                FullName = user.FullName ?? "",
                Email = user.Email ?? "",
                Role = user.Role ?? "",
                AvatarUrl = user.AvatarUrl ?? "",
                IsActive = user.IsActive,
                ManagerId = managerId,
                ManagerName = manager?.FullName,
                ManagerEmail = manager?.Email,
                TotalProjects = projectIds.Count,
                UnfinishedProjectCount = unfinishedProjects.Count,
                UnfinishedProjects = unfinishedProjects,
                HasPendingGlobalBanRequest = pendingGlobalBanUserIds.Contains(user.Id)
            };
        }

        private async Task<List<Project>> GetUnfinishedProjectsForUserAsync(User user)
        {
            var allProjects = (await _projectRepo.GetAllAsync()).ToList();
            var allAssignments = (await _assignmentRepo.GetAllAsync()).ToList();
            var unfinishedProjectIds = allProjects
                .Where(IsUnfinishedProject)
                .Select(project => project.Id)
                .ToHashSet();

            IEnumerable<int> userProjectIds;

            if (string.Equals(user.Role, UserRoles.Reviewer, StringComparison.OrdinalIgnoreCase))
            {
                userProjectIds = allAssignments
                    .Where(assignment => string.Equals(assignment.ReviewerId, user.Id, StringComparison.OrdinalIgnoreCase))
                    .Select(assignment => assignment.ProjectId)
                    .Distinct();
            }
            else
            {
                userProjectIds = allAssignments
                    .Where(assignment => string.Equals(assignment.AnnotatorId, user.Id, StringComparison.OrdinalIgnoreCase))
                    .Select(assignment => assignment.ProjectId)
                    .Distinct();
            }

            return allProjects
                .Where(project => unfinishedProjectIds.Contains(project.Id) && userProjectIds.Contains(project.Id))
                .OrderBy(project => project.Name)
                .ToList();
        }

        private async Task<(User Manager, List<Project> Projects)> ResolveResponsibleManagerForGlobalBanAsync(
            User user,
            IEnumerable<Project> unfinishedProjects)
        {
            var projectList = unfinishedProjects
                .Where(project => project != null)
                .OrderBy(project => project.Name)
                .ToList();

            if (!projectList.Any())
            {
                throw new Exception("This user no longer participates in unfinished projects that require manager approval.");
            }

            var responsibleManagerIds = projectList
                .Where(project => !string.IsNullOrWhiteSpace(project.ManagerId))
                .Select(project => project.ManagerId!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!responsibleManagerIds.Any())
            {
                throw new Exception(
                    $"Cannot create the global ban request because {user.FullName} is still assigned to unfinished project(s) without a responsible manager. " +
                    "A manager can only ban a user within their own project. Please assign the correct project manager or remove the user from those projects first.");
            }

            if (responsibleManagerIds.Count > 1)
            {
                var managerNames = new List<string>();
                foreach (var responsibleManagerId in responsibleManagerIds)
                {
                    var responsibleManager = await _userRepository.GetByIdAsync(responsibleManagerId);
                    managerNames.Add(responsibleManager?.FullName ?? responsibleManagerId);
                }

                throw new Exception(
                    $"{user.FullName} is still participating in unfinished projects owned by multiple managers ({string.Join(", ", managerNames)}). " +
                    "Managers can only remove users from their own projects. Please let the relevant project managers handle those project memberships first, then submit the global ban again.");
            }

            var managerId = responsibleManagerIds[0];
            var manager = await _userRepository.GetByIdAsync(managerId);
            if (manager == null || !string.Equals(manager.Role, UserRoles.Manager, StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("Cannot create the global ban request because the responsible project manager could not be found.");
            }

            return (
                manager,
                projectList
                    .Where(project => string.Equals(project.ManagerId, manager.Id, StringComparison.OrdinalIgnoreCase))
                    .ToList());
        }

        private static string BuildGlobalBanRequestMetadata(
            GlobalUserBanRequest banRequest,
            User targetUser,
            User admin,
            User manager,
            IEnumerable<Project> unfinishedProjects,
            string requestStatus,
            string? decisionNote = null,
            DateTime? resolvedAt = null)
        {
            var projectSummaries = BuildProjectSummaries(unfinishedProjects);
            var normalizedProjectSummaries = projectSummaries
                .Select(project => new
                {
                    id = project.Id,
                    name = project.Name,
                    status = project.Status
                })
                .ToList();

            return JsonSerializer.Serialize(new
            {
                banRequestId = banRequest.Id,
                targetUserId = targetUser.Id,
                targetUserName = targetUser.FullName,
                targetUserEmail = targetUser.Email,
                targetUserRole = targetUser.Role,
                requestedByAdminId = admin.Id,
                requestedByAdminName = admin.FullName,
                requestedByAdminEmail = admin.Email,
                managerId = manager.Id,
                managerName = manager.FullName,
                managerEmail = manager.Email,
                unfinishedProjectCount = projectSummaries.Count,
                unfinishedProjects = normalizedProjectSummaries,
                requestStatus,
                requestedAt = NormalizeUtcDateTime(banRequest.RequestedAt).ToString("O"),
                resolvedAt = resolvedAt.HasValue
                    ? NormalizeUtcDateTime(resolvedAt.Value).ToString("O")
                    : null,
                decisionNote
            });
        }

        private static string UpdateGlobalBanNotificationMetadata(
            string? metadataJson,
            string requestStatus,
            string? decisionNote,
            DateTime? resolvedAt)
        {
            JsonObject metadata;

            try
            {
                metadata = JsonNode.Parse(metadataJson ?? "{}")?.AsObject() ?? new JsonObject();
            }
            catch
            {
                metadata = new JsonObject();
            }

            metadata["requestStatus"] = requestStatus;
            metadata["decisionNote"] = decisionNote;
            metadata["resolvedAt"] = resolvedAt.HasValue
                ? NormalizeUtcDateTime(resolvedAt.Value).ToString("O")
                : null;

            return metadata.ToJsonString();
        }

        private static DateTime NormalizeUtcDateTime(DateTime value)
        {
            if (value == default)
            {
                return DateTime.SpecifyKind(value, DateTimeKind.Utc);
            }

            return value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
            };
        }

        private async Task SyncGlobalBanRequestNotificationsAsync(
            GlobalUserBanRequest banRequest,
            string requestStatus,
            string? decisionNote)
        {
            var notifications = (await _appNotificationRepo.FindAsync(notification =>
                notification.ReferenceType == "GlobalUserBanRequest" &&
                notification.ReferenceId == banRequest.Id.ToString()))
                .ToList();

            if (!notifications.Any())
            {
                return;
            }

            foreach (var notification in notifications)
            {
                notification.MetadataJson = UpdateGlobalBanNotificationMetadata(
                    notification.MetadataJson,
                    requestStatus,
                    decisionNote,
                    banRequest.ResolvedAt);

                if (!string.Equals(requestStatus, GlobalUserBanRequestStatusConstants.Pending, StringComparison.OrdinalIgnoreCase))
                {
                    notification.IsRead = true;
                }

                _appNotificationRepo.Update(notification);
            }

            await _appNotificationRepo.SaveChangesAsync();
        }

        private async Task CloseObsoletePendingGlobalBanRequestsAsync(
            IEnumerable<GlobalUserBanRequest> obsoleteRequests,
            string actorUserId,
            string targetUserEmail,
            string responsibleManagerName)
        {
            var obsoleteRequestList = obsoleteRequests.ToList();
            if (!obsoleteRequestList.Any())
            {
                return;
            }

            var resolvedAt = DateTime.UtcNow;
            var decisionNote =
                $"Automatically closed because the affected unfinished projects are now owned by manager {responsibleManagerName}.";

            foreach (var obsoleteRequest in obsoleteRequestList)
            {
                obsoleteRequest.Status = GlobalUserBanRequestStatusConstants.Rejected;
                obsoleteRequest.DecisionNote = decisionNote;
                obsoleteRequest.ResolvedAt = resolvedAt;
                _globalBanRequestRepo.Update(obsoleteRequest);
            }

            await _globalBanRequestRepo.SaveChangesAsync();

            foreach (var obsoleteRequest in obsoleteRequestList)
            {
                await SyncGlobalBanRequestNotificationsAsync(
                    obsoleteRequest,
                    obsoleteRequest.Status,
                    obsoleteRequest.DecisionNote);
            }

            await _logService.LogActionAsync(
                actorUserId,
                "CloseObsoleteGlobalBanRequest",
                "GlobalUserBanRequest",
                string.Join(",", obsoleteRequestList.Select(request => request.Id)),
                $"Closed {obsoleteRequestList.Count} obsolete pending global ban request(s) for user {targetUserEmail} before creating the updated request.");
        }

        private static string ComputeDataItemStatus(IEnumerable<Assignment> remainingAssignments)
        {
            var assignments = remainingAssignments.ToList();

            if (!assignments.Any())
            {
                return TaskStatusConstants.New;
            }

            if (assignments.Any(assignment => string.Equals(assignment.Status, TaskStatusConstants.Approved, StringComparison.OrdinalIgnoreCase)))
            {
                return TaskStatusConstants.Approved;
            }

            if (assignments.Any(assignment => string.Equals(assignment.Status, TaskStatusConstants.Submitted, StringComparison.OrdinalIgnoreCase)))
            {
                return TaskStatusConstants.Submitted;
            }

            if (assignments.Any(assignment => string.Equals(assignment.Status, TaskStatusConstants.InProgress, StringComparison.OrdinalIgnoreCase)))
            {
                return TaskStatusConstants.InProgress;
            }

            if (assignments.Any(assignment => string.Equals(assignment.Status, TaskStatusConstants.Rejected, StringComparison.OrdinalIgnoreCase)))
            {
                return TaskStatusConstants.Rejected;
            }

            return TaskStatusConstants.Assigned;
        }

        private async Task<List<(Project Project, int ChangedAssignments)>> RemoveUserFromUnfinishedProjectsAsync(User user)
        {
            var affectedProjects = await GetUnfinishedProjectsForUserAsync(user);
            var removalResults = new List<(Project Project, int ChangedAssignments)>();

            foreach (var projectSummary in affectedProjects)
            {
                var project = await _projectRepo.GetProjectWithDetailsAsync(projectSummary.Id);
                if (project == null)
                {
                    continue;
                }

                var changedAssignments = 0;

                foreach (var dataItem in project.DataItems)
                {
                    var projectAssignments = dataItem.Assignments.ToList();

                    if (string.Equals(user.Role, UserRoles.Reviewer, StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var assignment in projectAssignments.Where(assignment =>
                                     string.Equals(assignment.ReviewerId, user.Id, StringComparison.OrdinalIgnoreCase) &&
                                     !string.Equals(assignment.Status, TaskStatusConstants.Approved, StringComparison.OrdinalIgnoreCase) &&
                                     !string.Equals(assignment.Status, TaskStatusConstants.Rejected, StringComparison.OrdinalIgnoreCase)))
                        {
                            assignment.ReviewerId = null;
                            _assignmentRepo.Update(assignment);
                            changedAssignments++;
                        }
                    }
                    else
                    {
                        var assignmentsToDelete = projectAssignments
                            .Where(assignment =>
                                string.Equals(assignment.AnnotatorId, user.Id, StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(assignment.Status, TaskStatusConstants.Approved, StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        if (assignmentsToDelete.Any())
                        {
                            var removedAssignmentIds = assignmentsToDelete
                                .Select(assignment => assignment.Id)
                                .ToHashSet();

                            foreach (var assignment in assignmentsToDelete)
                            {
                                _assignmentRepo.Delete(assignment);
                                changedAssignments++;
                            }

                            dataItem.Status = ComputeDataItemStatus(
                                projectAssignments.Where(assignment => !removedAssignmentIds.Contains(assignment.Id)));
                        }
                    }
                }

                if (changedAssignments > 0)
                {
                    _projectRepo.Update(project);
                    removalResults.Add((project, changedAssignments));
                }
            }

            if (removalResults.Any())
            {
                await _assignmentRepo.SaveChangesAsync();
                await _projectRepo.SaveChangesAsync();
            }

            return removalResults;
        }

        public async Task<PagedResponse<UserResponse>> GetAllUsersAsync(int page, int pageSize)
        {
            var allUsers = (await _userRepository.GetAllAsync()).ToList();
            var allProjects = (await _projectRepo.GetAllAsync()).ToList();
            var allAssignments = (await _assignmentRepo.GetAllAsync()).ToList();
            var userLookup = allUsers
                .Where(user => !string.IsNullOrWhiteSpace(user.Id))
                .ToDictionary(user => user.Id, StringComparer.OrdinalIgnoreCase);
            var pendingGlobalBanUserIds = await GetPendingGlobalBanUserIdsAsync(allUsers.Select(user => user.Id));
            var unfinishedProjectIds = allProjects
                .Where(IsUnfinishedProject)
                .Select(project => project.Id)
                .ToHashSet();
            var annotatorProjectMap = BuildAnnotatorProjectMap(allAssignments);
            var reviewerProjectMap = BuildReviewerProjectMap(allAssignments);

            var totalCount = allUsers.Count();
            var stats = new
            {
                TotalAdmins = allUsers.Count(u => u.Role == UserRoles.Admin),
                TotalWorkers = allUsers.Count(u => u.Role != UserRoles.Admin)
            };

            var items = allUsers
                .OrderByDescending(u => u.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(user => MapUserResponse(
                    user,
                    userLookup,
                    annotatorProjectMap,
                    reviewerProjectMap,
                    allProjects,
                    unfinishedProjectIds,
                    pendingGlobalBanUserIds))
                .ToList();

            return new PagedResponse<UserResponse>
            {
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                Stats = stats,
                Items = items
            };
        }

        public async Task<List<UserResponse>> GetManagedUsersAsync(string managerId)
        {
            var allUsers = (await _userRepository.GetAllAsync()).ToList();
            var allProjects = (await _projectRepo.GetAllAsync()).ToList();
            var allAssignments = (await _assignmentRepo.GetAllAsync()).ToList();
            var userLookup = allUsers
                .Where(user => !string.IsNullOrWhiteSpace(user.Id))
                .ToDictionary(user => user.Id, StringComparer.OrdinalIgnoreCase);
            var unfinishedProjectIds = allProjects
                .Where(IsUnfinishedProject)
                .Select(project => project.Id)
                .ToHashSet();
            var annotatorProjectMap = BuildAnnotatorProjectMap(allAssignments);
            var reviewerProjectMap = BuildReviewerProjectMap(allAssignments);
            var pendingGlobalBanUserIds = await GetPendingGlobalBanUserIdsAsync(allUsers.Select(user => user.Id));
            var managedUsers = allUsers
                .Where(u => u.ManagerId == managerId && u.IsActive)
                .Select(user => MapUserResponse(
                    user,
                    userLookup,
                    annotatorProjectMap,
                    reviewerProjectMap,
                    allProjects,
                    unfinishedProjectIds,
                    pendingGlobalBanUserIds))
                .ToList();

            return managedUsers;
        }

        public async Task<List<UserResponse>> GetAllUsersNoPagingAsync()
        {
            var allUsers = (await _userRepository.GetAllAsync()).ToList();
            var allProjects = (await _projectRepo.GetAllAsync()).ToList();
            var allAssignments = (await _assignmentRepo.GetAllAsync()).ToList();
            var userLookup = allUsers
                .Where(user => !string.IsNullOrWhiteSpace(user.Id))
                .ToDictionary(user => user.Id, StringComparer.OrdinalIgnoreCase);
            var unfinishedProjectIds = allProjects
                .Where(IsUnfinishedProject)
                .Select(project => project.Id)
                .ToHashSet();
            var annotatorProjectMap = BuildAnnotatorProjectMap(allAssignments);
            var reviewerProjectMap = BuildReviewerProjectMap(allAssignments);
            var pendingGlobalBanUserIds = await GetPendingGlobalBanUserIdsAsync(allUsers.Select(user => user.Id));

            return allUsers
                .Select(user => MapUserResponse(
                    user,
                    userLookup,
                    annotatorProjectMap,
                    reviewerProjectMap,
                    allProjects,
                    unfinishedProjectIds,
                    pendingGlobalBanUserIds))
                .ToList();
        }

        public async Task UpdateUserAsync(string userId, string actorId, UpdateUserRequest request)
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null) throw new Exception("User not found");

            var actor = await _userRepository.GetByIdAsync(actorId);
            bool isActorAdmin = actor != null && actor.Role == UserRoles.Admin;

            if (isActorAdmin)
            {
                if (!string.IsNullOrEmpty(request.FullName))
                    throw new Exception("BR-ADM-28: Admin cannot modify user's FullName. Contact Manager for such changes.");
                if (!string.IsNullOrEmpty(request.Email))
                    throw new Exception("BR-ADM-28: Admin cannot modify user's Email. Contact Manager for such changes.");
            }

            if (!string.IsNullOrEmpty(request.Email) && user.IsEmailVerified)
            {
                throw new Exception("BR-ADM-24: Cannot modify email after account activation.");
            }

            if (!string.IsNullOrEmpty(request.FullName)) user.FullName = request.FullName;
            if (!string.IsNullOrEmpty(request.Email))
            {
                if (user.Email != request.Email && await _userRepository.IsEmailExistsAsync(request.Email))
                    throw new Exception("Email already exists.");
                user.Email = request.Email;
            }
            if (!string.IsNullOrEmpty(request.Role) && request.Role != user.Role)
            {
                if (!UserRoles.IsValid(request.Role)) throw new Exception("Invalid role.");
                bool hasPendingTasks = await _assignmentRepo.HasPendingTasksAsync(user.Id, user.Role);
                if (hasPendingTasks)
                {
                    throw new Exception($"Cannot change role. This user still has unfinished tasks as an {user.Role}.");
                }

                string oldRole = user.Role;
                user.Role = request.Role;

                await _notification.SendNotificationAsync(
                    userId,
                    $"Your account role has been changed from {oldRole} to {request.Role} by an Administrator.",
                    "RoleChange"
                );
            }
            if (!string.IsNullOrEmpty(request.Password))
            {
                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
            }

            if (request.ManagerId != null)
            {
                user.ManagerId = string.IsNullOrEmpty(request.ManagerId) ? null : request.ManagerId;
            }

            _userRepository.Update(user);
            await _userRepository.SaveChangesAsync();
            await _logService.LogActionAsync(userId, "UpdateUser", "User", userId, "Admin updated user details.");
        }
        public async Task ChangePasswordAsync(string userId, string oldPassword, string newPassword)
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null) throw new Exception("User not found");
            if (!BCrypt.Net.BCrypt.Verify(oldPassword, user.PasswordHash))
            {
                throw new Exception("Old password is incorrect.");
            }

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            _userRepository.Update(user);
            await _userRepository.SaveChangesAsync();
            await _logService.LogActionAsync(userId, "ChangePassword", "User", userId, "User changed their password.");
        }

        public async Task UpdateProfileAsync(string userId, UpdateProfileRequest request)
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null) throw new Exception("User not found");

            if (!string.IsNullOrEmpty(request.FullName)) user.FullName = request.FullName;
            if (!string.IsNullOrEmpty(request.AvatarUrl)) user.AvatarUrl = request.AvatarUrl;

            _userRepository.Update(user);
            await _userRepository.SaveChangesAsync();
            await _logService.LogActionAsync(userId, "UpdateProfile", "User", userId, "User updated their profile information.");
        }

        public async Task DeleteUserAsync(string userId)
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null) throw new Exception("User not found");
            bool hasPendingTasks = await _assignmentRepo.HasPendingTasksAsync(user.Id, user.Role);
            if (hasPendingTasks)
            {
                throw new Exception($"Cannot deactivate this user. They still have unfinished tasks as an {user.Role}. Please reassign or complete their tasks first.");
            }
            user.IsActive = false;
            _userRepository.Update(user);
            await _userRepository.SaveChangesAsync();

            await _logService.LogActionAsync(userId, "DeleteUser", "User", userId, "Admin deactivated the user account.");
        }

        public async Task<ToggleUserStatusResponse> ToggleUserStatusAsync(string userId, bool isActive, string? adminId = null)
        {
            if (!string.IsNullOrEmpty(adminId) && userId == adminId && !isActive)
            {
                throw new Exception("BR-ADM-19: Admin cannot block themselves.");
            }

            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null) throw new Exception("User not found");

            var admin = string.IsNullOrWhiteSpace(adminId)
                ? null
                : await _userRepository.GetByIdAsync(adminId);

            if (!isActive)
            {
                var unfinishedProjects = await GetUnfinishedProjectsForUserAsync(user);
                var existingPendingRequests = (await _globalBanRequestRepo.FindAsync(request =>
                    request.TargetUserId == user.Id &&
                    request.Status == GlobalUserBanRequestStatusConstants.Pending))
                    .ToList();

                var requiresManagerApproval =
                    admin != null &&
                    string.Equals(admin.Role, UserRoles.Admin, StringComparison.OrdinalIgnoreCase) &&
                    (string.Equals(user.Role, UserRoles.Annotator, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(user.Role, UserRoles.Reviewer, StringComparison.OrdinalIgnoreCase)) &&
                    unfinishedProjects.Any();

                if (requiresManagerApproval)
                {
                    var (manager, managerProjects) = await ResolveResponsibleManagerForGlobalBanAsync(user, unfinishedProjects);
                    var existingPendingRequest = existingPendingRequests
                        .FirstOrDefault(request => string.Equals(request.ManagerId, manager.Id, StringComparison.OrdinalIgnoreCase));

                    if (existingPendingRequest != null)
                    {
                        return new ToggleUserStatusResponse
                        {
                            Message = $"A global ban request for {user.FullName} is already waiting for manager approval.",
                            IsActive = user.IsActive,
                            RequiresManagerApproval = true,
                            GlobalBanRequestId = existingPendingRequest.Id
                        };
                    }

                    if (existingPendingRequests.Any())
                    {
                        await CloseObsoletePendingGlobalBanRequestsAsync(
                            existingPendingRequests,
                            admin!.Id,
                            user.Email,
                            manager.FullName);
                    }

                    var banRequest = new GlobalUserBanRequest
                    {
                        TargetUserId = user.Id,
                        RequestedByAdminId = admin!.Id,
                        ManagerId = manager.Id,
                        Status = GlobalUserBanRequestStatusConstants.Pending,
                        RequestedAt = DateTime.UtcNow
                    };

                    await _globalBanRequestRepo.AddAsync(banRequest);
                    await _globalBanRequestRepo.SaveChangesAsync();

                    var metadataJson = BuildGlobalBanRequestMetadata(
                        banRequest,
                        user,
                        admin,
                        manager,
                        managerProjects,
                        GlobalUserBanRequestStatusConstants.Pending);

                    var projectSummary = string.Join(", ", managerProjects.Select(project => $"\"{project.Name}\""));

                    await _notification.SendNotificationAsync(
                        manager.Id,
                        $"Admin requested a global ban for {user.Role} \"{user.FullName}\" ({user.Email}). This user is still participating in {managerProjects.Count} unfinished project(s) that you manage: {projectSummary}. Please approve or reject the request.",
                        "GlobalUserBanApproval",
                        "GlobalUserBanRequest",
                        banRequest.Id.ToString(),
                        "ResolveGlobalUserBanRequest",
                        metadataJson);

                    await _logService.LogActionAsync(
                        admin.Id,
                        "CreateGlobalUserBanRequest",
                        "GlobalUserBanRequest",
                        banRequest.Id.ToString(),
                        $"Admin requested manager approval to globally ban user {user.Email} while they still participate in {managerProjects.Count} unfinished project(s) owned by manager {manager.FullName}.");

                    return new ToggleUserStatusResponse
                    {
                        Message = $"The global ban request for {user.FullName} has been sent to project manager {manager.FullName} for approval.",
                        IsActive = user.IsActive,
                        RequiresManagerApproval = true,
                        GlobalBanRequestId = banRequest.Id
                    };
                }

                bool hasPendingTasks = await _assignmentRepo.HasPendingTasksAsync(user.Id, user.Role);
                if (hasPendingTasks)
                {
                    throw new Exception($"Cannot deactivate this user. They still have unfinished tasks as an {user.Role}. Please reassign or complete their tasks first.");
                }

                user.IsActive = false;
                _userRepository.Update(user);
                await _userRepository.SaveChangesAsync();

                await _logService.LogActionAsync(
                    adminId ?? userId,
                    "ToggleUserStatus",
                    "User",
                    userId,
                    "Admin deactivated the user account.");

                return new ToggleUserStatusResponse
                {
                    Message = $"User {user.FullName} has been deactivated successfully.",
                    IsActive = false,
                    RequiresManagerApproval = false
                };
            }

            user.IsActive = true;
            _userRepository.Update(user);
            await _userRepository.SaveChangesAsync();

            await _logService.LogActionAsync(
                adminId ?? userId,
                "ToggleUserStatus",
                "User",
                userId,
                "Admin activated the user account.");

            return new ToggleUserStatusResponse
            {
                Message = $"User {user.FullName} has been activated successfully.",
                IsActive = true,
                RequiresManagerApproval = false
            };
        }

        public async Task ResolveGlobalUserBanRequestAsync(int requestId, string managerId, ResolveGlobalUserBanRequest request)
        {
            List<(string UserId, string Message, string Type, string? ReferenceType, string? ReferenceId, string? ActionKey, string? MetadataJson)> pendingNotifications = new();
            var logEntries = new List<(string ActionType, string EntityName, string EntityId, string Description)>();

            await _globalBanRequestRepo.ExecuteInTransactionAsync(async () =>
            {
                var banRequest = await _globalBanRequestRepo.GetByIdAsync(requestId);
                if (banRequest == null)
                {
                    throw new Exception("Global ban request not found.");
                }

                if (!string.Equals(banRequest.ManagerId, managerId, StringComparison.OrdinalIgnoreCase))
                {
                    throw new UnauthorizedAccessException("Only the responsible manager can resolve this global ban request.");
                }

                if (!string.Equals(banRequest.Status, GlobalUserBanRequestStatusConstants.Pending, StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception("This global ban request has already been resolved.");
                }

                var manager = await _userRepository.GetByIdAsync(managerId) ?? new User
                {
                    Id = managerId,
                    FullName = "Manager",
                    Email = string.Empty,
                    Role = UserRoles.Manager
                };
                var targetUser = await _userRepository.GetByIdAsync(banRequest.TargetUserId)
                    ?? throw new Exception("Target user not found.");
                var admin = await _userRepository.GetByIdAsync(banRequest.RequestedByAdminId) ?? new User
                {
                    Id = banRequest.RequestedByAdminId,
                    FullName = "Administrator",
                    Email = string.Empty,
                    Role = UserRoles.Admin
                };

                var normalizedDecisionNote = string.IsNullOrWhiteSpace(request.DecisionNote)
                    ? null
                    : request.DecisionNote.Trim();
                var attemptNotifications = new List<(string UserId, string Message, string Type, string? ReferenceType, string? ReferenceId, string? ActionKey, string? MetadataJson)>();
                var attemptLogEntries = new List<(string ActionType, string EntityName, string EntityId, string Description)>();

                banRequest.Status = request.Approve
                    ? GlobalUserBanRequestStatusConstants.Approved
                    : GlobalUserBanRequestStatusConstants.Rejected;
                banRequest.DecisionNote = normalizedDecisionNote;
                banRequest.ResolvedAt = DateTime.UtcNow;

                _globalBanRequestRepo.Update(banRequest);
                await _globalBanRequestRepo.SaveChangesAsync();
                await SyncGlobalBanRequestNotificationsAsync(banRequest, banRequest.Status, normalizedDecisionNote);

                if (request.Approve)
                {
                    var removalResults = await RemoveUserFromUnfinishedProjectsAsync(targetUser);
                    targetUser.IsActive = false;
                    _userRepository.Update(targetUser);
                    await _userRepository.SaveChangesAsync();

                    var impactedProjectSummary = removalResults.Any()
                        ? string.Join(", ", removalResults.Select(result => $"\"{result.Project.Name}\""))
                        : "no unfinished projects";

                    attemptNotifications.Add((
                        admin.Id,
                        $"Manager {manager.FullName} approved the global ban for {targetUser.Role} \"{targetUser.FullName}\" ({targetUser.Email}). The account is now blocked from the system and removed from unfinished projects: {impactedProjectSummary}.",
                        "GlobalUserBanApproved",
                        "GlobalUserBanRequest",
                        banRequest.Id.ToString(),
                        null,
                        null));

                    attemptNotifications.Add((
                        targetUser.Id,
                        "Your account has been globally banned after manager approval. You have been removed from all unfinished projects and can no longer access the system.",
                        "AccountDeactivated",
                        "GlobalUserBanRequest",
                        banRequest.Id.ToString(),
                        null,
                        null));

                    foreach (var managerGroup in removalResults
                                 .Where(result => !string.IsNullOrWhiteSpace(result.Project.ManagerId) &&
                                                  !string.Equals(result.Project.ManagerId, managerId, StringComparison.OrdinalIgnoreCase))
                                 .GroupBy(result => result.Project.ManagerId!, StringComparer.OrdinalIgnoreCase))
                    {
                        var groupedProjectNames = string.Join(", ", managerGroup.Select(result => $"\"{result.Project.Name}\""));
                        var groupedAssignmentCount = managerGroup.Sum(result => result.ChangedAssignments);

                        attemptNotifications.Add((
                            managerGroup.Key,
                            $"User \"{targetUser.FullName}\" ({targetUser.Email}) was globally banned after manager approval and removed from your unfinished project(s) {groupedProjectNames}. {groupedAssignmentCount} pending assignment(s) were affected and may need reassignment.",
                            "UserRemoved",
                            null,
                            null,
                            null,
                            null));
                    }

                    attemptLogEntries.Add((
                        "ApproveGlobalUserBanRequest",
                        "GlobalUserBanRequest",
                        requestId.ToString(),
                        $"Manager approved the global ban for user {targetUser.Email}."));
                }
                else
                {
                    attemptNotifications.Add((
                        admin.Id,
                        $"Manager {manager.FullName} rejected the global ban request for {targetUser.Role} \"{targetUser.FullName}\" ({targetUser.Email}). The account remains active.",
                        "GlobalUserBanRejected",
                        "GlobalUserBanRequest",
                        banRequest.Id.ToString(),
                        null,
                        null));

                    attemptLogEntries.Add((
                        "RejectGlobalUserBanRequest",
                        "GlobalUserBanRequest",
                        requestId.ToString(),
                        $"Manager rejected the global ban for user {targetUser.Email}."));
                }

                pendingNotifications = attemptNotifications;
                logEntries = attemptLogEntries;
            });

            foreach (var notification in pendingNotifications)
            {
                await RunSideEffectSafelyAsync(
                    managerId,
                    "GlobalBanResolutionNotificationError",
                    "GlobalUserBanRequest",
                    requestId.ToString(),
                    $"Resolved global ban request {requestId}, but a notification for user {notification.UserId} could not be delivered.",
                    () => _notification.SendNotificationAsync(
                        notification.UserId,
                        notification.Message,
                        notification.Type,
                        notification.ReferenceType,
                        notification.ReferenceId,
                        notification.ActionKey,
                        notification.MetadataJson));
            }

            foreach (var logEntry in logEntries)
            {
                try
                {
                    await _logService.LogActionAsync(
                        managerId,
                        logEntry.ActionType,
                        logEntry.EntityName,
                        logEntry.EntityId,
                        logEntry.Description);
                }
                catch
                {
                }
            }
        }

        public async Task<ImportUserResponse> ImportUsersFromExcelAsync(Stream fileStream, string adminId)
        {
            var response = new ImportUserResponse();

            const int maxRowCount = 1000;

            using var workbook = new XLWorkbook(fileStream);
            var worksheet = workbook.Worksheet(1);
            var rows = worksheet.RangeUsed()?.RowsUsed()?.Skip(1).ToList() ?? new List<IXLRangeRow>();

            if (rows.Count > maxRowCount)
            {
                throw new Exception($"BR-ADM-25: Excel file contains {rows.Count} rows, which exceeds the limit of {maxRowCount} rows. Please split the data into multiple files.");
            }

            int rowNumber = 1;
            var createdUsers = new List<(User User, User? Manager, string TemporaryPassword)>();

            foreach (var row in rows)
            {
                rowNumber++;

                var email = row.Cell(1).GetValue<string>()?.Trim();
                var fullName = row.Cell(2).GetValue<string>()?.Trim();
                var role = row.Cell(3).GetValue<string>()?.Trim();
                var managerEmail = row.Cell(4).GetValue<string>()?.Trim();

                if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(fullName) || string.IsNullOrEmpty(role))
                {
                    response.FailureCount++;
                    response.Errors.Add($"Row {rowNumber}: Missing Email, FullName or Role.");
                    continue;
                }

                if (role != UserRoles.Annotator && role != UserRoles.Reviewer)
                {
                    response.FailureCount++;
                    response.Errors.Add($"Row {rowNumber}: Role '{role}' is not allowed. You can only import 'Annotator' or 'Reviewer'.");
                    continue;
                }

                if (await _userRepository.IsEmailExistsAsync(email))
                {
                    response.FailureCount++;
                    response.Errors.Add($"Row {rowNumber}: Email '{email}' already exists.");
                    continue;
                }

                string? managerIdToAssign = null;
                if (!string.IsNullOrEmpty(managerEmail))
                {
                    var manager = await _userRepository.GetUserByEmailAsync(managerEmail);
                    if (manager == null || (manager.Role != "Manager" && manager.Role != "Admin"))
                    {
                        response.FailureCount++;
                        response.Errors.Add($"Row {rowNumber}: Manager with email '{managerEmail}' not found or is not a Manager.");
                        continue;
                    }
                    managerIdToAssign = manager.Id;
                }

                var temporaryPassword = GenerateTemporaryPassword();
                var user = new User
                {
                    Email = email,
                    FullName = fullName,
                    Role = role,
                    ManagerId = managerIdToAssign,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(temporaryPassword),
                    IsEmailVerified = true
                };

                await _userRepository.AddAsync(user);
                createdUsers.Add((user, string.IsNullOrEmpty(managerIdToAssign) ? null : await _userRepository.GetByIdAsync(managerIdToAssign), temporaryPassword));
                response.SuccessCount++;
            }

            if (response.SuccessCount > 0)
            {
                await _userRepository.SaveChangesAsync();

                foreach (var (user, manager, temporaryPassword) in createdUsers)
                {
                    await RunUserSideEffectSafelyAsync(
                        adminId,
                        "ImportUsersWelcomeEmailError",
                        user.Id,
                        $"Imported user {user.Email}, but the welcome email could not be delivered.",
                        () => _workflowEmailService.SendWelcomeEmailAsync(user, manager, temporaryPassword));
                }
            }

            await _logService.LogActionAsync(
                adminId,
                "ImportUsers",
                "User",
                "Bulk Import",
                $"Admin imported {response.SuccessCount} users, failed {response.FailureCount} rows."
            );

            return response;
        }
        private string GenerateJwtToken(User user, int expiresInMinutes = 30)
        {
            var jwtSettings = _configuration.GetSection("Jwt");
            var keyString = jwtSettings["Key"];
            const int minJwtKeyBytes = 32;
            if (string.IsNullOrEmpty(keyString))
                throw new InvalidOperationException(
                    "JWT Key is not configured. " +
                    "Please set 'Jwt:Key' or environment variable 'Jwt__Key' with at least 32 characters for production use. " +
                    "Example: Jwt__Key=YourSecureKeyAtLeast32Chars!");

            var keyBytes = Encoding.UTF8.GetBytes(keyString);
            if (keyBytes.Length < minJwtKeyBytes)
                throw new InvalidOperationException(
                    $"JWT Key must be at least 32 bytes long for HMAC-SHA256 signing. " +
                    $"Current length: {keyBytes.Length} bytes. " +
                    "Please update 'Jwt:Key' or 'Jwt__Key' with a longer, secure key.");

            string safeAvatarUrl = string.IsNullOrEmpty(user.AvatarUrl)
                ? $"https://ui-avatars.com/api/?name={Uri.EscapeDataString(user.FullName ?? "User")}&background=random"
                : user.AvatarUrl;

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id),
                    new Claim(ClaimTypes.Email, user.Email),
                    new Claim(ClaimTypes.Role, user.Role),
                    new Claim("FullName", user.FullName ?? ""),
                    new Claim("AvatarUrl", safeAvatarUrl)
                }),
                Expires = DateTime.UtcNow.AddMinutes(expiresInMinutes),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256Signature),
                Issuer = jwtSettings["Issuer"],
                Audience = jwtSettings["Audience"]
            };
            var tokenHandler = new JwtSecurityTokenHandler();
            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }
        public async Task<List<UserResponse>> GetManagementBoardAsync()
        {
            var allUsers = await _userRepository.GetAllAsync();

            return allUsers
                .Where(u => u.Role == UserRoles.Admin || u.Role == UserRoles.Manager)
                .Select(u => new UserResponse
                {
                    Id = u.Id,
                    FullName = u.FullName ?? "",
                    Email = u.Email ?? "",
                    Role = u.Role ?? "",
                    AvatarUrl = u.AvatarUrl ?? "",
                    IsActive = u.IsActive
                })
                .OrderBy(u => u.Role)
                .ToList();
        }

        public async Task<EmailDispatchStatusResponse> ForgotPasswordAsync(string email)
        {
            const string responseMessage = "If your email is registered in our system, administrators have been notified and will review your password reset request.";
            const string emailFailureMessage = "If your email is registered in our system, the reset request was recorded, but SMTP email delivery to administrators is currently unavailable.";
            const string deliveryFailureMessage = "If your email is registered in our system, the reset request was recorded, but automated delivery to administrators failed. Please contact an administrator directly.";
            var user = await _userRepository.GetUserByEmailAsync(email);

            if (user == null)
            {
                return new EmailDispatchStatusResponse
                {
                    Message = responseMessage,
                    EmailDelivered = true,
                    NotificationDelivered = true
                };
            }

            bool emailDelivered = true;
            bool notificationDelivered = true;
            var admins = (await _userRepository.FindAsync(u => u.Role == UserRoles.Admin && u.IsActive)).ToList();

            if (!admins.Any())
            {
                notificationDelivered = false;
                await _logService.LogActionAsync(
                    user.Id,
                    "ForgotPasswordNotificationError",
                    "User",
                    user.Id,
                    "No active administrators are available to review this password reset request.");
            }
            else
            {
                try
                {
                    await _workflowEmailService.SendForgotPasswordRequestEmailsAsync(user, admins);
                }
                catch (Exception ex)
                {
                    emailDelivered = false;
                    await SafeLogUserSideEffectFailureAsync(
                        user.Id,
                        "ForgotPasswordEmailError",
                        user.Id,
                        $"Failed to send forgot password request emails for user {user.Email}. {ex.Message}");
                }

                foreach (var admin in admins)
                {
                    try
                    {
                        await _notification.SendNotificationAsync(
                            admin.Id,
                            $"User \"{user.FullName}\" ({user.Email}) has requested a manual password reset. Please review it in User Management.",
                            "PasswordResetRequest");
                    }
                    catch (Exception ex)
                    {
                        notificationDelivered = false;
                        await SafeLogUserSideEffectFailureAsync(
                            user.Id,
                            "ForgotPasswordNotificationError",
                            user.Id,
                            $"Failed to send forgot password notification to admin {admin.Id}. {ex.Message}");
                    }
                }
            }

            await _logService.LogActionAsync(
                user.Id,
                "ForgotPasswordRequested",
                "User",
                user.Id,
                "User requested a password reset. Admin approval is required before the password is changed."
            );

            var emailDeliveryMode = GetEmailDeliveryMode();
            var emailDeliveryTarget = GetEmailDeliveryTarget();
            var effectiveMessage = !notificationDelivered && !emailDelivered
                ? deliveryFailureMessage
                : !emailDelivered
                    ? emailFailureMessage
                    : IsPickupDirectoryDelivery(emailDeliveryMode)
                        ? $"If your email is registered in our system, administrators have been notified. In this Development environment, the email was written to the local mail-drop folder ({emailDeliveryTarget}) instead of being sent to a real inbox."
                        : responseMessage;

            return new EmailDispatchStatusResponse
            {
                Message = effectiveMessage,
                EmailDelivered = emailDelivered,
                NotificationDelivered = notificationDelivered,
                EmailDeliveryMode = emailDeliveryMode,
                EmailDeliveryTarget = emailDeliveryTarget
            };
        }
        public async Task<EmailDispatchStatusResponse> AdminChangeUserPasswordAsync(string adminId, string targetUserId)
        {
            var user = await _userRepository.GetByIdAsync(targetUserId);
            if (user == null) throw new Exception("User not found.");

            var previousPasswordHash = user.PasswordHash;
            var temporaryPassword = GenerateTemporaryPassword();

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(temporaryPassword);

            _userRepository.Update(user);
            await _userRepository.SaveChangesAsync();

            try
            {
                await _workflowEmailService.SendAdminPasswordResetEmailAsync(user, temporaryPassword);

                await _logService.LogActionAsync(
                    adminId,
                    "AdminChangePassword",
                    "User",
                    targetUserId,
                    $"Admin generated a new temporary password for user {user.Email}."
                );

                var emailDeliveryMode = GetEmailDeliveryMode();
                var emailDeliveryTarget = GetEmailDeliveryTarget();
                return new EmailDispatchStatusResponse
                {
                    Message = IsPickupDirectoryDelivery(emailDeliveryMode)
                        ? $"A new temporary password has been generated. In this Development environment, the reset email was written to the local mail-drop folder ({emailDeliveryTarget}) instead of being sent to the user's real inbox."
                        : "A new temporary password has been generated and the reset email was delivered.",
                    EmailDelivered = true,
                    NotificationDelivered = true,
                    EmailDeliveryMode = emailDeliveryMode,
                    EmailDeliveryTarget = emailDeliveryTarget
                };
            }
            catch (Exception ex)
            {
                try
                {
                    user.PasswordHash = previousPasswordHash;
                    _userRepository.Update(user);
                    await _userRepository.SaveChangesAsync();
                }
                catch (Exception rollbackEx)
                {
                    await SafeLogUserSideEffectFailureAsync(
                        adminId,
                        "AdminChangePasswordRollbackError",
                        targetUserId,
                        $"Temporary password delivery failed for user {user.Email}, and restoring the previous password also failed. {rollbackEx.Message}");

                    throw new Exception("Temporary password delivery failed and the previous password could not be restored. Please verify SMTP settings and reset the account again.");
                }

                await SafeLogUserSideEffectFailureAsync(
                    adminId,
                    "AdminChangePasswordEmailError",
                    targetUserId,
                    $"Temporary password generation for user {user.Email} was cancelled because the reset email could not be delivered. {ex.Message}");

                return new EmailDispatchStatusResponse
                {
                    Message = "Temporary password delivery failed, so the old password remains active. Please verify SMTP settings and try again.",
                    EmailDelivered = false,
                    NotificationDelivered = true,
                    EmailDeliveryMode = GetEmailDeliveryMode(),
                    EmailDeliveryTarget = GetEmailDeliveryTarget()
                };
            }
        }

        private string GetEmailDeliveryMode()
        {
            return _configuration["EmailSettings:DeliveryMode"]?.Trim() ?? "Network";
        }

        private string? GetEmailDeliveryTarget()
        {
            if (!IsPickupDirectoryDelivery(GetEmailDeliveryMode()))
            {
                return null;
            }

            var configuredPickupDirectory = _configuration["EmailSettings:PickupDirectory"]?.Trim();
            var effectivePickupDirectory = string.IsNullOrWhiteSpace(configuredPickupDirectory)
                ? "mail-drop"
                : configuredPickupDirectory;

            return Path.GetFullPath(
                Path.IsPathRooted(effectivePickupDirectory)
                    ? effectivePickupDirectory
                    : Path.Combine(Directory.GetCurrentDirectory(), effectivePickupDirectory));
        }

        private static bool IsPickupDirectoryDelivery(string? deliveryMode)
        {
            return string.Equals(deliveryMode, "PickupDirectory", StringComparison.OrdinalIgnoreCase);
        }

        private static string GenerateTemporaryPassword(int length = 12)
        {
            if (length < 10)
            {
                throw new ArgumentOutOfRangeException(nameof(length), "Temporary passwords must be at least 10 characters long.");
            }

            var passwordChars = new List<char>(length)
            {
                UppercasePasswordChars[RandomNumberGenerator.GetInt32(UppercasePasswordChars.Length)],
                LowercasePasswordChars[RandomNumberGenerator.GetInt32(LowercasePasswordChars.Length)],
                DigitPasswordChars[RandomNumberGenerator.GetInt32(DigitPasswordChars.Length)],
                SpecialPasswordChars[RandomNumberGenerator.GetInt32(SpecialPasswordChars.Length)]
            };

            while (passwordChars.Count < length)
            {
                passwordChars.Add(AllPasswordChars[RandomNumberGenerator.GetInt32(AllPasswordChars.Length)]);
            }

            for (var index = passwordChars.Count - 1; index > 0; index--)
            {
                var swapIndex = RandomNumberGenerator.GetInt32(index + 1);
                (passwordChars[index], passwordChars[swapIndex]) = (passwordChars[swapIndex], passwordChars[index]);
            }

            return new string(passwordChars.ToArray());
        }

        private async Task RunSideEffectSafelyAsync(
            string actorUserId,
            string actionType,
            string entityName,
            string entityId,
            string description,
            Func<Task> operation)
        {
            try
            {
                await operation();
            }
            catch (Exception ex)
            {
                try
                {
                    await _logService.LogActionAsync(
                        actorUserId,
                        actionType,
                        entityName,
                        entityId,
                        $"{description} {ex.Message}");
                }
                catch
                {
                }
            }
        }

        private Task RunUserSideEffectSafelyAsync(
            string actorUserId,
            string actionType,
            string entityId,
            string description,
            Func<Task> operation)
        {
            return RunSideEffectSafelyAsync(
                actorUserId,
                actionType,
                "User",
                entityId,
                description,
                operation);
        }

        private async Task SafeLogUserSideEffectFailureAsync(
            string actorUserId,
            string actionType,
            string entityId,
            string description)
        {
            try
            {
                await _logService.LogActionAsync(
                    actorUserId,
                    actionType,
                    "User",
                    entityId,
                    description);
            }
            catch
            {
            }
        }
    }
}

