using BLL.Interfaces;
using Core.DTOs.Requests;
using Core.DTOs.Responses;
using DAL.Interfaces;
using Core.Constants;
using Core.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using ClosedXML.Excel;

namespace BLL.Services
{
    public class UserService : IUserService
    {
        private readonly IUserRepository _userRepository;
        private readonly IRefreshTokenRepository _refreshTokenRepository;
        private readonly IConfiguration _configuration;
        private readonly IActivityLogService _logService;
        private readonly IAssignmentRepository _assignmentRepo;
        private readonly IProjectRepository _projectRepo;
        private readonly IAppNotificationService _notification;
        private readonly IWorkflowEmailService _workflowEmailService;
        public UserService(
            IUserRepository userRepository,
            IRefreshTokenRepository refreshTokenRepository,
            IConfiguration configuration,
            IAssignmentRepository assignmentRepo,
            IActivityLogService logService,
            IProjectRepository projectRepo,
            IAppNotificationService notification,
            IWorkflowEmailService workflowEmailService)
        {
            _userRepository = userRepository;
            _refreshTokenRepository = refreshTokenRepository;
            _configuration = configuration;
            _assignmentRepo = assignmentRepo;
            _logService = logService;
            _projectRepo = projectRepo;
            _notification = notification;
            _workflowEmailService = workflowEmailService;
        }

        public async Task<User> RegisterAsync(string fullName, string email, string password, string role, string? managerId = null)
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
                user.Id,
                "CreateUser",
                "User",
                user.Id,
                $"Account created with role {role}."
            );

            var manager = string.IsNullOrWhiteSpace(user.ManagerId)
                ? null
                : await _userRepository.GetByIdAsync(user.ManagerId);

            await _workflowEmailService.SendWelcomeEmailAsync(user, manager);

            return user;
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

