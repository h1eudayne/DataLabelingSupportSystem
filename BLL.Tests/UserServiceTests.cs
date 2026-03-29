using BLL.Interfaces;
using BLL.Services;
using Core.Constants;
using Core.DTOs.Requests;
using Core.Entities;
using DAL.Interfaces;
using Moq;
using Xunit;

namespace BLL.Tests
{
    public class UserServiceTests
    {
        private readonly Mock<IUserRepository> _userRepoMock;
        private readonly Mock<IRefreshTokenRepository> _refreshTokenRepoMock;
        private readonly Mock<IAssignmentRepository> _assignmentRepoMock;
        private readonly Mock<IProjectRepository> _projectRepoMock;
        private readonly Mock<IActivityLogService> _logServiceMock;
        private readonly Mock<IAppNotificationService> _notificationMock;
        private readonly Mock<Microsoft.Extensions.Configuration.IConfiguration> _configMock;
        private readonly Mock<IEmailService> _emailServiceMock;

        private readonly UserService _userService;

        public UserServiceTests()
        {
            _userRepoMock = new Mock<IUserRepository>();
            _emailServiceMock = new Mock<IEmailService>();
            _refreshTokenRepoMock = new Mock<IRefreshTokenRepository>();
            _assignmentRepoMock = new Mock<IAssignmentRepository>();
            _projectRepoMock = new Mock<IProjectRepository>();
            _logServiceMock = new Mock<IActivityLogService>();
            _notificationMock = new Mock<IAppNotificationService>();
            _configMock = new Mock<Microsoft.Extensions.Configuration.IConfiguration>();

            var jwtSection = new Mock<Microsoft.Extensions.Configuration.IConfigurationSection>();
            jwtSection.Setup(s => s["Key"]).Returns("ThisIsAVeryLongSecretKeyForTestingPurposes123456");
            jwtSection.Setup(s => s["Issuer"]).Returns("TestIssuer");
            jwtSection.Setup(s => s["Audience"]).Returns("TestAudience");
            _configMock.Setup(c => c.GetSection("Jwt")).Returns(jwtSection.Object);

            _userService = new UserService(
                _userRepoMock.Object,
                _refreshTokenRepoMock.Object,
                _configMock.Object,
                _assignmentRepoMock.Object,
                _logServiceMock.Object,
                _projectRepoMock.Object,
                _notificationMock.Object,
                _emailServiceMock.Object
            );
        }

        #region RegisterAsync Tests

