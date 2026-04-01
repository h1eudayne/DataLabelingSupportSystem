using BLL.Interfaces;
using BLL.Services;
using ClosedXML.Excel;
using Core.Constants;
using Core.DTOs.Requests;
using Core.Entities;
using Core.Interfaces;
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
        private readonly Mock<IRepository<GlobalUserBanRequest>> _globalBanRequestRepoMock;
        private readonly Mock<IRepository<AppNotification>> _appNotificationRepoMock;
        private readonly Mock<IActivityLogService> _logServiceMock;
        private readonly Mock<IAppNotificationService> _notificationMock;
        private readonly Mock<Microsoft.Extensions.Configuration.IConfiguration> _configMock;
        private readonly Mock<IWorkflowEmailService> _workflowEmailServiceMock;

        private readonly UserService _userService;

        public UserServiceTests()
        {
            _userRepoMock = new Mock<IUserRepository>();
            _workflowEmailServiceMock = new Mock<IWorkflowEmailService>();
            _refreshTokenRepoMock = new Mock<IRefreshTokenRepository>();
            _assignmentRepoMock = new Mock<IAssignmentRepository>();
            _projectRepoMock = new Mock<IProjectRepository>();
            _globalBanRequestRepoMock = new Mock<IRepository<GlobalUserBanRequest>>();
            _appNotificationRepoMock = new Mock<IRepository<AppNotification>>();
            _logServiceMock = new Mock<IActivityLogService>();
            _notificationMock = new Mock<IAppNotificationService>();
            _configMock = new Mock<Microsoft.Extensions.Configuration.IConfiguration>();

            var jwtSection = new Mock<Microsoft.Extensions.Configuration.IConfigurationSection>();
            jwtSection.Setup(s => s["Key"]).Returns("ThisIsAVeryLongSecretKeyForTestingPurposes123456");
            jwtSection.Setup(s => s["Issuer"]).Returns("TestIssuer");
            jwtSection.Setup(s => s["Audience"]).Returns("TestAudience");
            _configMock.Setup(c => c.GetSection("Jwt")).Returns(jwtSection.Object);
            _globalBanRequestRepoMock
                .Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<GlobalUserBanRequest, bool>>>()))
                .ReturnsAsync(new List<GlobalUserBanRequest>());
            _globalBanRequestRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _globalBanRequestRepoMock
                .Setup(r => r.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
                .Returns<Func<Task>, CancellationToken>((operation, _) => operation());
            _appNotificationRepoMock
                .Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<AppNotification, bool>>>()))
                .ReturnsAsync(new List<AppNotification>());
            _appNotificationRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            _userService = new UserService(
                _userRepoMock.Object,
                _refreshTokenRepoMock.Object,
                _configMock.Object,
                _assignmentRepoMock.Object,
                _logServiceMock.Object,
                _projectRepoMock.Object,
                _globalBanRequestRepoMock.Object,
                _appNotificationRepoMock.Object,
                _notificationMock.Object,
                _workflowEmailServiceMock.Object
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
            _workflowEmailServiceMock.Verify(w => w.SendWelcomeEmailAsync(It.IsAny<User>(), It.IsAny<User?>(), null), Times.Once);
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
            var manager = new User { Id = "manager-1", FullName = "Manager", Email = "manager@test.com", Role = UserRoles.Manager };
            _userRepoMock.Setup(r => r.IsEmailExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
            _userRepoMock.Setup(r => r.AddAsync(It.IsAny<User>())).Returns(Task.CompletedTask);
            _userRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _userRepoMock.Setup(r => r.GetByIdAsync("manager-1")).ReturnsAsync(manager);

            var result = await _userService.RegisterAsync("Jane Doe", "jane@test.com", "Password@123", UserRoles.Annotator, "manager-1");

            Assert.NotNull(result);
            Assert.Equal("manager-1", result.ManagerId);
            _workflowEmailServiceMock.Verify(w => w.SendWelcomeEmailAsync(It.Is<User>(u => u.Email == "jane@test.com"), manager, null), Times.Once);
        }

        #endregion

        #region ForgotPasswordAsync Tests

        [Fact]
        public async Task ForgotPasswordAsync_WithExistingUser_NotifiesAdminsWithoutChangingPassword()
        {
            var existingPasswordHash = BCrypt.Net.BCrypt.HashPassword("Password@123");
            var user = new User
            {
                Id = "user-1",
                Email = "john@test.com",
                FullName = "John Doe",
                Role = UserRoles.Annotator,
                PasswordHash = existingPasswordHash
            };
            var admins = new List<User>
            {
                new User { Id = "admin-1", Email = "admin@test.com", FullName = "Admin", Role = UserRoles.Admin, IsActive = true }
            };

            _userRepoMock.Setup(r => r.GetUserByEmailAsync(user.Email)).ReturnsAsync(user);
            _userRepoMock.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>())).ReturnsAsync(admins);

            var result = await _userService.ForgotPasswordAsync(user.Email);

            Assert.Equal("If your email is registered in our system, administrators have been notified and will review your password reset request.", result.Message);
            Assert.True(result.EmailDelivered);
            Assert.True(result.NotificationDelivered);
            Assert.Equal(existingPasswordHash, user.PasswordHash);
            _userRepoMock.Verify(r => r.Update(It.IsAny<User>()), Times.Never);
            _workflowEmailServiceMock.Verify(w => w.SendForgotPasswordRequestEmailsAsync(user, It.Is<IReadOnlyCollection<User>>(a => a.Count == 1)), Times.Once);
            _notificationMock.Verify(n => n.SendNotificationAsync(
                "admin-1",
                It.Is<string>(message => message.Contains(user.Email)),
                "PasswordResetRequest"), Times.Once);
        }

        [Fact]
        public async Task ForgotPasswordAsync_WhenAdminNotificationFails_StillReturnsResponseAndLogsFailure()
        {
            var user = new User
            {
                Id = "user-1",
                Email = "john@test.com",
                FullName = "John Doe",
                Role = UserRoles.Annotator,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password@123")
            };
            var admins = new List<User>
            {
                new User { Id = "admin-1", Email = "admin@test.com", FullName = "Admin", Role = UserRoles.Admin, IsActive = true }
            };

            _userRepoMock.Setup(r => r.GetUserByEmailAsync(user.Email)).ReturnsAsync(user);
            _userRepoMock.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>())).ReturnsAsync(admins);
            _notificationMock
                .Setup(n => n.SendNotificationAsync("admin-1", It.IsAny<string>(), "PasswordResetRequest"))
                .ThrowsAsync(new InvalidOperationException("Notification hub is offline"));

            var result = await _userService.ForgotPasswordAsync(user.Email);

            Assert.Equal("If your email is registered in our system, administrators have been notified and will review your password reset request.", result.Message);
            Assert.True(result.EmailDelivered);
            Assert.False(result.NotificationDelivered);
            _workflowEmailServiceMock.Verify(
                w => w.SendForgotPasswordRequestEmailsAsync(user, It.Is<IReadOnlyCollection<User>>(a => a.Count == 1)),
                Times.Once);
            _logServiceMock.Verify(
                l => l.LogActionAsync(
                    user.Id,
                    "ForgotPasswordRequested",
                    "User",
                    user.Id,
                    It.IsAny<string>(),
                    null),
                Times.Once);
            _logServiceMock.Verify(
                l => l.LogActionAsync(
                    user.Id,
                    "ForgotPasswordNotificationError",
                    "User",
                    user.Id,
                    It.Is<string>(description => description.Contains("admin-1")),
                    null),
                Times.Once);
        }

        [Fact]
        public async Task AdminChangeUserPasswordAsync_SendsResetEmailToUser()
        {
            var user = new User
            {
                Id = "user-1",
                Email = "john@test.com",
                FullName = "John Doe",
                Role = UserRoles.Annotator,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("OldPassword@123")
            };
            string? temporaryPassword = null;

            _userRepoMock.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);
            _userRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _workflowEmailServiceMock
                .Setup(w => w.SendAdminPasswordResetEmailAsync(user, It.IsAny<string>()))
                .Callback<User, string>((_, password) => temporaryPassword = password)
                .Returns(Task.CompletedTask);

            var result = await _userService.AdminChangeUserPasswordAsync("admin-1", user.Id);

            Assert.True(result.EmailDelivered);
            Assert.NotNull(temporaryPassword);
            Assert.InRange(temporaryPassword!.Length, 10, 12);
            Assert.True(BCrypt.Net.BCrypt.Verify(temporaryPassword, user.PasswordHash));
            _workflowEmailServiceMock.Verify(w => w.SendAdminPasswordResetEmailAsync(user, It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task CreateManagedUserAsync_GeneratesTemporaryPasswordAndEmailsUser()
        {
            User? createdUser = null;
            string? temporaryPassword = null;

            _userRepoMock.Setup(r => r.IsEmailExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
            _userRepoMock.Setup(r => r.HasAdminRoleAsync()).ReturnsAsync(false);
            _userRepoMock
                .Setup(r => r.AddAsync(It.IsAny<User>()))
                .Callback<User>(user =>
                {
                    user.Id = "managed-user-1";
                    createdUser = user;
                })
                .Returns(Task.CompletedTask);
            _userRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _workflowEmailServiceMock
                .Setup(w => w.SendWelcomeEmailAsync(It.IsAny<User>(), It.IsAny<User?>(), It.IsAny<string?>()))
                .Callback<User, User?, string?>((_, _, password) => temporaryPassword = password)
                .Returns(Task.CompletedTask);

            var result = await _userService.CreateManagedUserAsync("admin-1", "Jane Doe", "jane@test.com", UserRoles.Reviewer);

            Assert.True(result.EmailDelivered);
            Assert.NotNull(createdUser);
            Assert.NotNull(temporaryPassword);
            Assert.InRange(temporaryPassword!.Length, 10, 12);
            Assert.True(BCrypt.Net.BCrypt.Verify(temporaryPassword, createdUser!.PasswordHash));
            _workflowEmailServiceMock.Verify(
                w => w.SendWelcomeEmailAsync(
                    It.Is<User>(user => user.Email == "jane@test.com"),
                    It.IsAny<User?>(),
                    It.IsAny<string?>()),
                Times.Once);
        }

        [Fact]
        public async Task RegisterAsync_WhenWelcomeEmailFails_StillCreatesUserAndLogsFailure()
        {
            _userRepoMock.Setup(r => r.IsEmailExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
            _userRepoMock.Setup(r => r.HasAdminRoleAsync()).ReturnsAsync(false);
            _userRepoMock.Setup(r => r.AddAsync(It.IsAny<User>())).Returns(Task.CompletedTask);
            _userRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _workflowEmailServiceMock
                .Setup(w => w.SendWelcomeEmailAsync(It.IsAny<User>(), It.IsAny<User?>(), null))
                .ThrowsAsync(new InvalidOperationException("SMTP authentication failed"));

            var result = await _userService.RegisterAsync("John Doe", "john@test.com", "Password@123", UserRoles.Annotator);

            Assert.Equal("john@test.com", result.Email);
            _logServiceMock.Verify(
                l => l.LogActionAsync(
                    It.IsAny<string>(),
                    "WelcomeEmailError",
                    "User",
                    It.IsAny<string>(),
                    It.Is<string>(description => description.Contains("SMTP authentication failed")),
                    null),
                Times.Once);
        }

        [Fact]
        public async Task ForgotPasswordAsync_WhenAdminEmailFails_ReturnsWarningAndLogsFailure()
        {
            var user = new User
            {
                Id = "user-1",
                Email = "john@test.com",
                FullName = "John Doe",
                Role = UserRoles.Annotator,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password@123")
            };
            var admins = new List<User>
            {
                new User { Id = "admin-1", Email = "admin@test.com", FullName = "Admin", Role = UserRoles.Admin, IsActive = true }
            };

            _userRepoMock.Setup(r => r.GetUserByEmailAsync(user.Email)).ReturnsAsync(user);
            _userRepoMock.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>())).ReturnsAsync(admins);
            _workflowEmailServiceMock
                .Setup(w => w.SendForgotPasswordRequestEmailsAsync(user, It.IsAny<IReadOnlyCollection<User>>()))
                .ThrowsAsync(new InvalidOperationException("SMTP authentication failed"));

            var result = await _userService.ForgotPasswordAsync(user.Email);

            Assert.False(result.EmailDelivered);
            Assert.True(result.NotificationDelivered);
            Assert.Contains("SMTP email delivery", result.Message);
            _logServiceMock.Verify(
                l => l.LogActionAsync(
                    user.Id,
                    "ForgotPasswordEmailError",
                    "User",
                    user.Id,
                    It.Is<string>(description => description.Contains("SMTP authentication failed")),
                    null),
                Times.Once);
        }

        [Fact]
        public async Task ForgotPasswordAsync_WhenPickupDirectoryModeEnabled_ReturnsLocalDeliveryMessage()
        {
            var user = new User
            {
                Id = "user-1",
                Email = "john@test.com",
                FullName = "John Doe",
                Role = UserRoles.Annotator,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password@123")
            };
            var admins = new List<User>
            {
                new User { Id = "admin-1", Email = "admin@test.com", FullName = "Admin", Role = UserRoles.Admin, IsActive = true }
            };

            _configMock.Setup(c => c["EmailSettings:DeliveryMode"]).Returns("PickupDirectory");
            _configMock.Setup(c => c["EmailSettings:PickupDirectory"]).Returns("mail-drop");
            _userRepoMock.Setup(r => r.GetUserByEmailAsync(user.Email)).ReturnsAsync(user);
            _userRepoMock.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>())).ReturnsAsync(admins);

            var result = await _userService.ForgotPasswordAsync(user.Email);
            var expectedPickupPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "mail-drop"));

            Assert.True(result.EmailDelivered);
            Assert.Equal("PickupDirectory", result.EmailDeliveryMode);
            Assert.Equal(expectedPickupPath, result.EmailDeliveryTarget);
            Assert.Contains(expectedPickupPath, result.Message);
        }

        [Fact]
        public async Task AdminChangeUserPasswordAsync_WhenResetEmailFails_ReturnsWarningResult()
        {
            var user = new User
            {
                Id = "user-1",
                Email = "john@test.com",
                FullName = "John Doe",
                Role = UserRoles.Annotator
            };

            _userRepoMock.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);
            _userRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _workflowEmailServiceMock
                .Setup(w => w.SendAdminPasswordResetEmailAsync(user, It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("SMTP authentication failed"));

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword("OldPassword@123");
            var previousPasswordHash = user.PasswordHash;

            var result = await _userService.AdminChangeUserPasswordAsync("admin-1", user.Id);

            Assert.False(result.EmailDelivered);
            Assert.Contains("old password remains active", result.Message);
            Assert.Equal(previousPasswordHash, user.PasswordHash);
            _logServiceMock.Verify(
                l => l.LogActionAsync(
                    "admin-1",
                    "AdminChangePasswordEmailError",
                    "User",
                    user.Id,
                    It.Is<string>(description => description.Contains("SMTP authentication failed")),
                    null),
                Times.Once);
        }

        [Fact]
        public async Task AdminChangeUserPasswordAsync_WhenPickupDirectoryModeEnabled_ReturnsLocalDeliveryMessage()
        {
            var user = new User
            {
                Id = "user-1",
                Email = "john@test.com",
                FullName = "John Doe",
                Role = UserRoles.Annotator
            };

            _configMock.Setup(c => c["EmailSettings:DeliveryMode"]).Returns("PickupDirectory");
            _configMock.Setup(c => c["EmailSettings:PickupDirectory"]).Returns("mail-drop");
            _userRepoMock.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);
            _userRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            var result = await _userService.AdminChangeUserPasswordAsync("admin-1", user.Id);
            var expectedPickupPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "mail-drop"));

            Assert.True(result.EmailDelivered);
            Assert.Equal("PickupDirectory", result.EmailDeliveryMode);
            Assert.Equal(expectedPickupPath, result.EmailDeliveryTarget);
            Assert.Contains(expectedPickupPath, result.Message);
        }

        [Fact]
        public async Task ImportUsersFromExcelAsync_GeneratesUniqueTemporaryPasswordsPerUser()
        {
            var createdUsers = new List<User>();
            var temporaryPasswords = new List<string>();

            _userRepoMock.Setup(r => r.IsEmailExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
            _userRepoMock
                .Setup(r => r.AddAsync(It.IsAny<User>()))
                .Callback<User>(user =>
                {
                    user.Id = $"user-{createdUsers.Count + 1}";
                    createdUsers.Add(user);
                })
                .Returns(Task.CompletedTask);
            _userRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _workflowEmailServiceMock
                .Setup(w => w.SendWelcomeEmailAsync(It.IsAny<User>(), It.IsAny<User?>(), It.IsAny<string?>()))
                .Callback<User, User?, string?>((_, _, password) =>
                {
                    if (!string.IsNullOrWhiteSpace(password))
                    {
                        temporaryPasswords.Add(password);
                    }
                })
                .Returns(Task.CompletedTask);

            using var workbook = new XLWorkbook();
            var worksheet = workbook.AddWorksheet("Users");
            worksheet.Cell(1, 1).Value = "Email";
            worksheet.Cell(1, 2).Value = "FullName";
            worksheet.Cell(1, 3).Value = "Role";
            worksheet.Cell(1, 4).Value = "ManagerEmail";
            worksheet.Cell(2, 1).Value = "annotator1@test.com";
            worksheet.Cell(2, 2).Value = "Annotator 1";
            worksheet.Cell(2, 3).Value = UserRoles.Annotator;
            worksheet.Cell(3, 1).Value = "reviewer1@test.com";
            worksheet.Cell(3, 2).Value = "Reviewer 1";
            worksheet.Cell(3, 3).Value = UserRoles.Reviewer;

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            stream.Position = 0;

            var result = await _userService.ImportUsersFromExcelAsync(stream, "admin-1");

            Assert.Equal(2, result.SuccessCount);
            Assert.Equal(2, createdUsers.Count);
            Assert.Equal(2, temporaryPasswords.Count);
            Assert.Equal(2, temporaryPasswords.Distinct().Count());
            Assert.All(temporaryPasswords, password => Assert.InRange(password.Length, 10, 12));
            Assert.All(createdUsers.Zip(temporaryPasswords), pair =>
                Assert.True(BCrypt.Net.BCrypt.Verify(pair.Second, pair.First.PasswordHash)));
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
        public async Task LoginAsync_WithInactiveUser_ThrowsUnauthorizedAccessException()
        {
            var user = new User
            {
                Id = "user-1",
                Email = "john@test.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password@123"),
                IsActive = false
            };

            _userRepoMock.Setup(r => r.GetUserByEmailAsync("john@test.com")).ReturnsAsync(user);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
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
            _userRepoMock.Setup(r => r.GetByIdAsync("admin-1")).ReturnsAsync(new User { Id = "admin-1", Role = UserRoles.Admin });
            _assignmentRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Assignment>());
            _projectRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Project>());
            _globalBanRequestRepoMock
                .Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<GlobalUserBanRequest, bool>>>()))
                .ReturnsAsync(new List<GlobalUserBanRequest>());
            _assignmentRepoMock.Setup(r => r.HasPendingTasksAsync(user.Id, user.Role)).ReturnsAsync(true);

            await Assert.ThrowsAsync<Exception>(() =>
                _userService.ToggleUserStatusAsync("user-1", false, "admin-1"));
        }

        [Fact]
        public async Task ToggleUserStatusAsync_ManagedUserWithUnfinishedProjects_CreatesPendingGlobalBanRequest()
        {
            var user = new User
            {
                Id = "user-1",
                Role = UserRoles.Annotator,
                FullName = "Worker One",
                Email = "worker@test.com",
                IsActive = true,
                ManagerId = "manager-1"
            };
            var admin = new User { Id = "admin-1", FullName = "Admin", Role = UserRoles.Admin };
            var manager = new User { Id = "manager-1", FullName = "Manager One", Role = UserRoles.Manager };
            GlobalUserBanRequest? capturedRequest = null;

            _userRepoMock.Setup(r => r.GetByIdAsync("user-1")).ReturnsAsync(user);
            _userRepoMock.Setup(r => r.GetByIdAsync("admin-1")).ReturnsAsync(admin);
            _userRepoMock.Setup(r => r.GetByIdAsync("manager-1")).ReturnsAsync(manager);
            _assignmentRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Assignment>
            {
                new Assignment { Id = 1, ProjectId = 9, AnnotatorId = user.Id, Status = TaskStatusConstants.Assigned }
            });
            _projectRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Project>
            {
                new Project { Id = 9, Name = "Active Project", Status = ProjectStatusConstants.Active, ManagerId = "manager-1" }
            });
            _globalBanRequestRepoMock
                .Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<GlobalUserBanRequest, bool>>>()))
                .ReturnsAsync(new List<GlobalUserBanRequest>());
            _globalBanRequestRepoMock
                .Setup(r => r.AddAsync(It.IsAny<GlobalUserBanRequest>()))
                .Callback<GlobalUserBanRequest>(request =>
                {
                    request.Id = 42;
                    capturedRequest = request;
                })
                .Returns(Task.CompletedTask);
            _globalBanRequestRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _notificationMock
                .Setup(n => n.SendNotificationAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>()))
                .Returns(Task.CompletedTask);

            var result = await _userService.ToggleUserStatusAsync("user-1", false, "admin-1");

            Assert.NotNull(capturedRequest);
            Assert.True(result.RequiresManagerApproval);
            Assert.Equal(42, result.GlobalBanRequestId);
            Assert.True(user.IsActive);
            _notificationMock.Verify(n => n.SendNotificationAsync(
                "manager-1",
                It.Is<string>(message => message.Contains("Worker One") && message.Contains("Active Project")),
                "GlobalUserBanApproval",
                "GlobalUserBanRequest",
                "42",
                "ResolveGlobalUserBanRequest",
                It.Is<string>(metadata =>
                    metadata.Contains("\"requestStatus\":\"Pending\"") &&
                    metadata.Contains("\"targetUserRole\":\"Annotator\"") &&
                    metadata.Contains("\"requestedByAdminName\":\"Admin\"") &&
                    metadata.Contains("\"unfinishedProjectCount\":1") &&
                    metadata.Contains("\"unfinishedProjects\":[{\"id\":9,\"name\":\"Active Project\",\"status\":\"Active\"}]"))),
                Times.Once);
        }

        [Fact]
        public async Task ToggleUserStatusAsync_UsesProjectManagerInsteadOfProfileManagerForGlobalBanApproval()
        {
            var user = new User
            {
                Id = "user-1",
                Role = UserRoles.Annotator,
                FullName = "Worker One",
                Email = "worker@test.com",
                IsActive = true,
                ManagerId = "profile-manager"
            };
            var admin = new User { Id = "admin-1", FullName = "Admin", Role = UserRoles.Admin };
            var projectManager = new User { Id = "project-manager", FullName = "Project Manager", Role = UserRoles.Manager };
            GlobalUserBanRequest? capturedRequest = null;

            _userRepoMock.Setup(r => r.GetByIdAsync("user-1")).ReturnsAsync(user);
            _userRepoMock.Setup(r => r.GetByIdAsync("admin-1")).ReturnsAsync(admin);
            _userRepoMock.Setup(r => r.GetByIdAsync("project-manager")).ReturnsAsync(projectManager);
            _assignmentRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Assignment>
            {
                new Assignment { Id = 1, ProjectId = 9, AnnotatorId = user.Id, Status = TaskStatusConstants.Assigned }
            });
            _projectRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Project>
            {
                new Project { Id = 9, Name = "Managed By Project Owner", Status = ProjectStatusConstants.Active, ManagerId = "project-manager" }
            });
            _globalBanRequestRepoMock
                .Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<GlobalUserBanRequest, bool>>>()))
                .ReturnsAsync(new List<GlobalUserBanRequest>());
            _globalBanRequestRepoMock
                .Setup(r => r.AddAsync(It.IsAny<GlobalUserBanRequest>()))
                .Callback<GlobalUserBanRequest>(request =>
                {
                    request.Id = 52;
                    capturedRequest = request;
                })
                .Returns(Task.CompletedTask);
            _globalBanRequestRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _notificationMock
                .Setup(n => n.SendNotificationAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>()))
                .Returns(Task.CompletedTask);

            var result = await _userService.ToggleUserStatusAsync("user-1", false, "admin-1");

            Assert.NotNull(capturedRequest);
            Assert.True(result.RequiresManagerApproval);
            Assert.Equal("project-manager", capturedRequest!.ManagerId);
            Assert.Contains("Project Manager", result.Message);
            _notificationMock.Verify(n => n.SendNotificationAsync(
                "project-manager",
                It.Is<string>(message => message.Contains("you manage", StringComparison.OrdinalIgnoreCase)),
                "GlobalUserBanApproval",
                "GlobalUserBanRequest",
                "52",
                "ResolveGlobalUserBanRequest",
                It.Is<string>(metadata => metadata.Contains("\"managerId\":\"project-manager\""))),
                Times.Once);
        }

        [Fact]
        public async Task ToggleUserStatusAsync_MultipleResponsibleProjectManagers_ThrowsClearException()
        {
            var user = new User
            {
                Id = "user-1",
                Role = UserRoles.Annotator,
                FullName = "Worker One",
                Email = "worker@test.com",
                IsActive = true
            };
            var admin = new User { Id = "admin-1", FullName = "Admin", Role = UserRoles.Admin };
            _userRepoMock.Setup(r => r.GetByIdAsync("user-1")).ReturnsAsync(user);
            _userRepoMock.Setup(r => r.GetByIdAsync("admin-1")).ReturnsAsync(admin);
            _userRepoMock.Setup(r => r.GetByIdAsync("manager-1")).ReturnsAsync(new User { Id = "manager-1", FullName = "Manager One", Role = UserRoles.Manager });
            _userRepoMock.Setup(r => r.GetByIdAsync("manager-2")).ReturnsAsync(new User { Id = "manager-2", FullName = "Manager Two", Role = UserRoles.Manager });
            _assignmentRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Assignment>
            {
                new Assignment { Id = 1, ProjectId = 9, AnnotatorId = user.Id, Status = TaskStatusConstants.Assigned },
                new Assignment { Id = 2, ProjectId = 10, AnnotatorId = user.Id, Status = TaskStatusConstants.InProgress }
            });
            _projectRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Project>
            {
                new Project { Id = 9, Name = "Project One", Status = ProjectStatusConstants.Active, ManagerId = "manager-1" },
                new Project { Id = 10, Name = "Project Two", Status = ProjectStatusConstants.Active, ManagerId = "manager-2" }
            });
            _globalBanRequestRepoMock
                .Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<GlobalUserBanRequest, bool>>>()))
                .ReturnsAsync(new List<GlobalUserBanRequest>());

            var ex = await Assert.ThrowsAsync<Exception>(() =>
                _userService.ToggleUserStatusAsync("user-1", false, "admin-1"));

            Assert.Contains("multiple managers", ex.Message, StringComparison.OrdinalIgnoreCase);
            _globalBanRequestRepoMock.Verify(r => r.AddAsync(It.IsAny<GlobalUserBanRequest>()), Times.Never);
        }

        [Fact]
        public async Task ToggleUserStatusAsync_ObsoletePendingRequestForWrongManager_IsClosedBeforeCreatingNewOne()
        {
            var user = new User
            {
                Id = "user-1",
                Role = UserRoles.Annotator,
                FullName = "Worker One",
                Email = "worker@test.com",
                IsActive = true,
                ManagerId = "legacy-manager"
            };
            var admin = new User { Id = "admin-1", FullName = "Admin", Role = UserRoles.Admin };
            var rightManager = new User { Id = "manager-2", FullName = "Manager Two", Role = UserRoles.Manager };
            var obsoleteRequest = new GlobalUserBanRequest
            {
                Id = 11,
                TargetUserId = "user-1",
                RequestedByAdminId = "admin-1",
                ManagerId = "legacy-manager",
                Status = GlobalUserBanRequestStatusConstants.Pending,
                RequestedAt = DateTime.UtcNow.AddMinutes(-30)
            };
            GlobalUserBanRequest? createdRequest = null;

            _userRepoMock.Setup(r => r.GetByIdAsync("user-1")).ReturnsAsync(user);
            _userRepoMock.Setup(r => r.GetByIdAsync("admin-1")).ReturnsAsync(admin);
            _userRepoMock.Setup(r => r.GetByIdAsync("manager-2")).ReturnsAsync(rightManager);
            _assignmentRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Assignment>
            {
                new Assignment { Id = 1, ProjectId = 9, AnnotatorId = user.Id, Status = TaskStatusConstants.Assigned }
            });
            _projectRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Project>
            {
                new Project { Id = 9, Name = "Right Project", Status = ProjectStatusConstants.Active, ManagerId = "manager-2" }
            });
            _globalBanRequestRepoMock
                .Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<GlobalUserBanRequest, bool>>>()))
                .ReturnsAsync(new List<GlobalUserBanRequest> { obsoleteRequest });
            _globalBanRequestRepoMock
                .Setup(r => r.AddAsync(It.IsAny<GlobalUserBanRequest>()))
                .Callback<GlobalUserBanRequest>(request =>
                {
                    request.Id = 12;
                    createdRequest = request;
                })
                .Returns(Task.CompletedTask);
            _globalBanRequestRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _notificationMock
                .Setup(n => n.SendNotificationAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>()))
                .Returns(Task.CompletedTask);

            var result = await _userService.ToggleUserStatusAsync("user-1", false, "admin-1");

            Assert.True(result.RequiresManagerApproval);
            Assert.NotNull(createdRequest);
            Assert.Equal("manager-2", createdRequest!.ManagerId);
            Assert.Equal(GlobalUserBanRequestStatusConstants.Rejected, obsoleteRequest.Status);
            Assert.Contains("Automatically closed", obsoleteRequest.DecisionNote ?? string.Empty);
            _globalBanRequestRepoMock.Verify(r => r.Update(It.Is<GlobalUserBanRequest>(request => request.Id == 11)), Times.AtLeastOnce);
        }

        [Fact]
        public async Task ToggleUserStatusAsync_ValidDeactivation_DeactivatesUser()
        {
            var user = new User
            {
                Id = "user-1",
                Role = UserRoles.Annotator,
                FullName = "Worker One",
                IsActive = true
            };

            _userRepoMock.Setup(r => r.GetByIdAsync("user-1")).ReturnsAsync(user);
            _userRepoMock.Setup(r => r.GetByIdAsync("admin-1")).ReturnsAsync(new User { Id = "admin-1", Role = UserRoles.Admin });
            _assignmentRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Assignment>());
            _projectRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Project>());
            _globalBanRequestRepoMock
                .Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<GlobalUserBanRequest, bool>>>()))
                .ReturnsAsync(new List<GlobalUserBanRequest>());
            _assignmentRepoMock.Setup(r => r.HasPendingTasksAsync(user.Id, user.Role)).ReturnsAsync(false);
            _userRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            var result = await _userService.ToggleUserStatusAsync("user-1", false, "admin-1");

            Assert.False(user.IsActive);
            Assert.False(result.RequiresManagerApproval);
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

            var result = await _userService.ToggleUserStatusAsync("user-1", true);

            Assert.True(user.IsActive);
            Assert.True(result.IsActive);
        }

        [Fact]
        public async Task ResolveGlobalUserBanRequestAsync_Approve_DeactivatesUserAndRemovesFromProjects()
        {
            var banRequest = new GlobalUserBanRequest
            {
                Id = 42,
                TargetUserId = "user-1",
                RequestedByAdminId = "admin-1",
                ManagerId = "manager-1",
                Status = GlobalUserBanRequestStatusConstants.Pending,
                RequestedAt = DateTime.UtcNow.AddMinutes(-10)
            };
            var targetUser = new User
            {
                Id = "user-1",
                FullName = "Worker One",
                Email = "worker@test.com",
                Role = UserRoles.Annotator,
                IsActive = true,
                ManagerId = "manager-1"
            };
            var admin = new User { Id = "admin-1", FullName = "Admin", Email = "admin@test.com", Role = UserRoles.Admin };
            var manager = new User { Id = "manager-1", FullName = "Manager", Email = "manager@test.com", Role = UserRoles.Manager };
            var assignment = new Assignment
            {
                Id = 100,
                ProjectId = 9,
                DataItemId = 5,
                AnnotatorId = "user-1",
                Status = TaskStatusConstants.Submitted
            };
            var detailedProject = new Project
            {
                Id = 9,
                Name = "Pending Project",
                Status = ProjectStatusConstants.Active,
                ManagerId = "manager-1",
                DataItems = new List<DataItem>
                {
                    new DataItem
                    {
                        Id = 5,
                        ProjectId = 9,
                        Status = TaskStatusConstants.Submitted,
                        Assignments = new List<Assignment> { assignment }
                    }
                }
            };
            var managerNotification = new AppNotification
            {
                Id = 77,
                UserId = "manager-1",
                ReferenceType = "GlobalUserBanRequest",
                ReferenceId = "42",
                MetadataJson = "{\"requestStatus\":\"Pending\"}",
                IsRead = false
            };

            _globalBanRequestRepoMock.Setup(r => r.GetByIdAsync(42)).ReturnsAsync(banRequest);
            _globalBanRequestRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _userRepoMock.Setup(r => r.GetByIdAsync("user-1")).ReturnsAsync(targetUser);
            _userRepoMock.Setup(r => r.GetByIdAsync("admin-1")).ReturnsAsync(admin);
            _userRepoMock.Setup(r => r.GetByIdAsync("manager-1")).ReturnsAsync(manager);
            _userRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _assignmentRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Assignment> { assignment });
            _assignmentRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _projectRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Project>
            {
                new Project { Id = 9, Name = "Pending Project", Status = ProjectStatusConstants.Active, ManagerId = "manager-1" }
            });
            _projectRepoMock.Setup(r => r.GetProjectWithDetailsAsync(9)).ReturnsAsync(detailedProject);
            _projectRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _appNotificationRepoMock
                .Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<AppNotification, bool>>>()))
                .ReturnsAsync(new List<AppNotification> { managerNotification });
            _appNotificationRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _notificationMock
                .Setup(n => n.SendNotificationAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>()))
                .Returns(Task.CompletedTask);

            await _userService.ResolveGlobalUserBanRequestAsync(42, "manager-1", new ResolveGlobalUserBanRequest
            {
                Approve = true
            });

            Assert.False(targetUser.IsActive);
            Assert.Equal(GlobalUserBanRequestStatusConstants.Approved, banRequest.Status);
            Assert.True(managerNotification.IsRead);
            Assert.Contains("Approved", managerNotification.MetadataJson ?? string.Empty);
            _assignmentRepoMock.Verify(r => r.Delete(It.Is<Assignment>(a => a.Id == 100)), Times.Once);
            _globalBanRequestRepoMock.Verify(
                r => r.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()),
                Times.Once);
            _notificationMock.Verify(n => n.SendNotificationAsync(
                "admin-1",
                It.Is<string>(message => message.Contains("approved")),
                "GlobalUserBanApproved",
                "GlobalUserBanRequest",
                "42",
                null,
                null), Times.Once);
            _notificationMock.Verify(n => n.SendNotificationAsync(
                "user-1",
                It.Is<string>(message => message.Contains("globally banned")),
                "AccountDeactivated",
                "GlobalUserBanRequest",
                "42",
                null,
                null), Times.Once);
        }

        [Fact]
        public async Task ResolveGlobalUserBanRequestAsync_Reject_KeepsUserActive()
        {
            var banRequest = new GlobalUserBanRequest
            {
                Id = 55,
                TargetUserId = "user-1",
                RequestedByAdminId = "admin-1",
                ManagerId = "manager-1",
                Status = GlobalUserBanRequestStatusConstants.Pending,
                RequestedAt = DateTime.UtcNow.AddMinutes(-10)
            };
            var targetUser = new User
            {
                Id = "user-1",
                FullName = "Worker One",
                Email = "worker@test.com",
                Role = UserRoles.Annotator,
                IsActive = true,
                ManagerId = "manager-1"
            };
            var admin = new User { Id = "admin-1", FullName = "Admin", Email = "admin@test.com", Role = UserRoles.Admin };
            var manager = new User { Id = "manager-1", FullName = "Manager", Email = "manager@test.com", Role = UserRoles.Manager };
            var managerNotification = new AppNotification
            {
                Id = 88,
                UserId = "manager-1",
                ReferenceType = "GlobalUserBanRequest",
                ReferenceId = "55",
                MetadataJson = "{\"requestStatus\":\"Pending\"}",
                IsRead = false
            };

            _globalBanRequestRepoMock.Setup(r => r.GetByIdAsync(55)).ReturnsAsync(banRequest);
            _globalBanRequestRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _userRepoMock.Setup(r => r.GetByIdAsync("user-1")).ReturnsAsync(targetUser);
            _userRepoMock.Setup(r => r.GetByIdAsync("admin-1")).ReturnsAsync(admin);
            _userRepoMock.Setup(r => r.GetByIdAsync("manager-1")).ReturnsAsync(manager);
            _appNotificationRepoMock
                .Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<AppNotification, bool>>>()))
                .ReturnsAsync(new List<AppNotification> { managerNotification });
            _appNotificationRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _notificationMock
                .Setup(n => n.SendNotificationAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>()))
                .Returns(Task.CompletedTask);

            await _userService.ResolveGlobalUserBanRequestAsync(55, "manager-1", new ResolveGlobalUserBanRequest
            {
                Approve = false
            });

            Assert.True(targetUser.IsActive);
            Assert.Equal(GlobalUserBanRequestStatusConstants.Rejected, banRequest.Status);
            _globalBanRequestRepoMock.Verify(
                r => r.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()),
                Times.Once);
            _notificationMock.Verify(n => n.SendNotificationAsync(
                "admin-1",
                It.Is<string>(message => message.Contains("rejected")),
                "GlobalUserBanRejected",
                "GlobalUserBanRequest",
                "55",
                null,
                null), Times.Once);
        }

        [Fact]
        public async Task ResolveGlobalUserBanRequestAsync_NotificationFailure_DoesNotRollbackApprovedDecision()
        {
            var banRequest = new GlobalUserBanRequest
            {
                Id = 42,
                TargetUserId = "user-1",
                RequestedByAdminId = "admin-1",
                ManagerId = "manager-1",
                Status = GlobalUserBanRequestStatusConstants.Pending,
                RequestedAt = DateTime.UtcNow.AddMinutes(-10)
            };
            var targetUser = new User
            {
                Id = "user-1",
                FullName = "Worker One",
                Email = "worker@test.com",
                Role = UserRoles.Annotator,
                IsActive = true,
                ManagerId = "manager-1"
            };
            var admin = new User { Id = "admin-1", FullName = "Admin", Email = "admin@test.com", Role = UserRoles.Admin };
            var manager = new User { Id = "manager-1", FullName = "Manager", Email = "manager@test.com", Role = UserRoles.Manager };
            var assignment = new Assignment
            {
                Id = 100,
                ProjectId = 9,
                DataItemId = 5,
                AnnotatorId = "user-1",
                Status = TaskStatusConstants.Submitted
            };
            var detailedProject = new Project
            {
                Id = 9,
                Name = "Pending Project",
                Status = ProjectStatusConstants.Active,
                ManagerId = "manager-1",
                DataItems = new List<DataItem>
                {
                    new DataItem
                    {
                        Id = 5,
                        ProjectId = 9,
                        Status = TaskStatusConstants.Submitted,
                        Assignments = new List<Assignment> { assignment }
                    }
                }
            };
            var managerNotification = new AppNotification
            {
                Id = 77,
                UserId = "manager-1",
                ReferenceType = "GlobalUserBanRequest",
                ReferenceId = "42",
                MetadataJson = "{\"requestStatus\":\"Pending\"}",
                IsRead = false
            };

            _globalBanRequestRepoMock.Setup(r => r.GetByIdAsync(42)).ReturnsAsync(banRequest);
            _globalBanRequestRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _userRepoMock.Setup(r => r.GetByIdAsync("user-1")).ReturnsAsync(targetUser);
            _userRepoMock.Setup(r => r.GetByIdAsync("admin-1")).ReturnsAsync(admin);
            _userRepoMock.Setup(r => r.GetByIdAsync("manager-1")).ReturnsAsync(manager);
            _userRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _assignmentRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Assignment> { assignment });
            _assignmentRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _projectRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Project>
            {
                new Project { Id = 9, Name = "Pending Project", Status = ProjectStatusConstants.Active, ManagerId = "manager-1" }
            });
            _projectRepoMock.Setup(r => r.GetProjectWithDetailsAsync(9)).ReturnsAsync(detailedProject);
            _projectRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _appNotificationRepoMock
                .Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<AppNotification, bool>>>()))
                .ReturnsAsync(new List<AppNotification> { managerNotification });
            _appNotificationRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _notificationMock
                .Setup(n => n.SendNotificationAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>()))
                .ThrowsAsync(new Exception("Notification store unavailable"));

            await _userService.ResolveGlobalUserBanRequestAsync(42, "manager-1", new ResolveGlobalUserBanRequest
            {
                Approve = true
            });

            Assert.False(targetUser.IsActive);
            Assert.Equal(GlobalUserBanRequestStatusConstants.Approved, banRequest.Status);
            _globalBanRequestRepoMock.Verify(
                r => r.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()),
                Times.Once);
            _logServiceMock.Verify(l => l.LogActionAsync(
                "manager-1",
                "GlobalBanResolutionNotificationError",
                "GlobalUserBanRequest",
                "42",
                It.Is<string>(description => description.Contains("could not be delivered")),
                It.IsAny<string>()), Times.AtLeastOnce);
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

        [Fact]
        public async Task GetAllUsersAsync_IncludesManagerAndUnfinishedProjectDetails()
        {
            var users = new List<User>
            {
                new User { Id = "manager-1", FullName = "Manager One", Email = "manager@test.com", Role = UserRoles.Manager, IsActive = true },
                new User
                {
                    Id = "worker-1",
                    FullName = "Worker One",
                    Email = "worker@test.com",
                    Role = UserRoles.Annotator,
                    ManagerId = "manager-1",
                    IsActive = true
                }
            };
            var activeProject = new Project
            {
                Id = 9,
                Name = "Active Project",
                Status = ProjectStatusConstants.Active,
                ManagerId = "manager-1"
            };
            var completedProject = new Project
            {
                Id = 11,
                Name = "Completed Project",
                Status = ProjectStatusConstants.Completed,
                ManagerId = "manager-1"
            };
            var assignments = new List<Assignment>
            {
                new Assignment { Id = 1, ProjectId = 9, AnnotatorId = "worker-1", Status = TaskStatusConstants.Assigned },
                new Assignment { Id = 2, ProjectId = 11, AnnotatorId = "worker-1", Status = TaskStatusConstants.Approved }
            };
            var pendingRequests = new List<GlobalUserBanRequest>
            {
                new GlobalUserBanRequest
                {
                    Id = 99,
                    TargetUserId = "worker-1",
                    Status = GlobalUserBanRequestStatusConstants.Pending
                }
            };

            _userRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(users);
            _projectRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Project> { activeProject, completedProject });
            _assignmentRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(assignments);
            _globalBanRequestRepoMock
                .Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<GlobalUserBanRequest, bool>>>()))
                .ReturnsAsync(pendingRequests);

            var result = await _userService.GetAllUsersAsync(1, 10);
            var worker = Assert.Single(result.Items, item => item.Id == "worker-1");

            Assert.Equal("manager-1", worker.ManagerId);
            Assert.Equal("Manager One", worker.ManagerName);
            Assert.Equal("manager@test.com", worker.ManagerEmail);
            Assert.Equal(2, worker.TotalProjects);
            Assert.Equal(1, worker.UnfinishedProjectCount);
            Assert.True(worker.HasPendingGlobalBanRequest);
            var unfinishedProject = Assert.Single(worker.UnfinishedProjects);
            Assert.Equal(9, unfinishedProject.Id);
            Assert.Equal("Active Project", unfinishedProject.Name);
            Assert.Equal(ProjectStatusConstants.Active, unfinishedProject.Status);
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

