using BLL.Interfaces;
using Core.DTOs.Requests;
using Core.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Xunit;

namespace API.Tests.Controllers
{
    public class AuthControllerTests
    {
        private readonly Mock<IUserService> _userServiceMock;
        private readonly Mock<IActivityLogService> _logServiceMock;
        private readonly Mock<DAL.ApplicationDbContext> _contextMock;

        public AuthControllerTests()
        {
            _userServiceMock = new Mock<IUserService>();
            _logServiceMock = new Mock<IActivityLogService>();
            _contextMock = new Mock<DAL.ApplicationDbContext>();
        }

        private AuthController CreateController(ClaimsPrincipal? user = null)
        {
            var controller = new AuthController(_userServiceMock.Object, _logServiceMock.Object, _contextMock.Object);
            if (user != null)
            {
                controller.ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = user }
                };
            }
            return controller;
        }

        #region Register Tests

        [Fact]
        public async Task Register_WithValidRequest_ReturnsOk()
        {
            var request = new RegisterRequest
            {
                FullName = "John Doe",
                Email = "john@test.com",
                Password = "Password@123"
            };

            _userServiceMock.Setup(s => s.RegisterAsync(
                request.FullName, request.Email, request.Password, "Annotator", null))
                .ReturnsAsync(new User { Id = "1", Email = request.Email });

            var controller = CreateController();
            var result = await controller.Register(request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }

        [Fact]
        public async Task Register_WithExistingEmail_ReturnsConflict()
        {
            var request = new RegisterRequest
            {
                FullName = "John Doe",
                Email = "existing@test.com",
                Password = "Password@123"
            };

            _userServiceMock.Setup(s => s.RegisterAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
                .ThrowsAsync(new Exception("Email already exists."));

            var controller = CreateController();
            var result = await controller.Register(request);

            var conflictResult = Assert.IsType<ConflictObjectResult>(result);
            Assert.NotNull(conflictResult.Value);
        }

        [Fact]
        public async Task Register_WithInvalidData_ReturnsBadRequest()
        {
            var request = new RegisterRequest
            {
                FullName = "",
                Email = "invalid-email",
                Password = "123"
            };

            _userServiceMock.Setup(s => s.RegisterAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
                .ThrowsAsync(new Exception("Invalid data"));

            var controller = CreateController();
            var result = await controller.Register(request);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        #endregion

        #region Login Tests

        [Fact]
        public async Task Login_WithValidCredentials_ReturnsTokens()
        {
            var request = new LoginRequest
            {
                Email = "john@test.com",
                Password = "Password@123"
            };

            _userServiceMock.Setup(s => s.LoginAsync(request.Email, request.Password))
                .ReturnsAsync(("access-token", "refresh-token"));

            var controller = CreateController();
            var result = await controller.Login(request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }

        [Fact]
        public async Task Login_WithInvalidCredentials_ReturnsUnauthorized()
        {
            var request = new LoginRequest
            {
                Email = "john@test.com",
                Password = "WrongPassword"
            };

            _userServiceMock.Setup(s => s.LoginAsync(request.Email, request.Password))
                .ReturnsAsync((null, null));

            var controller = CreateController();
            var result = await controller.Login(request);

            Assert.IsType<UnauthorizedObjectResult>(result);
        }

        [Fact]
        public async Task Login_WithDeactivatedAccount_ReturnsForbidden()
        {
            var request = new LoginRequest
            {
                Email = "john@test.com",
                Password = "Password@123"
            };

            _userServiceMock.Setup(s => s.LoginAsync(request.Email, request.Password))
                .ThrowsAsync(new ArgumentException("Account is deactivated or banned."));

            var controller = CreateController();
            var result = await controller.Login(request);

            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(403, statusResult.StatusCode);
        }

        #endregion

        #region RefreshToken Tests

        [Fact]
        public async Task RefreshToken_WithValidToken_ReturnsNewTokens()
        {
            var request = new RefreshTokenRequest
            {
                RefreshToken = "valid-refresh-token"
            };

            _userServiceMock.Setup(s => s.RefreshTokenAsync(request.RefreshToken))
                .ReturnsAsync(("new-access-token", "new-refresh-token"));

            var controller = CreateController();
            var result = await controller.RefreshToken(request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }

        [Fact]
        public async Task RefreshToken_WithInvalidToken_ReturnsUnauthorized()
        {
            var request = new RefreshTokenRequest
            {
                RefreshToken = "invalid-token"
            };

            _userServiceMock.Setup(s => s.RefreshTokenAsync(request.RefreshToken))
                .ReturnsAsync((null, null));

            var controller = CreateController();
            var result = await controller.RefreshToken(request);

            Assert.IsType<UnauthorizedObjectResult>(result);
        }

        #endregion

        #region Logout Tests

        [Fact]
        public async Task Logout_WithAuthenticatedUser_ReturnsOk()
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, "user-1")
            };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            var user = new ClaimsPrincipal(identity);

            _userServiceMock.Setup(s => s.RevokeRefreshTokenAsync("user-1"))
                .Returns(Task.CompletedTask);

            var controller = CreateController(user);
            var result = await controller.Logout();

            Assert.IsType<OkObjectResult>(result);
            _userServiceMock.Verify(s => s.RevokeRefreshTokenAsync("user-1"), Times.Once);
        }

        [Fact]
        public async Task Logout_WithNoUserId_ReturnsOk()
        {
            var controller = CreateController(new ClaimsPrincipal());
            var result = await controller.Logout();

            Assert.IsType<OkObjectResult>(result);
        }

        #endregion

        #region ForgotPassword Tests

        [Fact]
        public async Task ForgotPassword_WithValidEmail_ReturnsNewPassword()
        {
            var request = new ForgotPasswordRequest
            {
                Email = "john@test.com"
            };

            _userServiceMock.Setup(s => s.ForgotPasswordAsync(request.Email))
                .ReturnsAsync("new-password-123");

            var controller = CreateController();
            var result = await controller.ForgotPassword(request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }

        [Fact]
        public async Task ForgotPassword_WithNonexistentEmail_ReturnsBadRequest()
        {
            var request = new ForgotPasswordRequest
            {
                Email = "nonexistent@test.com"
            };

            _userServiceMock.Setup(s => s.ForgotPasswordAsync(request.Email))
                .ThrowsAsync(new Exception("Email not found in the system."));

            var controller = CreateController();
            var result = await controller.ForgotPassword(request);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        #endregion
    }

    public class ProjectControllerTests
    {
        private readonly Mock<IProjectService> _projectServiceMock;
        private readonly ProjectController _controller;

        public ProjectControllerTests()
        {
            _projectServiceMock = new Mock<IProjectService>();
            _controller = new ProjectController(_projectServiceMock.Object);
        }

        private void SetupUser(string userId, string role)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Role, role)
            };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            var user = new ClaimsPrincipal(identity);

            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            };
        }

        #region CreateProject Tests

        [Fact]
        public async Task CreateProject_WithValidRequest_ReturnsOk()
        {
            SetupUser("manager-1", "Manager");

            var request = new CreateProjectRequest
            {
                Name = "Test Project",
                LabelClasses = new List<CreateLabelClassRequest>
                {
                    new CreateLabelClassRequest { Name = "Cat", Color = "#FF0000" }
                }
            };

            _projectServiceMock.Setup(s => s.CreateProjectAsync("manager-1", request))
                .ReturnsAsync(new Core.DTOs.Responses.ProjectDetailResponse { Id = 1, Name = "Test Project" });

            var result = await _controller.CreateProject(request);

            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public async Task CreateProject_WithEmptyName_ReturnsBadRequest()
        {
            SetupUser("manager-1", "Manager");

            var request = new CreateProjectRequest
            {
                Name = "",
                LabelClasses = new List<CreateLabelClassRequest>
                {
                    new CreateLabelClassRequest { Name = "Cat", Color = "#FF0000" }
                }
            };

            var result = await _controller.CreateProject(request);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task CreateProject_WithoutLabels_ReturnsBadRequest()
        {
            SetupUser("manager-1", "Manager");

            var request = new CreateProjectRequest
            {
                Name = "Test Project",
                LabelClasses = new List<CreateLabelClassRequest>()
            };

            var result = await _controller.CreateProject(request);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task CreateProject_WithServiceError_ReturnsBadRequest()
        {
            SetupUser("manager-1", "Manager");

            var request = new CreateProjectRequest
            {
                Name = "Test Project",
                LabelClasses = new List<CreateLabelClassRequest>
                {
                    new CreateLabelClassRequest { Name = "Cat", Color = "#FF0000" }
                }
            };

            _projectServiceMock.Setup(s => s.CreateProjectAsync(It.IsAny<string>(), It.IsAny<CreateProjectRequest>()))
                .ThrowsAsync(new Exception("Service error"));

            var result = await _controller.CreateProject(request);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        #endregion

        #region GetProjectDetails Tests

        [Fact]
        public async Task GetProjectDetails_ProjectExists_ReturnsOk()
        {
            var project = new Core.DTOs.Responses.ProjectDetailResponse
            {
                Id = 1,
                Name = "Test Project"
            };

            _projectServiceMock.Setup(s => s.GetProjectDetailsAsync(1))
                .ReturnsAsync(project);

            var result = await _controller.GetProjectDetails(1);

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }

        [Fact]
        public async Task GetProjectDetails_ProjectNotFound_ReturnsNotFound()
        {
            _projectServiceMock.Setup(s => s.GetProjectDetailsAsync(999))
                .ReturnsAsync((Core.DTOs.Responses.ProjectDetailResponse?)null);

            var result = await _controller.GetProjectDetails(999);

            Assert.IsType<NotFoundObjectResult>(result);
        }

        #endregion

        #region UpdateProject Tests

        [Fact]
        public async Task UpdateProject_WithValidRequest_ReturnsOk()
        {
            SetupUser("manager-1", "Manager");

            var request = new UpdateProjectRequest
            {
                Name = "Updated Project"
            };

            _projectServiceMock.Setup(s => s.UpdateProjectAsync(1, request, "manager-1"))
                .Returns(Task.CompletedTask);

            var result = await _controller.UpdateProject(1, request);

            Assert.IsType<OkObjectResult>(result);
        }

        #endregion

        #region DeleteProject Tests

        [Fact]
        public async Task DeleteProject_WithValidId_ReturnsOk()
        {
            SetupUser("manager-1", "Manager");

            _projectServiceMock.Setup(s => s.DeleteProjectAsync(1))
                .Returns(Task.CompletedTask);

            var result = await _controller.DeleteProject(1);

            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public async Task DeleteProject_ProjectNotFound_ReturnsBadRequest()
        {
            SetupUser("manager-1", "Manager");

            _projectServiceMock.Setup(s => s.DeleteProjectAsync(999))
                .ThrowsAsync(new Exception("Project not found"));

            var result = await _controller.DeleteProject(999);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        #endregion

        #region CompleteProject Tests

        [Fact]
        public async Task CompleteProject_WithValidId_ReturnsOk()
        {
            SetupUser("manager-1", "Manager");

            _projectServiceMock.Setup(s => s.CompleteProjectAsync(1, "manager-1"))
                .Returns(Task.CompletedTask);

            var result = await _controller.CompleteProject(1);

            Assert.IsType<OkObjectResult>(result);
        }

        #endregion

        #region ArchiveProject Tests

        [Fact]
        public async Task ArchiveProject_WithValidId_ReturnsOk()
        {
            SetupUser("manager-1", "Manager");

            _projectServiceMock.Setup(s => s.ArchiveProjectAsync(1, "manager-1"))
                .Returns(Task.CompletedTask);

            var result = await _controller.ArchiveProject(1);

            Assert.IsType<OkObjectResult>(result);
        }

        #endregion

        #region AssignReviewers Tests

        [Fact]
        public async Task AssignReviewers_WithValidRequest_ReturnsOk()
        {
            SetupUser("manager-1", "Manager");

            var request = new AssignReviewersRequest
            {
                ProjectId = 1,
                ReviewerIds = new List<string> { "reviewer-1", "reviewer-2" }
            };

            _projectServiceMock.Setup(s => s.AssignReviewersAsync(request))
                .Returns(Task.CompletedTask);

            var result = await _controller.AssignReviewers(request);

            Assert.IsType<OkObjectResult>(result);
        }

        #endregion
    }

    public class ReviewControllerTests
    {
        private readonly Mock<IReviewService> _reviewServiceMock;
        private readonly Mock<IStatisticService> _statisticServiceMock;
        private readonly ReviewController _controller;

        public ReviewControllerTests()
        {
            _reviewServiceMock = new Mock<IReviewService>();
            _statisticServiceMock = new Mock<IStatisticService>();
            _controller = new ReviewController(_reviewServiceMock.Object, _statisticServiceMock.Object);
        }

        private void SetupUser(string userId, string role)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Role, role)
            };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            var user = new ClaimsPrincipal(identity);

            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            };
        }

        #region ReviewAssignment Tests

        [Fact]
        public async Task ReviewTask_WithValidRequest_ReturnsOk()
        {
            SetupUser("reviewer-1", "Reviewer");

            var request = new Core.DTOs.Requests.ReviewRequest
            {
                AssignmentId = 1,
                IsApproved = true,
                Comment = "Good work"
            };

            _reviewServiceMock.Setup(s => s.ReviewAssignmentAsync("reviewer-1", request))
                .Returns(Task.CompletedTask);

            var result = await _controller.ReviewTask(request);

            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public async Task ReviewTask_SelfReview_ThrowsException()
        {
            SetupUser("annotator-1", "Reviewer");

            var request = new Core.DTOs.Requests.ReviewRequest
            {
                AssignmentId = 1,
                IsApproved = true
            };

            _reviewServiceMock.Setup(s => s.ReviewAssignmentAsync("annotator-1", request))
                .ThrowsAsync(new Exception("BR-REV-10: A reviewer must not review their own annotated tasks"));

            var result = await _controller.ReviewTask(request);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        #endregion

        #region AuditReview Tests

        [Fact]
        public async Task AuditReview_WithValidRequest_ReturnsOk()
        {
            SetupUser("manager-1", "Manager");

            var request = new Core.DTOs.Requests.AuditReviewRequest
            {
                ReviewLogId = 1,
                IsCorrectDecision = true
            };

            _reviewServiceMock.Setup(s => s.AuditReviewAsync("manager-1", request))
                .Returns(Task.CompletedTask);

            var result = await _controller.AuditReview(request);

            Assert.IsType<OkObjectResult>(result);
        }

        #endregion

        #region GetTasksForReview Tests

        [Fact]
        public async Task GetTasksForReview_ReturnsOk()
        {
            SetupUser("reviewer-1", "Reviewer");

            var tasks = new List<Core.DTOs.Responses.TaskResponse>
            {
                new Core.DTOs.Responses.TaskResponse { AssignmentId = 1, Status = "Submitted" }
            };

            _reviewServiceMock.Setup(s => s.GetTasksForReviewAsync(1, "reviewer-1"))
                .ReturnsAsync(tasks);

            var result = await _controller.GetTasksForReview(1);

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }

        #endregion

        #region GetReviewerProjects Tests

        [Fact]
        public async Task GetReviewerProjects_ReturnsOk()
        {
            SetupUser("reviewer-1", "Reviewer");

            var projects = new List<Core.DTOs.Responses.AssignedProjectResponse>
            {
                new Core.DTOs.Responses.AssignedProjectResponse { ProjectId = 1, ProjectName = "Test Project" }
            };

            _reviewServiceMock.Setup(s => s.GetReviewerProjectsAsync("reviewer-1"))
                .ReturnsAsync(projects);

            var result = await _controller.GetReviewerProjects();

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }

        #endregion
    }
}
