using BLL.Interfaces;
using BLL.Services;
using Core.Constants;
using Core.DTOs.Requests;
using Core.DTOs.Responses;
using Core.Entities;
using DAL.Interfaces;
using Moq;
using System.Linq.Expressions;
using Xunit;

namespace BLL.Tests
{
    public class ReviewServiceTests
    {
        private readonly Mock<IAssignmentRepository> _assignmentRepoMock;
        private readonly Mock<IRepository<ReviewLog>> _reviewLogRepoMock;
        private readonly Mock<IRepository<DataItem>> _dataItemRepoMock;
        private readonly Mock<IStatisticService> _statisticServiceMock;
        private readonly Mock<IProjectRepository> _projectRepoMock;
        private readonly Mock<IUserRepository> _userRepoMock;
        private readonly Mock<IActivityLogService> _logServiceMock;
        private readonly Mock<IAppNotificationService> _notificationMock;
        private readonly Mock<IRepository<UserProjectStat>> _statsRepoMock;

        private readonly ReviewService _reviewService;

        public ReviewServiceTests()
        {
            _assignmentRepoMock = new Mock<IAssignmentRepository>();
            _reviewLogRepoMock = new Mock<IRepository<ReviewLog>>();
            _dataItemRepoMock = new Mock<IRepository<DataItem>>();
            _statisticServiceMock = new Mock<IStatisticService>();
            _projectRepoMock = new Mock<IProjectRepository>();
            _userRepoMock = new Mock<IUserRepository>();
            _logServiceMock = new Mock<IActivityLogService>();
            _notificationMock = new Mock<IAppNotificationService>();
            _statsRepoMock = new Mock<IRepository<UserProjectStat>>();

            _reviewService = new ReviewService(
                _assignmentRepoMock.Object,
                _reviewLogRepoMock.Object,
                _dataItemRepoMock.Object,
                _statisticServiceMock.Object,
                _projectRepoMock.Object,
                _userRepoMock.Object,
                _notificationMock.Object,
                _logServiceMock.Object,
                _statsRepoMock.Object
            );
        }

        #region ReviewAssignmentAsync Tests