        [Fact]
        public async Task RegisterAsync_WithValidData_CreatesUser()
        {
            _userRepoMock.Setup(r => r.IsEmailExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
            _userRepoMock.Setup(r => r.HasAdminRoleAsync()).ReturnsAsync(false);
            _userRepoMock.Setup(r => r.AddAsync(It.IsAny<User>())).Returns(Task.CompletedTask);
            _userRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            var result = await _userService.RegisterAsync("John Doe", "john@test.com", "Password@123", UserRoles.Annotator);

            Assert.NotNull(result);
            Assert.Equal("john@test.com", result.Email);
            Assert.Equal(UserRoles.Annotator, result.Role);
            _userRepoMock.Verify(r => r.AddAsync(It.IsAny<User>()), Times.Once);
            _logServiceMock.Verify(l => l.LogActionAsync(It.IsAny<string>(), "CreateUser", "User", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task RegisterAsync_WithExistingEmail_ThrowsException()
        {
            _userRepoMock.Setup(r => r.IsEmailExistsAsync("john@test.com")).ReturnsAsync(true);

            await Assert.ThrowsAsync<Exception>(() =>
                _userService.RegisterAsync("John Doe", "john@test.com", "Password@123", UserRoles.Annotator));
        }

        [Fact]
        public async Task RegisterAsync_WithInvalidRole_ThrowsException()
        {
            await Assert.ThrowsAsync<Exception>(() =>
                _userService.RegisterAsync("John Doe", "john@test.com", "Password@123", "InvalidRole"));
        }

        [Fact]
        public async Task RegisterAsync_SecondAdmin_ThrowsException()
        {
            _userRepoMock.Setup(r => r.IsEmailExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
            _userRepoMock.Setup(r => r.HasAdminRoleAsync()).ReturnsAsync(true);

            var ex = await Assert.ThrowsAsync<Exception>(() =>
                _userService.RegisterAsync("Second Admin", "admin2@test.com", "Password@123", UserRoles.Admin));

            Assert.Contains("BR-ADM-27", ex.Message);
        }

        [Fact]
        public async Task RegisterAsync_AsAnnotatorWithManager_SetsManagerId()
        {
            _userRepoMock.Setup(r => r.IsEmailExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
            _userRepoMock.Setup(r => r.AddAsync(It.IsAny<User>())).Returns(Task.CompletedTask);
            _userRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            var result = await _userService.RegisterAsync("Jane Doe", "jane@test.com", "Password@123", UserRoles.Annotator, "manager-1");

            Assert.NotNull(result);
            Assert.Equal("manager-1", result.ManagerId);
        }

        #endregion

        #region LoginAsync Tests

        [Fact]
        public async Task LoginAsync_WithValidCredentials_ReturnsTokens()
        {
            var user = new User
            {
                Id = "user-1",
                Email = "john@test.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password@123"),
                IsActive = true,
                Role = UserRoles.Annotator
            };

            _userRepoMock.Setup(r => r.GetUserByEmailAsync("john@test.com")).ReturnsAsync(user);
            _refreshTokenRepoMock.Setup(r => r.RevokeAllUserTokensAsync(user.Id)).Returns(Task.CompletedTask);
            _refreshTokenRepoMock.Setup(r => r.AddAsync(It.IsAny<RefreshToken>())).Returns(Task.CompletedTask);
            _refreshTokenRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            var result = await _userService.LoginAsync("john@test.com", "Password@123");

            Assert.NotNull(result.accessToken);
            Assert.NotNull(result.refreshToken);
        }

        [Fact]
        public async Task LoginAsync_WithInvalidPassword_ReturnsNullTokens()
        {
            var user = new User
            {
                Id = "user-1",
                Email = "john@test.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password@123"),
                IsActive = true
            };

            _userRepoMock.Setup(r => r.GetUserByEmailAsync("john@test.com")).ReturnsAsync(user);

            var result = await _userService.LoginAsync("john@test.com", "WrongPassword");

            Assert.Null(result.accessToken);
            Assert.Null(result.refreshToken);
        }

        [Fact]
        public async Task LoginAsync_WithInactiveUser_ThrowsException()
        {
            var user = new User
            {
                Id = "user-1",
                Email = "john@test.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password@123"),
                IsActive = false
            };

            _userRepoMock.Setup(r => r.GetUserByEmailAsync("john@test.com")).ReturnsAsync(user);

            await Assert.ThrowsAsync<ArgumentException>(() =>
                _userService.LoginAsync("john@test.com", "Password@123"));
        }

        [Fact]
        public async Task LoginAsync_WithNonexistentUser_ReturnsNullTokens()
        {
            _userRepoMock.Setup(r => r.GetUserByEmailAsync("nonexistent@test.com")).ReturnsAsync((User?)null);

            var result = await _userService.LoginAsync("nonexistent@test.com", "Password@123");

            Assert.Null(result.accessToken);
            Assert.Null(result.refreshToken);
        }

        #endregion

        #region RefreshTokenAsync Tests

        [Fact]
        public async Task RefreshTokenAsync_WithValidToken_ReturnsNewTokens()
        {
            var user = new User
            {
                Id = "user-1",
                Email = "john@test.com",
                IsActive = true,
                Role = UserRoles.Annotator
            };
            var refreshToken = new RefreshToken
            {
                Id = 1,
                UserId = user.Id,
                Token = "valid-refresh-token",
                ExpiresAt = DateTime.UtcNow.AddDays(7)
            };

            _refreshTokenRepoMock.Setup(r => r.GetByTokenAsync("valid-refresh-token")).ReturnsAsync(refreshToken);
            _userRepoMock.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);
            _refreshTokenRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _refreshTokenRepoMock.Setup(r => r.AddAsync(It.IsAny<RefreshToken>())).Returns(Task.CompletedTask);

            var result = await _userService.RefreshTokenAsync("valid-refresh-token");

            Assert.NotNull(result.accessToken);
            Assert.NotNull(result.refreshToken);
            Assert.NotEqual("valid-refresh-token", result.refreshToken);
        }

        [Fact]
        public async Task RefreshTokenAsync_WithExpiredToken_ReturnsNullTokens()
        {
            var refreshToken = new RefreshToken
            {
                Id = 1,
                UserId = "user-1",
                Token = "expired-token",
                ExpiresAt = DateTime.UtcNow.AddDays(-1)
            };

            _refreshTokenRepoMock.Setup(r => r.GetByTokenAsync("expired-token")).ReturnsAsync(refreshToken);

            var result = await _userService.RefreshTokenAsync("expired-token");

            Assert.Null(result.accessToken);
            Assert.Null(result.refreshToken);
        }

        [Fact]
        public async Task RefreshTokenAsync_WithInactiveUser_ReturnsNullTokens()
        {
            var refreshToken = new RefreshToken
            {
                Id = 1,
                UserId = "user-1",
                Token = "valid-token",
                ExpiresAt = DateTime.UtcNow.AddDays(7)
            };

            _refreshTokenRepoMock.Setup(r => r.GetByTokenAsync("valid-token")).ReturnsAsync(refreshToken);
            _userRepoMock.Setup(r => r.GetByIdAsync("user-1")).ReturnsAsync((User?)null);

            var result = await _userService.RefreshTokenAsync("valid-token");

            Assert.Null(result.accessToken);
            Assert.Null(result.refreshToken);
        }

        #endregion

        #region DeleteUserAsync Tests

        [Fact]
        public async Task DeleteUserAsync_WithNoPendingTasks_DeactivatesUser()
        {
            var user = new User
            {
                Id = "user-1",
                Email = "john@test.com",
                IsActive = true,
                Role = UserRoles.Annotator
            };

            _userRepoMock.Setup(r => r.GetByIdAsync("user-1")).ReturnsAsync(user);
            _assignmentRepoMock.Setup(r => r.HasPendingTasksAsync(user.Id, user.Role)).ReturnsAsync(false);
            _userRepoMock.Setup(r => r.Update(It.IsAny<User>())).Verifiable();
            _userRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            await _userService.DeleteUserAsync("user-1");

            Assert.False(user.IsActive);
            _logServiceMock.Verify(l => l.LogActionAsync("user-1", "DeleteUser", "User", "user-1", It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task DeleteUserAsync_WithPendingTasks_ThrowsException()
        {
            var user = new User
            {
                Id = "user-1",
                Email = "john@test.com",
                IsActive = true,
                Role = UserRoles.Annotator
            };

            _userRepoMock.Setup(r => r.GetByIdAsync("user-1")).ReturnsAsync(user);
            _assignmentRepoMock.Setup(r => r.HasPendingTasksAsync(user.Id, user.Role)).ReturnsAsync(true);

            await Assert.ThrowsAsync<Exception>(() =>
                _userService.DeleteUserAsync("user-1"));
        }

        [Fact]
        public async Task DeleteUserAsync_UserNotFound_ThrowsException()
        {
            _userRepoMock.Setup(r => r.GetByIdAsync("nonexistent")).ReturnsAsync((User?)null);

            await Assert.ThrowsAsync<Exception>(() =>
                _userService.DeleteUserAsync("nonexistent"));
        }

        #endregion

        #region ToggleUserStatusAsync Tests

        [Fact]
        public async Task ToggleUserStatusAsync_AdminBlocksSelf_ThrowsException()
        {
            await Assert.ThrowsAsync<Exception>(() =>
                _userService.ToggleUserStatusAsync("admin-1", false, "admin-1"));
        }

        [Fact]
        public async Task ToggleUserStatusAsync_DeactivateWithPendingTasks_ThrowsException()
        {
            var user = new User
            {
                Id = "user-1",
                Role = UserRoles.Annotator,
                IsActive = true
            };

            _userRepoMock.Setup(r => r.GetByIdAsync("user-1")).ReturnsAsync(user);
            _assignmentRepoMock.Setup(r => r.HasPendingTasksAsync(user.Id, user.Role)).ReturnsAsync(true);

            await Assert.ThrowsAsync<Exception>(() =>
                _userService.ToggleUserStatusAsync("user-1", false, "admin-1"));
        }

        [Fact]
        public async Task ToggleUserStatusAsync_ValidDeactivation_DeactivatesUser()
        {
            var user = new User
            {
                Id = "user-1",
                Role = UserRoles.Annotator,
                IsActive = true
            };

            _userRepoMock.Setup(r => r.GetByIdAsync("user-1")).ReturnsAsync(user);
            _assignmentRepoMock.Setup(r => r.HasPendingTasksAsync(user.Id, user.Role)).ReturnsAsync(false);
            _assignmentRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Assignment>());
            _projectRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Project>());
            _userRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            await _userService.ToggleUserStatusAsync("user-1", false, "admin-1");

            Assert.False(user.IsActive);
        }

        [Fact]
        public async Task ToggleUserStatusAsync_ActivateUser_SetsIsActiveTrue()
        {
            var user = new User
            {
                Id = "user-1",
                Role = UserRoles.Annotator,
                IsActive = false
            };

            _userRepoMock.Setup(r => r.GetByIdAsync("user-1")).ReturnsAsync(user);
            _userRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            await _userService.ToggleUserStatusAsync("user-1", true);

            Assert.True(user.IsActive);
        }

        #endregion

        #region ChangePasswordAsync Tests

        [Fact]
        public async Task ChangePasswordAsync_WithCorrectOldPassword_ChangesPassword()
        {
            var user = new User
            {
                Id = "user-1",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("OldPassword@123")
            };

            _userRepoMock.Setup(r => r.GetByIdAsync("user-1")).ReturnsAsync(user);
            _userRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            await _userService.ChangePasswordAsync("user-1", "OldPassword@123", "NewPassword@456");

            Assert.NotNull(user.PasswordHash);
            _logServiceMock.Verify(l => l.LogActionAsync("user-1", "ChangePassword", "User", "user-1", It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task ChangePasswordAsync_WithIncorrectOldPassword_ThrowsException()
        {
            var user = new User
            {
                Id = "user-1",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("OldPassword@123")
            };

            _userRepoMock.Setup(r => r.GetByIdAsync("user-1")).ReturnsAsync(user);

            await Assert.ThrowsAsync<Exception>(() =>
                _userService.ChangePasswordAsync("user-1", "WrongPassword", "NewPassword@456"));
        }

        #endregion

        #region GetAllUsersAsync Tests

        [Fact]
        public async Task GetAllUsersAsync_ReturnsPagedResponse()
        {
            var users = new List<User>
            {
                new User { Id = "1", FullName = "User 1", Email = "user1@test.com", Role = UserRoles.Annotator },
                new User { Id = "2", FullName = "User 2", Email = "user2@test.com", Role = UserRoles.Manager }
            };

            _userRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(users);
            _projectRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Project>());
            _assignmentRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Assignment>());

            var result = await _userService.GetAllUsersAsync(1, 10);

            Assert.Equal(2, result.TotalCount);
            Assert.Equal(2, result.Items.Count);
            Assert.Equal(1, result.Page);
            Assert.Equal(10, result.PageSize);
        }

        #endregion

        #region UpdateProfileAsync Tests

        [Fact]
        public async Task UpdateProfileAsync_UpdatesFullNameAndAvatar()
        {
            var user = new User
            {
                Id = "user-1",
                FullName = "Old Name",
                AvatarUrl = "old-url"
            };

            _userRepoMock.Setup(r => r.GetByIdAsync("user-1")).ReturnsAsync(user);
            _userRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            var request = new UpdateProfileRequest
            {
                FullName = "New Name",
                AvatarUrl = "new-url"
            };

            await _userService.UpdateProfileAsync("user-1", request);

            Assert.Equal("New Name", user.FullName);
            Assert.Equal("new-url", user.AvatarUrl);
            _logServiceMock.Verify(l => l.LogActionAsync("user-1", "UpdateProfile", "User", "user-1", It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        #endregion

        #region UpdateUserAsync Tests

        [Fact]
        public async Task UpdateUserAsync_AdminCannotModifyFullName()
        {
            var admin = new User { Id = "admin-1", Role = UserRoles.Admin };
            var targetUser = new User { Id = "user-1", FullName = "Target User", Email = "target@test.com", IsEmailVerified = false };

            _userRepoMock.Setup(r => r.GetByIdAsync("user-1")).ReturnsAsync(targetUser);
            _userRepoMock.Setup(r => r.GetByIdAsync("admin-1")).ReturnsAsync(admin);

            var request = new UpdateUserRequest { FullName = "New Name" };

            var ex = await Assert.ThrowsAsync<Exception>(() =>
                _userService.UpdateUserAsync("user-1", "admin-1", request));

            Assert.Contains("BR-ADM-28", ex.Message);
        }

        [Fact]
        public async Task UpdateUserAsync_CannotChangeRoleWithPendingTasks()
        {
            var admin = new User { Id = "admin-1", Role = UserRoles.Admin };
            var targetUser = new User { Id = "user-1", Role = UserRoles.Annotator, IsEmailVerified = false };

            _userRepoMock.Setup(r => r.GetByIdAsync("user-1")).ReturnsAsync(targetUser);
            _userRepoMock.Setup(r => r.GetByIdAsync("admin-1")).ReturnsAsync(admin);
            _assignmentRepoMock.Setup(r => r.HasPendingTasksAsync(targetUser.Id, targetUser.Role)).ReturnsAsync(true);

            var request = new UpdateUserRequest { Role = UserRoles.Reviewer };

            await Assert.ThrowsAsync<Exception>(() =>
                _userService.UpdateUserAsync("user-1", "admin-1", request));
        }

        #endregion

        #region GetUserByIdAsync Tests

        [Fact]
        public async Task GetUserByIdAsync_UserExists_ReturnsUser()
        {
            var user = new User { Id = "user-1", Email = "test@test.com" };
            _userRepoMock.Setup(r => r.GetByIdAsync("user-1")).ReturnsAsync(user);

            var result = await _userService.GetUserByIdAsync("user-1");

            Assert.NotNull(result);
            Assert.Equal("test@test.com", result.Email);
        }

        [Fact]
        public async Task GetUserByIdAsync_UserNotFound_ReturnsNull()
        {
            _userRepoMock.Setup(r => r.GetByIdAsync("nonexistent")).ReturnsAsync((User?)null);

            var result = await _userService.GetUserByIdAsync("nonexistent");

            Assert.Null(result);
        }

        #endregion

        #region IsEmailExistsAsync Tests

        [Fact]
        public async Task IsEmailExistsAsync_EmailExists_ReturnsTrue()
        {
            _userRepoMock.Setup(r => r.IsEmailExistsAsync("existing@test.com")).ReturnsAsync(true);

            var result = await _userService.IsEmailExistsAsync("existing@test.com");

            Assert.True(result);
        }

        [Fact]
        public async Task IsEmailExistsAsync_EmailNotExists_ReturnsFalse()
        {
            _userRepoMock.Setup(r => r.IsEmailExistsAsync("new@test.com")).ReturnsAsync(false);

            var result = await _userService.IsEmailExistsAsync("new@test.com");

            Assert.False(result);
        }

        #endregion

        #region GetManagedUsersAsync Tests

        [Fact]
        public async Task GetManagedUsersAsync_ReturnsOnlyManagedActiveUsers()
        {
            var users = new List<User>
            {
                new User { Id = "user-1", FullName = "User 1", Email = "user1@test.com", Role = UserRoles.Annotator, ManagerId = "manager-1", IsActive = true },
                new User { Id = "user-2", FullName = "User 2", Email = "user2@test.com", Role = UserRoles.Annotator, ManagerId = "manager-1", IsActive = false },
                new User { Id = "user-3", FullName = "User 3", Email = "user3@test.com", Role = UserRoles.Annotator, ManagerId = "other-manager", IsActive = true }
            };

            _userRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(users);

            var result = await _userService.GetManagedUsersAsync("manager-1");

            Assert.Single(result);
            Assert.Equal("user-1", result[0].Id);
        }

        #endregion

        #region GetManagementBoardAsync Tests

        [Fact]
        public async Task GetManagementBoardAsync_ReturnsOnlyAdminsAndManagers()
        {
            var users = new List<User>
            {
                new User { Id = "admin-1", FullName = "Admin", Email = "admin@test.com", Role = UserRoles.Admin },
                new User { Id = "manager-1", FullName = "Manager", Email = "manager@test.com", Role = UserRoles.Manager },
                new User { Id = "annotator-1", FullName = "Annotator", Email = "annotator@test.com", Role = UserRoles.Annotator }
            };

            _userRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(users);

            var result = await _userService.GetManagementBoardAsync();

            Assert.Equal(2, result.Count);
            Assert.All(result, u => Assert.Contains(u.Role, new[] { UserRoles.Admin, UserRoles.Manager }));
        }

        #endregion

        #region UpdateAvatarAsync Tests

        [Fact]
        public async Task UpdateAvatarAsync_UpdatesAvatarUrl()
        {
            var user = new User { Id = "user-1", AvatarUrl = "old-url" };
            _userRepoMock.Setup(r => r.GetByIdAsync("user-1")).ReturnsAsync(user);
            _userRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            await _userService.UpdateAvatarAsync("user-1", "new-avatar-url");

            Assert.Equal("new-avatar-url", user.AvatarUrl);
            _logServiceMock.Verify(l => l.LogActionAsync("user-1", "UpdateAvatar", "User", "user-1", It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        #endregion
    }
}