        public async Task<(string? accessToken, string? refreshToken)> LoginAsync(string email, string password)
        {
            var user = await _userRepository.GetUserByEmailAsync(email);
            if (user == null) return (null, null);
            if (!user.IsActive)
            {
                throw new ArgumentException("Account is deactivated or banned.");
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

        public async Task<PagedResponse<UserResponse>> GetAllUsersAsync(int page, int pageSize)
        {
            var allUsers = await _userRepository.GetAllAsync();
            var allProjects = await _projectRepo.GetAllAsync();
            var allAssignments = await _assignmentRepo.GetAllAsync();

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
                .Select(u =>
                {
                    int totalProjects;

                    if (u.Role == UserRoles.Manager || u.Role == UserRoles.Admin)
                    {
                        totalProjects = allProjects.Count(p => p.ManagerId == u.Id);
                    }
                    else
                    {
                        totalProjects = allAssignments
                            .Where(a => a.AnnotatorId == u.Id || a.ReviewerId == u.Id)
                            .Select(a => a.ProjectId)
                            .Distinct()
                            .Count();
                    }

                    return new UserResponse
                    {
                        Id = u.Id,
                        FullName = u.FullName ?? "",
                        Email = u.Email ?? "",
                        Role = u.Role ?? "",
                        AvatarUrl = u.AvatarUrl ?? "",
                        IsActive = u.IsActive,
                        ManagerId = u.ManagerId,
                        TotalProjects = totalProjects
                    };
                }).ToList();

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
            var allUsers = await _userRepository.GetAllAsync();
            var managedUsers = allUsers
                .Where(u => u.ManagerId == managerId && u.IsActive)
                .Select(u => new UserResponse
                {
                    Id = u.Id,
                    FullName = u.FullName ?? "",
                    Email = u.Email ?? "",
                    Role = u.Role ?? "",
                    AvatarUrl = u.AvatarUrl ?? "",
                    IsActive = u.IsActive,
                    ManagerId = u.ManagerId,
                    TotalProjects = 0
                }).ToList();

            return managedUsers;
        }

        public async Task<List<UserResponse>> GetAllUsersNoPagingAsync()
        {
            var allUsers = await _userRepository.GetAllAsync();
            return allUsers.Select(u => new UserResponse
            {
                Id = u.Id,
                FullName = u.FullName ?? "",
                Email = u.Email ?? "",
                Role = u.Role ?? "",
                AvatarUrl = u.AvatarUrl ?? "",
                IsActive = u.IsActive,
                ManagerId = u.ManagerId,
                TotalProjects = 0
            }).ToList();
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

        public async Task ToggleUserStatusAsync(string userId, bool isActive, string? adminId = null)
        {
            if (!string.IsNullOrEmpty(adminId) && userId == adminId && !isActive)
            {
                throw new Exception("BR-ADM-19: Admin cannot block themselves.");
            }

            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null) throw new Exception("User not found");
            if (!isActive)
            {
                bool hasPendingTasks = await _assignmentRepo.HasPendingTasksAsync(user.Id, user.Role);
                if (hasPendingTasks)
                {
                    throw new Exception($"Cannot deactivate this user. They still have unfinished tasks as an {user.Role}. Please reassign or complete their tasks first.");
                }


                if (user.Role == UserRoles.Annotator || user.Role == UserRoles.Reviewer)
                {
                    var userAssignments = await _assignmentRepo.GetAllAsync();
                    var allProjects = await _projectRepo.GetAllAsync();

                    var activeProjects = allProjects.Where(p => p.Status == "Active").ToList();
                    var affectedProjectIds = userAssignments
                        .Where(a => (a.AnnotatorId == user.Id || a.ReviewerId == user.Id) &&
                                    activeProjects.Any(p => p.Id == a.ProjectId))
                        .Select(a => a.ProjectId)
                        .Distinct()
                        .ToList();


                    foreach (var projectId in affectedProjectIds)
                    {
                        var project = activeProjects.FirstOrDefault(p => p.Id == projectId);
                        if (project != null && !string.IsNullOrEmpty(project.ManagerId))
                        {
                            await _notification.SendNotificationAsync(
                                project.ManagerId,
                                $"Admin has locked the account of {user.Role} \"{user.FullName}\" ({user.Email}) who is assigned to your project \"{project.Name}\". Please reassign their tasks to another member.",
                                "UserLocked"
                            );
                        }
                    }
                }
            }
            user.IsActive = isActive;
            _userRepository.Update(user);
            await _userRepository.SaveChangesAsync();

            var statusStr = isActive ? "activated" : "deactivated";
            await _logService.LogActionAsync(userId, "ToggleUserStatus", "User", userId, $"Admin {statusStr} the user account.");
        }

        public async Task<ImportUserResponse> ImportUsersFromExcelAsync(Stream fileStream, string adminId)
        {
            var response = new ImportUserResponse();
            var defaultPassword = BCrypt.Net.BCrypt.HashPassword("Password@123");

            const int maxRowCount = 1000;

            using var workbook = new XLWorkbook(fileStream);
            var worksheet = workbook.Worksheet(1);
            var rows = worksheet.RangeUsed()?.RowsUsed()?.Skip(1).ToList() ?? new List<IXLRangeRow>();

            if (rows.Count > maxRowCount)
            {
                throw new Exception($"BR-ADM-25: Excel file contains {rows.Count} rows, which exceeds the limit of {maxRowCount} rows. Please split the data into multiple files.");
            }

            int rowNumber = 1;
            var createdUsers = new List<(User User, User? Manager)>();

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

                var user = new User
                {
                    Email = email,
                    FullName = fullName,
                    Role = role,
                    ManagerId = managerIdToAssign,
                    PasswordHash = defaultPassword
                };

                await _userRepository.AddAsync(user);
                createdUsers.Add((user, string.IsNullOrEmpty(managerIdToAssign) ? null : await _userRepository.GetByIdAsync(managerIdToAssign)));
                response.SuccessCount++;
            }

            if (response.SuccessCount > 0)
            {
                await _userRepository.SaveChangesAsync();

                foreach (var (user, manager) in createdUsers)
                {
                    await _workflowEmailService.SendWelcomeEmailAsync(user, manager);
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
            if (string.IsNullOrEmpty(keyString))
                throw new InvalidOperationException(
                    "JWT Key is not configured. " +
                    "Please set 'Jwt:Key' in appsettings.json with at least 16 characters for production use. " +
                    "Example: \"Jwt\": { \"Key\": \"YourSecureKeyAtLeast16Chars!\" }");

            var keyBytes = Encoding.ASCII.GetBytes(keyString);
            if (keyBytes.Length < 16)
                throw new InvalidOperationException(
                    $"JWT Key must be at least 16 characters long for security. " +
                    $"Current length: {keyBytes.Length} characters. " +
                    $"Please update 'Jwt:Key' in appsettings.json with a longer, secure key.");

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

        public async Task<string> ForgotPasswordAsync(string email)
        {
            const string responseMessage = "If your email is registered in our system, administrators have been notified and will review your password reset request.";
            var user = await _userRepository.GetUserByEmailAsync(email);

            if (user == null)
            {
                return responseMessage;
            }

            var admins = (await _userRepository.FindAsync(u => u.Role == UserRoles.Admin && u.IsActive)).ToList();
            if (admins.Any())
            {
                await _workflowEmailService.SendForgotPasswordRequestEmailsAsync(user, admins);

                foreach (var admin in admins)
                {
                    await _notification.SendNotificationAsync(
                        admin.Id,
                        $"User \"{user.FullName}\" ({user.Email}) has requested a manual password reset. Please review it in User Management.",
                        "PasswordResetRequest");
                }
            }

            await _logService.LogActionAsync(
                user.Id,
                "ForgotPasswordRequested",
                "User",
                user.Id,
                "User requested a password reset. Admin approval is required before the password is changed."
            );

            return responseMessage;
        }
        public async Task AdminChangeUserPasswordAsync(string adminId, string targetUserId, string newPassword)
        {
            var user = await _userRepository.GetByIdAsync(targetUserId);
            if (user == null) throw new Exception("User not found.");

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);

            _userRepository.Update(user);
            await _userRepository.SaveChangesAsync();

            await _logService.LogActionAsync(
                adminId,
                "AdminChangePassword",
                "User",
                targetUserId,
                $"Admin explicitly changed the password for user {user.Email}."
            );

            await _workflowEmailService.SendAdminPasswordResetEmailAsync(user, newPassword);
        }
    }
}