        [Fact]
        public async Task ReviewAssignmentAsync_ApproveTask_UpdatesAssignmentAndDataItem()
        {
            var assignment = new Assignment
            {
                Id = 1,
                AnnotatorId = "annotator-1",
                ProjectId = 1,
                Status = TaskStatusConstants.Submitted,
                DataItemId = 1,
                ReviewLogs = new List<ReviewLog>()
            };
            var reviewer = new User { Id = "reviewer-1", Role = UserRoles.Reviewer };
            var project = new Project { Id = 1, ManagerId = "manager-1", PenaltyUnit = 10 };

            _assignmentRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(assignment);
            _userRepoMock.Setup(r => r.GetByIdAsync("reviewer-1")).ReturnsAsync(reviewer);
            _reviewLogRepoMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<ReviewLog, bool>>>())).ReturnsAsync(new List<ReviewLog>());
            _projectRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(project);
            _dataItemRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new DataItem { Id = 1 });
            _reviewLogRepoMock.Setup(r => r.AddAsync(It.IsAny<ReviewLog>())).Returns(Task.CompletedTask);
            _assignmentRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            var request = new ReviewRequest
            {
                AssignmentId = 1,
                IsApproved = true,
                Comment = "Good work"
            };

            await _reviewService.ReviewAssignmentAsync("reviewer-1", request);

            Assert.Equal(TaskStatusConstants.Approved, assignment.Status);
            _statisticServiceMock.Verify(s => s.TrackReviewResultAsync("annotator-1", "reviewer-1", 1, true, 100, false), Times.Once);
            _notificationMock.Verify(n => n.SendNotificationAsync("annotator-1", It.IsAny<string>(), "Success"), Times.Once);
        }

        [Fact]
        public async Task ReviewAssignmentAsync_RejectTask_UpdatesAssignmentWithPenalty()
        {
            var assignment = new Assignment
            {
                Id = 1,
                AnnotatorId = "annotator-1",
                ProjectId = 1,
                Status = TaskStatusConstants.Submitted,
                ReviewLogs = new List<ReviewLog>()
            };
            var reviewer = new User { Id = "reviewer-1", Role = UserRoles.Reviewer };
            var project = new Project
            {
                Id = 1,
                ManagerId = "manager-1",
                PenaltyUnit = 10,
                ChecklistItems = new List<ReviewChecklistItem>
                {
                    new ReviewChecklistItem { Code = "E1", Weight = 3, IsCritical = true }
                }
            };

            _assignmentRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(assignment);
            _userRepoMock.Setup(r => r.GetByIdAsync("reviewer-1")).ReturnsAsync(reviewer);
            _reviewLogRepoMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<ReviewLog, bool>>>())).ReturnsAsync(new List<ReviewLog>());
            _projectRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(project);
            _reviewLogRepoMock.Setup(r => r.AddAsync(It.IsAny<ReviewLog>())).Returns(Task.CompletedTask);
            _assignmentRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            var request = new ReviewRequest
            {
                AssignmentId = 1,
                IsApproved = false,
                Comment = "Missing required elements",
                ErrorCategory = "E1"
            };

            await _reviewService.ReviewAssignmentAsync("reviewer-1", request);

            Assert.Equal(TaskStatusConstants.Rejected, assignment.Status);
            _statisticServiceMock.Verify(s => s.TrackReviewResultAsync("annotator-1", "reviewer-1", 1, false, 70, true), Times.Once);
            _notificationMock.Verify(n => n.SendNotificationAsync("annotator-1", It.IsAny<string>(), "Error"), Times.Once);
        }

        [Fact]
        public async Task ReviewAssignmentAsync_ReviewerReviewsOwnTask_ThrowsException()
        {
            var assignment = new Assignment
            {
                Id = 1,
                AnnotatorId = "annotator-1",
                Status = TaskStatusConstants.Submitted
            };

            _assignmentRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(assignment);

            var request = new ReviewRequest { AssignmentId = 1, IsApproved = true };

            var ex = await Assert.ThrowsAsync<Exception>(() =>
                _reviewService.ReviewAssignmentAsync("annotator-1", request));

            Assert.Contains("BR-REV-10", ex.Message);
        }

        [Fact]
        public async Task ReviewAssignmentAsync_TaskNotSubmitted_ThrowsException()
        {
            var assignment = new Assignment
            {
                Id = 1,
                AnnotatorId = "annotator-1",
                Status = TaskStatusConstants.InProgress
            };

            _assignmentRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(assignment);

            var request = new ReviewRequest { AssignmentId = 1, IsApproved = true };

            await Assert.ThrowsAsync<Exception>(() =>
                _reviewService.ReviewAssignmentAsync("reviewer-1", request));
        }

        [Fact]
        public async Task ReviewAssignmentAsync_RejectWithoutComment_ThrowsException()
        {
            var assignment = new Assignment
            {
                Id = 1,
                AnnotatorId = "annotator-1",
                Status = TaskStatusConstants.Submitted
            };
            var reviewer = new User { Id = "reviewer-1", Role = UserRoles.Reviewer };

            _assignmentRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(assignment);
            _userRepoMock.Setup(r => r.GetByIdAsync("reviewer-1")).ReturnsAsync(reviewer);

            var request = new ReviewRequest
            {
                AssignmentId = 1,
                IsApproved = false,
                Comment = ""
            };

            var ex = await Assert.ThrowsAsync<Exception>(() =>
                _reviewService.ReviewAssignmentAsync("reviewer-1", request));

            Assert.Contains("clear comment", ex.Message);
        }

        [Fact]
        public async Task ReviewAssignmentAsync_DuplicateReview_ThrowsException()
        {
            var assignment = new Assignment
            {
                Id = 1,
                AnnotatorId = "annotator-1",
                Status = TaskStatusConstants.Submitted,
                ReviewerId = "reviewer-1"
            };
            var reviewer = new User { Id = "reviewer-1", Role = UserRoles.Reviewer };
            var project = new Project { Id = 1, PenaltyUnit = 10 };

            _assignmentRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(assignment);
            _userRepoMock.Setup(r => r.GetByIdAsync("reviewer-1")).ReturnsAsync(reviewer);
            _reviewLogRepoMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<ReviewLog, bool>>>())).ReturnsAsync(
                new List<ReviewLog> { new ReviewLog { ReviewerId = "reviewer-1" } });
            _projectRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(project);

            var request = new ReviewRequest { AssignmentId = 1, IsApproved = true };

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _reviewService.ReviewAssignmentAsync("reviewer-1", request));
        }

        #endregion

        #region AuditReviewAsync Tests

        [Fact]
        public async Task AuditReviewAsync_ValidAudit_UpdatesReviewLog()
        {
            var reviewLog = new ReviewLog
            {
                Id = 1,
                AssignmentId = 1,
                ReviewerId = "reviewer-1",
                IsAudited = false
            };
            var assignment = new Assignment
            {
                Id = 1,
                ProjectId = 1
            };

            _reviewLogRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(reviewLog);
            _assignmentRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(assignment);
            _reviewLogRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            var request = new AuditReviewRequest
            {
                ReviewLogId = 1,
                IsCorrectDecision = true
            };

            await _reviewService.AuditReviewAsync("manager-1", request);

            Assert.True(reviewLog.IsAudited);
            Assert.Equal("Agree", reviewLog.AuditResult);
            _statisticServiceMock.Verify(s => s.TrackAuditResultAsync("reviewer-1", 1, true), Times.Once);
        }

        [Fact]
        public async Task AuditReviewAsync_AlreadyAudited_ThrowsException()
        {
            var reviewLog = new ReviewLog
            {
                Id = 1,
                IsAudited = true
            };

            _reviewLogRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(reviewLog);

            var request = new AuditReviewRequest { ReviewLogId = 1, IsCorrectDecision = true };

            await Assert.ThrowsAsync<Exception>(() =>
                _reviewService.AuditReviewAsync("manager-1", request));
        }

        [Fact]
        public async Task AuditReviewAsync_LogNotFound_ThrowsException()
        {
            _reviewLogRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ReturnsAsync((ReviewLog?)null);

            var request = new AuditReviewRequest { ReviewLogId = 999, IsCorrectDecision = true };

            await Assert.ThrowsAsync<Exception>(() =>
                _reviewService.AuditReviewAsync("manager-1", request));
        }

        #endregion

        #region GetReviewerProjectsAsync Tests

        [Fact]
        public async Task GetReviewerProjectsAsync_ReturnsAssignedProjects()
        {
            var assignments = new List<Assignment>
            {
                new Assignment
                {
                    Id = 1,
                    ProjectId = 1,
                    ReviewerId = "reviewer-1",
                    AssignedDate = DateTime.UtcNow.AddDays(-5),
                    Status = TaskStatusConstants.Submitted,
                    DataItem = new DataItem { ProjectId = 1 }
                },
                new Assignment
                {
                    Id = 2,
                    ProjectId = 1,
                    ReviewerId = "reviewer-1",
                    AssignedDate = DateTime.UtcNow.AddDays(-5),
                    Status = TaskStatusConstants.Approved,
                    DataItem = new DataItem { ProjectId = 1 }
                }
            };
            var project = new Project
            {
                Id = 1,
                Name = "Test Project",
                Deadline = DateTime.UtcNow.AddDays(30),
                DataItems = new List<DataItem>()
            };

            _assignmentRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(assignments);
            _projectRepoMock.Setup(r => r.GetProjectWithDetailsAsync(1)).ReturnsAsync(project);

            var result = await _reviewService.GetReviewerProjectsAsync("reviewer-1");

            Assert.Single(result);
            Assert.Equal("Test Project", result[0].ProjectName);
            Assert.Equal(2, result[0].TotalImages);
        }

        #endregion

        #region HandleEscalatedTaskAsync Tests

        [Fact]
        public async Task HandleEscalatedTaskAsync_ApproveAction_ApprovesAssignment()
        {
            var assignment = new Assignment
            {
                Id = 1,
                AnnotatorId = "annotator-1",
                ProjectId = 1,
                Status = TaskStatusConstants.Rejected,
                IsEscalated = true,
                Project = new Project { Id = 1, ManagerId = "manager-1" }
            };

            _assignmentRepoMock.Setup(r => r.GetAssignmentWithDetailsAsync(1)).ReturnsAsync(assignment);
            _dataItemRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ReturnsAsync(new DataItem { Id = 1 });
            _assignmentRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _dataItemRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            var request = new EscalationActionRequest
            {
                AssignmentId = 1,
                Action = "approve"
            };

            await _reviewService.HandleEscalatedTaskAsync("manager-1", request);

            Assert.Equal(TaskStatusConstants.Approved, assignment.Status);
            Assert.False(assignment.IsEscalated);
            _notificationMock.Verify(n => n.SendNotificationAsync("annotator-1", It.IsAny<string>(), "Success"), Times.Once);
        }

        [Fact]
        public async Task HandleEscalatedTaskAsync_RejectAction_RejectsAssignment()
        {
            var assignment = new Assignment
            {
                Id = 1,
                AnnotatorId = "annotator-1",
                ProjectId = 1,
                Status = TaskStatusConstants.Rejected,
                IsEscalated = true,
                Project = new Project { Id = 1, ManagerId = "manager-1" }
            };

            _assignmentRepoMock.Setup(r => r.GetAssignmentWithDetailsAsync(1)).ReturnsAsync(assignment);
            _assignmentRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _dataItemRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            var request = new EscalationActionRequest
            {
                AssignmentId = 1,
                Action = "reject",
                Comment = "Task quality below standards"
            };

            await _reviewService.HandleEscalatedTaskAsync("manager-1", request);

            Assert.False(assignment.IsEscalated);
            Assert.Equal(0, assignment.RejectCount);
            _notificationMock.Verify(n => n.SendNotificationAsync("annotator-1", It.IsAny<string>(), "Error"), Times.Once);
        }

        [Fact]
        public async Task HandleEscalatedTaskAsync_ReassignAction_ReassignsToNewAnnotator()
        {
            var assignment = new Assignment
            {
                Id = 1,
                AnnotatorId = "old-annotator",
                ProjectId = 1,
                Status = TaskStatusConstants.Rejected,
                IsEscalated = true,
                Project = new Project { Id = 1, ManagerId = "manager-1" }
            };
            var newAnnotator = new User { Id = "new-annotator", Role = UserRoles.Annotator };

            _assignmentRepoMock.Setup(r => r.GetAssignmentWithDetailsAsync(1)).ReturnsAsync(assignment);
            _userRepoMock.Setup(r => r.GetByIdAsync("new-annotator")).ReturnsAsync(newAnnotator);
            _assignmentRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _dataItemRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            var request = new EscalationActionRequest
            {
                AssignmentId = 1,
                Action = "reassign",
                NewAnnotatorId = "new-annotator"
            };

            await _reviewService.HandleEscalatedTaskAsync("manager-1", request);

            Assert.Equal("new-annotator", assignment.AnnotatorId);
            Assert.Equal(TaskStatusConstants.Assigned, assignment.Status);
            Assert.False(assignment.IsEscalated);
            _notificationMock.Verify(n => n.SendNotificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Exactly(2));
        }

        [Fact]
        public async Task HandleEscalatedTaskAsync_LockAction_LocksUser()
        {
            var assignment = new Assignment
            {
                Id = 1,
                AnnotatorId = "annotator-1",
                ProjectId = 1,
                Status = TaskStatusConstants.Rejected,
                IsEscalated = true,
                Project = new Project { Id = 1, ManagerId = "manager-1" }
            };
            var stats = new List<UserProjectStat>
            {
                new UserProjectStat { UserId = "annotator-1", ProjectId = 1 }
            };

            _assignmentRepoMock.Setup(r => r.GetAssignmentWithDetailsAsync(1)).ReturnsAsync(assignment);
            _statsRepoMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<UserProjectStat, bool>>>())).ReturnsAsync(stats);
            _assignmentRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _dataItemRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            var request = new EscalationActionRequest
            {
                AssignmentId = 1,
                Action = "lock"
            };

            await _reviewService.HandleEscalatedTaskAsync("manager-1", request);

            Assert.False(assignment.IsEscalated);
            Assert.True(stats[0].IsLocked);
        }

        [Fact]
        public async Task HandleEscalatedTaskAsync_NotEscalated_ThrowsException()
        {
            var assignment = new Assignment
            {
                Id = 1,
                IsEscalated = false,
                Project = new Project { Id = 1, ManagerId = "manager-1" }
            };

            _assignmentRepoMock.Setup(r => r.GetAssignmentWithDetailsAsync(1)).ReturnsAsync(assignment);

            var request = new EscalationActionRequest { AssignmentId = 1, Action = "approve" };

            await Assert.ThrowsAsync<Exception>(() =>
                _reviewService.HandleEscalatedTaskAsync("manager-1", request));
        }

        [Fact]
        public async Task HandleEscalatedTaskAsync_WrongManager_ThrowsUnauthorized()
        {
            var assignment = new Assignment
            {
                Id = 1,
                IsEscalated = true,
                Project = new Project { Id = 1, ManagerId = "manager-1" }
            };

            _assignmentRepoMock.Setup(r => r.GetAssignmentWithDetailsAsync(1)).ReturnsAsync(assignment);

            var request = new EscalationActionRequest { AssignmentId = 1, Action = "approve" };

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                _reviewService.HandleEscalatedTaskAsync("other-manager", request));
        }

        #endregion
    }
}
