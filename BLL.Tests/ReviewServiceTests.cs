using BLL.Interfaces;
using BLL.Services;
using Core.Constants;
using Core.DTOs.Requests;
using Core.DTOs.Responses;
using Core.Entities;
using Core.Interfaces;
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

            _dataItemRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
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

        [Fact]
        public async Task ReviewAssignmentAsync_AfterResubmission_AllowsReviewerToReviewAgain()
        {
            var submittedAt = DateTime.UtcNow;
            var oldReview = new ReviewLog
            {
                ReviewerId = "reviewer-1",
                Verdict = "Rejected",
                CreatedAt = submittedAt.AddMinutes(-10)
            };

            var assignment = new Assignment
            {
                Id = 1,
                AnnotatorId = "annotator-1",
                ProjectId = 1,
                Status = TaskStatusConstants.Submitted,
                ReviewerId = "reviewer-1",
                SubmittedAt = submittedAt,
                DataItemId = 1,
                ReviewLogs = new List<ReviewLog>()
            };
            var reviewer = new User { Id = "reviewer-1", Role = UserRoles.Reviewer };
            var project = new Project { Id = 1, ManagerId = "manager-1", PenaltyUnit = 10 };

            _assignmentRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(assignment);
            _userRepoMock.Setup(r => r.GetByIdAsync("reviewer-1")).ReturnsAsync(reviewer);
            _reviewLogRepoMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<ReviewLog, bool>>>()))
                .ReturnsAsync(new List<ReviewLog> { oldReview });
            _reviewLogRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<ReviewLog> { oldReview });
            _projectRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(project);
            _dataItemRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new DataItem { Id = 1 });
            _reviewLogRepoMock.Setup(r => r.AddAsync(It.IsAny<ReviewLog>())).Returns(Task.CompletedTask);
            _assignmentRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _projectRepoMock.Setup(r => r.GetProjectWithDetailsAsync(1)).ReturnsAsync((Project?)null);
            _assignmentRepoMock.Setup(r => r.GetAssignmentWithDetailsAsync(1)).ReturnsAsync(new Assignment
            {
                Id = 1,
                AnnotatorId = "annotator-1",
                ProjectId = 1,
                DataItemId = 1,
                ReviewLogs = new List<ReviewLog>
                {
                    oldReview,
                    new ReviewLog
                    {
                        ReviewerId = "reviewer-1",
                        Verdict = "Approved",
                        CreatedAt = submittedAt.AddMinutes(1)
                    }
                }
            });
            _assignmentRepoMock.Setup(r => r.GetRelatedAssignmentsForDisputeAsync(1, "annotator-1", 1))
                .ReturnsAsync(new List<Assignment>());

            await _reviewService.ReviewAssignmentAsync("reviewer-1", new ReviewRequest
            {
                AssignmentId = 1,
                IsApproved = true,
                Comment = "Looks good after resubmission"
            });

            Assert.Equal(TaskStatusConstants.Approved, assignment.Status);
            _reviewLogRepoMock.Verify(r => r.AddAsync(It.IsAny<ReviewLog>()), Times.Once);
        }

        [Fact]
        public async Task ReviewAssignmentAsync_WhenLastApprovalCompletesProject_NotifiesManagerReadyToComplete()
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
            var project = new Project { Id = 1, Name = "Ready Project", ManagerId = "manager-1", PenaltyUnit = 10 };
            var completedProject = new Project
            {
                Id = 1,
                Name = "Ready Project",
                ManagerId = "manager-1",
                Status = ProjectStatusConstants.Active,
                DataItems = new List<DataItem>
                {
                    new DataItem
                    {
                        Id = 1,
                        Assignments = new List<Assignment>
                        {
                            new Assignment { Id = 1, Status = TaskStatusConstants.Approved }
                        }
                    }
                }
            };

            _assignmentRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(assignment);
            _userRepoMock.Setup(r => r.GetByIdAsync("reviewer-1")).ReturnsAsync(reviewer);
            _reviewLogRepoMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<ReviewLog, bool>>>())).ReturnsAsync(new List<ReviewLog>());
            _reviewLogRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<ReviewLog>());
            _projectRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(project);
            _projectRepoMock.Setup(r => r.GetProjectWithDetailsAsync(1)).ReturnsAsync(completedProject);
            _dataItemRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new DataItem { Id = 1 });
            _reviewLogRepoMock.Setup(r => r.AddAsync(It.IsAny<ReviewLog>())).Returns(Task.CompletedTask);
            _assignmentRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _assignmentRepoMock.Setup(r => r.GetAssignmentWithDetailsAsync(1)).ReturnsAsync(new Assignment
            {
                Id = 1,
                AnnotatorId = "annotator-1",
                ProjectId = 1,
                DataItemId = 1,
                ReviewLogs = new List<ReviewLog>
                {
                    new ReviewLog { ReviewerId = "reviewer-1", Verdict = "Approved", CreatedAt = DateTime.UtcNow }
                }
            });
            _assignmentRepoMock.Setup(r => r.GetRelatedAssignmentsForDisputeAsync(1, "annotator-1", 1)).ReturnsAsync(new List<Assignment>());

            await _reviewService.ReviewAssignmentAsync("reviewer-1", new ReviewRequest
            {
                AssignmentId = 1,
                IsApproved = true,
                Comment = "Looks good"
            });

            _notificationMock.Verify(n => n.SendNotificationAsync(
                "manager-1",
                It.Is<string>(message => message.Contains("Ready Project") && message.Contains("ready")),
                "ProjectReadyToComplete"), Times.Once);
        }

        [Fact]
        public async Task ReviewAssignmentAsync_WhenReviewerVotesTie_NotifiesManagerAndReviewersAboutPenaltyReview()
        {
            var assignment = new Assignment
            {
                Id = 1,
                AnnotatorId = "annotator-1",
                ProjectId = 1,
                Status = TaskStatusConstants.Submitted,
                DataItemId = 9,
                ReviewLogs = new List<ReviewLog>()
            };
            var reviewer = new User { Id = "reviewer-1", Role = UserRoles.Reviewer };
            var annotator = new User { Id = "annotator-1", FullName = "Annotator One", Role = UserRoles.Annotator };
            var project = new Project { Id = 1, Name = "Penalty Project", ManagerId = "manager-1", PenaltyUnit = 10 };
            var currentDetailedAssignment = new Assignment
            {
                Id = 1,
                AnnotatorId = "annotator-1",
                ProjectId = 1,
                DataItemId = 9,
                ReviewLogs = new List<ReviewLog>
                {
                    new ReviewLog { ReviewerId = "reviewer-1", Verdict = "Rejected", CreatedAt = DateTime.UtcNow }
                }
            };
            var relatedAssignments = new List<Assignment>
            {
                new Assignment
                {
                    Id = 2,
                    AnnotatorId = "annotator-1",
                    ProjectId = 1,
                    DataItemId = 9,
                    ReviewLogs = new List<ReviewLog>
                    {
                        new ReviewLog { ReviewerId = "reviewer-2", Verdict = "Approved", CreatedAt = DateTime.UtcNow }
                    }
                }
            };

            _assignmentRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(assignment);
            _userRepoMock.Setup(r => r.GetByIdAsync("reviewer-1")).ReturnsAsync(reviewer);
            _userRepoMock.Setup(r => r.GetByIdAsync("annotator-1")).ReturnsAsync(annotator);
            _reviewLogRepoMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<ReviewLog, bool>>>())).ReturnsAsync(new List<ReviewLog>());
            _projectRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(project);
            _projectRepoMock.Setup(r => r.GetProjectWithDetailsAsync(1)).ReturnsAsync((Project?)null);
            _reviewLogRepoMock.Setup(r => r.AddAsync(It.IsAny<ReviewLog>())).Returns(Task.CompletedTask);
            _assignmentRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _assignmentRepoMock.Setup(r => r.GetAssignmentWithDetailsAsync(1)).ReturnsAsync(currentDetailedAssignment);
            _assignmentRepoMock.Setup(r => r.GetRelatedAssignmentsForDisputeAsync(1, "annotator-1", 9)).ReturnsAsync(relatedAssignments);

            await _reviewService.ReviewAssignmentAsync("reviewer-1", new ReviewRequest
            {
                AssignmentId = 1,
                IsApproved = false,
                Comment = "Missing label"
            });

            Assert.Equal("Escalated", currentDetailedAssignment.Status);
            Assert.All(relatedAssignments, related => Assert.Equal("Escalated", related.Status));
            _notificationMock.Verify(n => n.SendNotificationAsync(
                "manager-1",
                It.Is<string>(message => message.Contains("Penalty Project") && message.Contains("tied reviewer result")),
                "PenaltyReview"), Times.Once);
            _notificationMock.Verify(n => n.SendNotificationAsync(
                "reviewer-1",
                It.Is<string>(message => message.Contains("Penalty Project") && message.Contains("tied")),
                "PenaltyReview"), Times.Once);
            _notificationMock.Verify(n => n.SendNotificationAsync(
                "reviewer-2",
                It.Is<string>(message => message.Contains("Penalty Project") && message.Contains("tied")),
                "PenaltyReview"), Times.Once);
        }

        [Fact]
        public async Task ReviewAssignmentAsync_WhenConsensusApproves_FinalizesAllReviewerCopiesAsApproved()
        {
            var assignment = new Assignment
            {
                Id = 21,
                AnnotatorId = "annotator-1",
                ProjectId = 1,
                Status = TaskStatusConstants.Submitted,
                DataItemId = 88,
                ReviewLogs = new List<ReviewLog>()
            };
            var reviewer = new User { Id = "reviewer-4", Role = UserRoles.Reviewer };
            var project = new Project { Id = 1, Name = "Consensus Project", ManagerId = "manager-1", PenaltyUnit = 10 };
            var currentDetailedAssignment = new Assignment
            {
                Id = 21,
                AnnotatorId = "annotator-1",
                ProjectId = 1,
                DataItemId = 88,
                Status = TaskStatusConstants.Approved,
                ReviewLogs = new List<ReviewLog>
                {
                    new ReviewLog { ReviewerId = "reviewer-4", Verdict = "Approved", CreatedAt = DateTime.UtcNow }
                }
            };
            var relatedAssignments = new List<Assignment>
            {
                new Assignment
                {
                    Id = 22,
                    AnnotatorId = "annotator-1",
                    ProjectId = 1,
                    DataItemId = 88,
                    Status = TaskStatusConstants.Approved,
                    ReviewLogs = new List<ReviewLog>
                    {
                        new ReviewLog { ReviewerId = "reviewer-1", Verdict = "Approved", CreatedAt = DateTime.UtcNow.AddMinutes(-3) }
                    }
                },
                new Assignment
                {
                    Id = 23,
                    AnnotatorId = "annotator-1",
                    ProjectId = 1,
                    DataItemId = 88,
                    Status = TaskStatusConstants.Approved,
                    ReviewLogs = new List<ReviewLog>
                    {
                        new ReviewLog { ReviewerId = "reviewer-2", Verdict = "Approved", CreatedAt = DateTime.UtcNow.AddMinutes(-2) }
                    }
                },
                new Assignment
                {
                    Id = 24,
                    AnnotatorId = "annotator-1",
                    ProjectId = 1,
                    DataItemId = 88,
                    Status = TaskStatusConstants.Rejected,
                    ReviewLogs = new List<ReviewLog>
                    {
                        new ReviewLog { ReviewerId = "reviewer-3", Verdict = "Rejected", CreatedAt = DateTime.UtcNow.AddMinutes(-1) }
                    }
                }
            };

            _assignmentRepoMock.Setup(r => r.GetByIdAsync(21)).ReturnsAsync(assignment);
            _userRepoMock.Setup(r => r.GetByIdAsync("reviewer-4")).ReturnsAsync(reviewer);
            _reviewLogRepoMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<ReviewLog, bool>>>())).ReturnsAsync(new List<ReviewLog>());
            _projectRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(project);
            _projectRepoMock.Setup(r => r.GetProjectWithDetailsAsync(1)).ReturnsAsync((Project?)null);
            _reviewLogRepoMock.Setup(r => r.AddAsync(It.IsAny<ReviewLog>())).Returns(Task.CompletedTask);
            _assignmentRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _assignmentRepoMock.Setup(r => r.GetAssignmentWithDetailsAsync(21)).ReturnsAsync(currentDetailedAssignment);
            _assignmentRepoMock.Setup(r => r.GetRelatedAssignmentsForDisputeAsync(21, "annotator-1", 88)).ReturnsAsync(relatedAssignments);
            _dataItemRepoMock.Setup(r => r.GetByIdAsync(88)).ReturnsAsync(new DataItem { Id = 88, Status = TaskStatusConstants.Submitted });

            await _reviewService.ReviewAssignmentAsync("reviewer-4", new ReviewRequest
            {
                AssignmentId = 21,
                IsApproved = true,
                Comment = "Looks correct"
            });

            Assert.Equal(TaskStatusConstants.Approved, currentDetailedAssignment.Status);
            Assert.All(relatedAssignments, related => Assert.Equal(TaskStatusConstants.Approved, related.Status));
            _notificationMock.Verify(n => n.SendNotificationAsync(
                "annotator-1",
                It.Is<string>(message => message.Contains("reviewer consensus") && message.Contains("3 approve / 1 reject")),
                "Success"), Times.Once);
            _notificationMock.Verify(n => n.SendNotificationAsync(
                "manager-1",
                It.IsAny<string>(),
                "PenaltyReview"), Times.Never);
        }

        [Fact]
        public async Task ReviewAssignmentAsync_WhenConsensusRejects_FinalizesAllReviewerCopiesAsRejected()
        {
            var assignment = new Assignment
            {
                Id = 31,
                AnnotatorId = "annotator-1",
                ProjectId = 1,
                Status = TaskStatusConstants.Submitted,
                DataItemId = 99,
                ReviewLogs = new List<ReviewLog>()
            };
            var reviewer = new User { Id = "reviewer-4", Role = UserRoles.Reviewer };
            var project = new Project { Id = 1, Name = "Reject Consensus", ManagerId = "manager-1", PenaltyUnit = 10 };
            var currentDetailedAssignment = new Assignment
            {
                Id = 31,
                AnnotatorId = "annotator-1",
                ProjectId = 1,
                DataItemId = 99,
                Status = TaskStatusConstants.Rejected,
                ReviewLogs = new List<ReviewLog>
                {
                    new ReviewLog { ReviewerId = "reviewer-4", Verdict = "Rejected", Comment = "Missing object", CreatedAt = DateTime.UtcNow }
                }
            };
            var relatedAssignments = new List<Assignment>
            {
                new Assignment
                {
                    Id = 32,
                    AnnotatorId = "annotator-1",
                    ProjectId = 1,
                    DataItemId = 99,
                    Status = TaskStatusConstants.Rejected,
                    ReviewLogs = new List<ReviewLog>
                    {
                        new ReviewLog { ReviewerId = "reviewer-1", Verdict = "Rejected", Comment = "Wrong class", CreatedAt = DateTime.UtcNow.AddMinutes(-3) }
                    }
                },
                new Assignment
                {
                    Id = 33,
                    AnnotatorId = "annotator-1",
                    ProjectId = 1,
                    DataItemId = 99,
                    Status = TaskStatusConstants.Rejected,
                    ReviewLogs = new List<ReviewLog>
                    {
                        new ReviewLog { ReviewerId = "reviewer-2", Verdict = "Rejected", Comment = "Wrong box", CreatedAt = DateTime.UtcNow.AddMinutes(-2) }
                    }
                },
                new Assignment
                {
                    Id = 34,
                    AnnotatorId = "annotator-1",
                    ProjectId = 1,
                    DataItemId = 99,
                    Status = TaskStatusConstants.Approved,
                    ReviewLogs = new List<ReviewLog>
                    {
                        new ReviewLog { ReviewerId = "reviewer-3", Verdict = "Approved", CreatedAt = DateTime.UtcNow.AddMinutes(-1) }
                    }
                }
            };

            _assignmentRepoMock.Setup(r => r.GetByIdAsync(31)).ReturnsAsync(assignment);
            _userRepoMock.Setup(r => r.GetByIdAsync("reviewer-4")).ReturnsAsync(reviewer);
            _reviewLogRepoMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<ReviewLog, bool>>>())).ReturnsAsync(new List<ReviewLog>());
            _projectRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(project);
            _projectRepoMock.Setup(r => r.GetProjectWithDetailsAsync(1)).ReturnsAsync((Project?)null);
            _reviewLogRepoMock.Setup(r => r.AddAsync(It.IsAny<ReviewLog>())).Returns(Task.CompletedTask);
            _assignmentRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _assignmentRepoMock.Setup(r => r.GetAssignmentWithDetailsAsync(31)).ReturnsAsync(currentDetailedAssignment);
            _assignmentRepoMock.Setup(r => r.GetRelatedAssignmentsForDisputeAsync(31, "annotator-1", 99)).ReturnsAsync(relatedAssignments);

            await _reviewService.ReviewAssignmentAsync("reviewer-4", new ReviewRequest
            {
                AssignmentId = 31,
                IsApproved = false,
                Comment = "Missing object"
            });

            Assert.Equal(TaskStatusConstants.Rejected, currentDetailedAssignment.Status);
            Assert.Equal(1, currentDetailedAssignment.RejectCount);
            Assert.All(relatedAssignments, related =>
            {
                Assert.Equal(TaskStatusConstants.Rejected, related.Status);
                Assert.Equal(1, related.RejectCount);
            });
            _notificationMock.Verify(n => n.SendNotificationAsync(
                "annotator-1",
                It.Is<string>(message => message.Contains("reviewer consensus") && message.Contains("1 approve / 3 reject")),
                "Error"), Times.Once);
        }

        [Fact]
        public async Task ReviewAssignmentAsync_WhenRejectCountReachesThree_EscalatesAndNotifiesAnnotator()
        {
            var assignment = new Assignment
            {
                Id = 41,
                AnnotatorId = "annotator-1",
                ProjectId = 1,
                Status = TaskStatusConstants.Submitted,
                DataItemId = 100,
                RejectCount = 2,
                ReviewLogs = new List<ReviewLog>()
            };
            var reviewer = new User { Id = "reviewer-1", Role = UserRoles.Reviewer };
            var project = new Project { Id = 1, Name = "Escalation Project", ManagerId = "manager-1", PenaltyUnit = 10 };
            var currentDetailedAssignment = new Assignment
            {
                Id = 41,
                AnnotatorId = "annotator-1",
                ProjectId = 1,
                DataItemId = 100,
                Status = TaskStatusConstants.Rejected,
                RejectCount = 2,
                ReviewLogs = new List<ReviewLog>
                {
                    new ReviewLog { ReviewerId = "reviewer-1", Verdict = "Rejected", Comment = "Still wrong", CreatedAt = DateTime.UtcNow }
                }
            };

            _assignmentRepoMock.Setup(r => r.GetByIdAsync(41)).ReturnsAsync(assignment);
            _userRepoMock.Setup(r => r.GetByIdAsync("reviewer-1")).ReturnsAsync(reviewer);
            _reviewLogRepoMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<ReviewLog, bool>>>())).ReturnsAsync(new List<ReviewLog>());
            _projectRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(project);
            _projectRepoMock.Setup(r => r.GetProjectWithDetailsAsync(1)).ReturnsAsync((Project?)null);
            _reviewLogRepoMock.Setup(r => r.AddAsync(It.IsAny<ReviewLog>())).Returns(Task.CompletedTask);
            _assignmentRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _assignmentRepoMock.Setup(r => r.GetAssignmentWithDetailsAsync(41)).ReturnsAsync(currentDetailedAssignment);
            _assignmentRepoMock.Setup(r => r.GetRelatedAssignmentsForDisputeAsync(41, "annotator-1", 100)).ReturnsAsync(new List<Assignment>());

            await _reviewService.ReviewAssignmentAsync("reviewer-1", new ReviewRequest
            {
                AssignmentId = 41,
                IsApproved = false,
                Comment = "Still wrong"
            });

            Assert.Equal("Escalated", currentDetailedAssignment.Status);
            Assert.True(currentDetailedAssignment.IsEscalated);
            Assert.Equal(3, currentDetailedAssignment.RejectCount);
            _notificationMock.Verify(n => n.SendNotificationAsync(
                "manager-1",
                It.Is<string>(message => message.Contains("escalated after 3 rejected review cycles")),
                "Urgent"), Times.Once);
            _notificationMock.Verify(n => n.SendNotificationAsync(
                "annotator-1",
                It.Is<string>(message => message.Contains("rejected 3 times") && message.Contains("manager review")),
                "Warning"), Times.Once);
        }

        [Fact]
        public async Task ReviewAssignmentAsync_IgnoresPreviousSubmissionVotesWhenCheckingTie()
        {
            var now = DateTime.UtcNow;
            var assignment = new Assignment
            {
                Id = 11,
                AnnotatorId = "annotator-1",
                ProjectId = 1,
                Status = TaskStatusConstants.Submitted,
                DataItemId = 9,
                SubmittedAt = now,
                ReviewLogs = new List<ReviewLog>()
            };
            var reviewer = new User { Id = "reviewer-1", Role = UserRoles.Reviewer };
            var project = new Project { Id = 1, Name = "Cycle Project", ManagerId = "manager-1", PenaltyUnit = 10 };
            var currentDetailedAssignment = new Assignment
            {
                Id = 11,
                AnnotatorId = "annotator-1",
                ProjectId = 1,
                DataItemId = 9,
                SubmittedAt = now,
                ReviewLogs = new List<ReviewLog>
                {
                    new ReviewLog { ReviewerId = "reviewer-1", Verdict = "Rejected", CreatedAt = now.AddMinutes(1) }
                }
            };
            var relatedAssignments = new List<Assignment>
            {
                new Assignment
                {
                    Id = 12,
                    AnnotatorId = "annotator-1",
                    ProjectId = 1,
                    DataItemId = 9,
                    SubmittedAt = now,
                    ReviewLogs = new List<ReviewLog>
                    {
                        new ReviewLog { ReviewerId = "reviewer-2", Verdict = "Approved", CreatedAt = now.AddMinutes(-20) }
                    }
                }
            };

            _assignmentRepoMock.Setup(r => r.GetByIdAsync(11)).ReturnsAsync(assignment);
            _userRepoMock.Setup(r => r.GetByIdAsync("reviewer-1")).ReturnsAsync(reviewer);
            _reviewLogRepoMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<ReviewLog, bool>>>())).ReturnsAsync(new List<ReviewLog>());
            _projectRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(project);
            _projectRepoMock.Setup(r => r.GetProjectWithDetailsAsync(1)).ReturnsAsync((Project?)null);
            _reviewLogRepoMock.Setup(r => r.AddAsync(It.IsAny<ReviewLog>())).Returns(Task.CompletedTask);
            _assignmentRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _assignmentRepoMock.Setup(r => r.GetAssignmentWithDetailsAsync(11)).ReturnsAsync(currentDetailedAssignment);
            _assignmentRepoMock.Setup(r => r.GetRelatedAssignmentsForDisputeAsync(11, "annotator-1", 9)).ReturnsAsync(relatedAssignments);

            await _reviewService.ReviewAssignmentAsync("reviewer-1", new ReviewRequest
            {
                AssignmentId = 11,
                IsApproved = false,
                Comment = "Current cycle reject"
            });

            _notificationMock.Verify(n => n.SendNotificationAsync(
                "manager-1",
                It.Is<string>(message => message.Contains("tied reviewer result")),
                "PenaltyReview"), Times.Never);
            _notificationMock.Verify(n => n.SendNotificationAsync(
                "reviewer-2",
                It.Is<string>(message => message.Contains("tied")),
                "PenaltyReview"), Times.Never);
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
            _projectRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new Project { Id = 1, Name = "Audit Project" });

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
        public async Task AuditReviewAsync_WhenManagerDisagrees_NotifiesReviewerEvaluationFailed()
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
            _projectRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new Project { Id = 1, Name = "Audit Project" });

            await _reviewService.AuditReviewAsync("manager-1", new AuditReviewRequest
            {
                ReviewLogId = 1,
                IsCorrectDecision = false
            });

            _notificationMock.Verify(n => n.SendNotificationAsync(
                "reviewer-1",
                It.Is<string>(message => message.Contains("Audit Project") && message.Contains("failed")),
                "Warning"), Times.Once);
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

        #region Batch Completion Tests

        [Fact]
        public async Task GetBatchCompletionStatusAsync_IgnoresReviewLogsFromPreviousSubmissionCycle()
        {
            var submittedAt = DateTime.UtcNow;
            var assignment = new Assignment
            {
                Id = 1,
                AnnotatorId = "annotator-1",
                ReviewerId = "reviewer-1",
                Status = TaskStatusConstants.Submitted,
                SubmittedAt = submittedAt,
                Annotator = new User { Id = "annotator-1", FullName = "Annotator One" },
                ReviewLogs = new List<ReviewLog>
                {
                    new ReviewLog
                    {
                        ReviewerId = "reviewer-1",
                        Verdict = "Rejected",
                        CreatedAt = submittedAt.AddMinutes(-15)
                    }
                }
            };

            var project = new Project
            {
                Id = 1,
                Name = "Review Project",
                DataItems = new List<DataItem>
                {
                    new DataItem
                    {
                        Id = 10,
                        Assignments = new List<Assignment> { assignment }
                    }
                }
            };

            _projectRepoMock.Setup(r => r.GetProjectWithDetailsAsync(1)).ReturnsAsync(project);
            _statsRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<UserProjectStat>());

            var result = await _reviewService.GetBatchCompletionStatusAsync(1, "reviewer-1");

            var batch = Assert.Single(result.AnnotatorBatches);
            Assert.Equal("annotator-1", batch.AnnotatorId);
            Assert.Equal(0, batch.Approved);
            Assert.Equal(0, batch.Rejected);
            Assert.Equal(1, batch.PendingReview);
            Assert.False(batch.IsComplete);
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

