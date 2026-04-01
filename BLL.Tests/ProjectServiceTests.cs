using BLL.Interfaces;
using BLL.Services;
using Core.Constants;
using Core.DTOs.Requests;
using Core.DTOs.Responses;
using Core.Entities;
using Core.Interfaces;
using Moq;
using System.Text;
using System.Text.Json;
using Xunit;

namespace BLL.Tests
{
    public class ProjectServiceTests
    {
        private readonly Mock<IProjectRepository> _projectRepoMock;
        private readonly Mock<IUserRepository> _userRepoMock;
        private readonly Mock<IRepository<UserProjectStat>> _statsRepoMock;
        private readonly Mock<IAssignmentRepository> _assignmentRepoMock;
        private readonly Mock<IActivityLogRepository> _activityLogRepoMock;
        private readonly Mock<IRepository<ProjectFlag>> _flagRepoMock;
        private readonly Mock<IDisputeRepository> _disputeRepoMock;
        private readonly Mock<IAppNotificationService> _notificationMock;
        private readonly Mock<IWorkflowEmailService> _workflowEmailServiceMock;

        private readonly ProjectService _projectService;

        public ProjectServiceTests()
        {
            _projectRepoMock = new Mock<IProjectRepository>();
            _userRepoMock = new Mock<IUserRepository>();
            _statsRepoMock = new Mock<IRepository<UserProjectStat>>();
            _assignmentRepoMock = new Mock<IAssignmentRepository>();
            _activityLogRepoMock = new Mock<IActivityLogRepository>();
            _flagRepoMock = new Mock<IRepository<ProjectFlag>>();
            _disputeRepoMock = new Mock<IDisputeRepository>();
            _notificationMock = new Mock<IAppNotificationService>();
            _workflowEmailServiceMock = new Mock<IWorkflowEmailService>();

            _projectService = new ProjectService(
                _projectRepoMock.Object,
                _userRepoMock.Object,
                _statsRepoMock.Object,
                _assignmentRepoMock.Object,
                _activityLogRepoMock.Object,
                _flagRepoMock.Object,
                _disputeRepoMock.Object,
                _notificationMock.Object,
                _workflowEmailServiceMock.Object
            );
        }

        #region CreateProjectAsync Tests

        [Fact]
        public async Task CreateProjectAsync_WithManager_CreatesProject()
        {
            var manager = new User
            {
                Id = "manager-1",
                FullName = "Test Manager",
                Email = "manager@test.com",
                Role = UserRoles.Manager
            };

            _userRepoMock.Setup(r => r.GetByIdAsync("manager-1")).ReturnsAsync(manager);
            _projectRepoMock.Setup(r => r.AddAsync(It.IsAny<Project>())).Returns(Task.CompletedTask);
            _projectRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _flagRepoMock.Setup(r => r.AddAsync(It.IsAny<ProjectFlag>())).Returns(Task.CompletedTask);
            _flagRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _activityLogRepoMock.Setup(r => r.AddAsync(It.IsAny<ActivityLog>())).Returns(Task.CompletedTask);
            _activityLogRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            var request = new CreateProjectRequest
            {
                Name = "Test Project",
                Description = "Test Description",
                StartDate = DateTime.UtcNow,
                Deadline = DateTime.UtcNow.AddDays(30),
                LabelClasses = new List<LabelClassRequest>
                {
                    new LabelClassRequest { Name = "Cat", Color = "#FF0000" }
                }
            };

            var result = await _projectService.CreateProjectAsync("manager-1", request);

            Assert.NotNull(result);
            Assert.Equal("Test Project", result.Name);
            Assert.Equal("Test Manager", result.ManagerName);
            _projectRepoMock.Verify(r => r.AddAsync(It.IsAny<Project>()), Times.Once);
        }

        [Fact]
        public async Task CreateProjectAsync_WithNonManager_ThrowsException()
        {
            var user = new User
            {
                Id = "user-1",
                Role = UserRoles.Annotator
            };

            _userRepoMock.Setup(r => r.GetByIdAsync("user-1")).ReturnsAsync(user);

            var request = new CreateProjectRequest
            {
                Name = "Test Project",
                LabelClasses = new List<LabelClassRequest>()
            };

            var ex = await Assert.ThrowsAsync<Exception>(() =>
                _projectService.CreateProjectAsync("user-1", request));

            Assert.Contains("BR-MNG-01", ex.Message);
        }

        [Fact]
        public async Task CreateProjectAsync_UserNotFound_ThrowsException()
        {
            _userRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<string>())).ReturnsAsync((User?)null);

            var request = new CreateProjectRequest { Name = "Test Project" };

            await Assert.ThrowsAsync<Exception>(() =>
                _projectService.CreateProjectAsync("nonexistent", request));
        }

        #endregion

        #region GetProjectDetailsAsync Tests

        [Fact]
        public async Task GetProjectDetailsAsync_ProjectExists_ReturnsDetails()
        {
            var project = new Project
            {
                Id = 1,
                Name = "Test Project",
                Description = "Description",
                ManagerId = "manager-1",
                Manager = new User { Id = "manager-1", FullName = "Manager", Email = "m@test.com" },
                Deadline = DateTime.UtcNow.AddDays(30),
                AllowGeometryTypes = "Rectangle",
                LabelClasses = new List<LabelClass>
                {
                    new LabelClass { Id = 1, Name = "Cat", Color = "#FF0000" }
                },
                DataItems = new List<DataItem>
                {
                    new DataItem
                    {
                        Id = 1,
                        Status = TaskStatusConstants.Approved,
                        Assignments = new List<Assignment>
                        {
                            new Assignment
                            {
                                Id = 1,
                                AnnotatorId = "annotator-1",
                                Status = TaskStatusConstants.Approved,
                                Annotator = new User { Id = "annotator-1", FullName = "Annotator", Email = "a@test.com", Role = UserRoles.Annotator }
                            }
                        }
                    }
                }
            };

            _projectRepoMock.Setup(r => r.GetProjectWithDetailsAsync(1)).ReturnsAsync(project);

            var result = await _projectService.GetProjectDetailsAsync(1);

            Assert.NotNull(result);
            Assert.Equal("Test Project", result.Name);
            Assert.Equal(1, result.TotalDataItems);
            Assert.Equal(0, result.UnassignedDataItemCount);
            Assert.Equal(1, result.ProcessedItems);
            Assert.Single(result.Labels);
        }

        [Fact]
        public async Task GetProjectDetailsAsync_WithMultipleReviewerCopies_CountsAnnotatorTasksByDistinctImages()
        {
            var annotator = new User
            {
                Id = "annotator-1",
                FullName = "Annotator 1",
                Email = "annotator@test.com",
                Role = UserRoles.Annotator
            };
            var reviewer1 = new User
            {
                Id = "reviewer-1",
                FullName = "Reviewer 1",
                Email = "reviewer1@test.com",
                Role = UserRoles.Reviewer
            };
            var reviewer2 = new User
            {
                Id = "reviewer-2",
                FullName = "Reviewer 2",
                Email = "reviewer2@test.com",
                Role = UserRoles.Reviewer
            };

            var project = new Project
            {
                Id = 1,
                Name = "Multi Reviewer Project",
                Description = "Description",
                ManagerId = "manager-1",
                Manager = new User { Id = "manager-1", FullName = "Manager", Email = "m@test.com" },
                Deadline = DateTime.UtcNow.AddDays(30),
                AllowGeometryTypes = "Rectangle",
                LabelClasses = new List<LabelClass>(),
                DataItems = new List<DataItem>
                {
                    new DataItem
                    {
                        Id = 101,
                        Status = TaskStatusConstants.Submitted,
                        Assignments = new List<Assignment>
                        {
                            new Assignment
                            {
                                Id = 1,
                                DataItemId = 101,
                                AnnotatorId = annotator.Id,
                                ReviewerId = reviewer1.Id,
                                Status = TaskStatusConstants.Submitted,
                                Annotator = annotator,
                                Reviewer = reviewer1
                            },
                            new Assignment
                            {
                                Id = 2,
                                DataItemId = 101,
                                AnnotatorId = annotator.Id,
                                ReviewerId = reviewer2.Id,
                                Status = TaskStatusConstants.Submitted,
                                Annotator = annotator,
                                Reviewer = reviewer2
                            }
                        }
                    },
                    new DataItem
                    {
                        Id = 102,
                        Status = TaskStatusConstants.Approved,
                        Assignments = new List<Assignment>
                        {
                            new Assignment
                            {
                                Id = 3,
                                DataItemId = 102,
                                AnnotatorId = annotator.Id,
                                ReviewerId = reviewer1.Id,
                                Status = TaskStatusConstants.Approved,
                                Annotator = annotator,
                                Reviewer = reviewer1
                            },
                            new Assignment
                            {
                                Id = 4,
                                DataItemId = 102,
                                AnnotatorId = annotator.Id,
                                ReviewerId = reviewer2.Id,
                                Status = TaskStatusConstants.Approved,
                                Annotator = annotator,
                                Reviewer = reviewer2
                            }
                        }
                    }
                }
            };

            _projectRepoMock.Setup(r => r.GetProjectWithDetailsAsync(1)).ReturnsAsync(project);

            var result = await _projectService.GetProjectDetailsAsync(1);

            Assert.NotNull(result);

            var annotatorMember = Assert.Single(result!.Members, m => m.Role == UserRoles.Annotator);
            Assert.Equal(2, annotatorMember.TasksAssigned);
            Assert.Equal(1, annotatorMember.TasksCompleted);
            Assert.Equal(50m, annotatorMember.Progress);

            var reviewerMembers = result.Members.Where(m => m.Role == UserRoles.Reviewer).OrderBy(m => m.Id).ToList();
            Assert.Equal(2, reviewerMembers.Count);
            Assert.All(reviewerMembers, reviewer =>
            {
                Assert.Equal(2, reviewer.TasksAssigned);
                Assert.Equal(1, reviewer.TasksCompleted);
                Assert.Equal(50m, reviewer.Progress);
            });
        }

        [Fact]
        public async Task GetProjectDetailsAsync_ProjectNotFound_ReturnsNull()
        {
            _projectRepoMock.Setup(r => r.GetProjectWithDetailsAsync(It.IsAny<int>())).ReturnsAsync((Project?)null);

            var result = await _projectService.GetProjectDetailsAsync(999);

            Assert.Null(result);
        }

        #endregion

        #region UpdateProjectAsync Tests

        [Fact]
        public async Task UpdateProjectAsync_AsManager_UpdatesProject()
        {
            var manager = new User { Id = "manager-1", Role = UserRoles.Manager };
            var project = new Project
            {
                Id = 1,
                Name = "Old Name",
                ManagerId = "manager-1",
                Status = ProjectStatusConstants.Draft
            };

            _projectRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(project);
            _userRepoMock.Setup(r => r.GetByIdAsync("manager-1")).ReturnsAsync(manager);
            _projectRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _activityLogRepoMock.Setup(r => r.AddAsync(It.IsAny<ActivityLog>())).Returns(Task.CompletedTask);
            _activityLogRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            var request = new UpdateProjectRequest
            {
                Name = "New Name",
                Description = "New Description"
            };

            await _projectService.UpdateProjectAsync(1, request, "manager-1");

            Assert.Equal("New Name", project.Name);
            Assert.Equal("New Description", project.Description);
        }

        [Fact]
        public async Task UpdateProjectAsync_AsAdmin_ThrowsException()
        {
            var admin = new User { Id = "admin-1", Role = UserRoles.Admin };
            var project = new Project { Id = 1, Name = "Test", ManagerId = "manager-1" };

            _projectRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(project);
            _userRepoMock.Setup(r => r.GetByIdAsync("admin-1")).ReturnsAsync(admin);

            var request = new UpdateProjectRequest { Name = "New Name" };

            var ex = await Assert.ThrowsAsync<Exception>(() =>
                _projectService.UpdateProjectAsync(1, request, "admin-1"));

            Assert.Contains("BR-ADM-18", ex.Message);
        }

        [Fact]
        public async Task UpdateProjectAsync_AsNonManager_ThrowsUnauthorized()
        {
            var otherManager = new User { Id = "other-1", Role = UserRoles.Manager };
            var project = new Project { Id = 1, Name = "Test", ManagerId = "manager-1" };

            _projectRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(project);
            _userRepoMock.Setup(r => r.GetByIdAsync("other-1")).ReturnsAsync(otherManager);

            var request = new UpdateProjectRequest { Name = "New Name" };

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                _projectService.UpdateProjectAsync(1, request, "other-1"));
        }

        #endregion

        #region DeleteProjectAsync Tests

        [Fact]
        public async Task DeleteProjectAsync_ProjectExists_DeletesProject()
        {
            var project = new Project
            {
                Id = 1,
                Name = "Test Project",
                ManagerId = "manager-1"
            };

            _projectRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(project);
            _projectRepoMock.Setup(r => r.Delete(It.IsAny<Project>())).Verifiable();
            _projectRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _activityLogRepoMock.Setup(r => r.AddAsync(It.IsAny<ActivityLog>())).Returns(Task.CompletedTask);
            _activityLogRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            await _projectService.DeleteProjectAsync(1);

            _projectRepoMock.Verify(r => r.Delete(It.IsAny<Project>()), Times.Once);
        }

        [Fact]
        public async Task DeleteProjectAsync_ProjectNotFound_ThrowsException()
        {
            _projectRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ReturnsAsync((Project?)null);

            await Assert.ThrowsAsync<Exception>(() =>
                _projectService.DeleteProjectAsync(999));
        }

        #endregion

        #region GetProjectStatisticsAsync Tests

        [Fact]
        public async Task GetProjectStatisticsAsync_ReviewerAccuracy_IncludesRejectedTaskWithoutDispute()
        {
            var reviewer = new User
            {
                Id = "reviewer-1",
                FullName = "Reviewer One",
                Email = "reviewer@test.com",
                Role = UserRoles.Reviewer
            };

            var annotator = new User
            {
                Id = "annotator-1",
                FullName = "Annotator One",
                Email = "annotator@test.com",
                Role = UserRoles.Annotator
            };

            var approvedAssignment = new Assignment
            {
                Id = 1,
                ProjectId = 10,
                DataItemId = 201,
                ReviewerId = reviewer.Id,
                Reviewer = reviewer,
                AnnotatorId = annotator.Id,
                Annotator = annotator,
                Status = TaskStatusConstants.Approved,
                ReviewLogs = new List<ReviewLog>
                {
                    new ReviewLog
                    {
                        Id = 101,
                        AssignmentId = 1,
                        ReviewerId = reviewer.Id,
                        Verdict = "Approved"
                    }
                }
            };

            var overturnedRejectedAssignment = new Assignment
            {
                Id = 2,
                ProjectId = 10,
                DataItemId = 202,
                ReviewerId = reviewer.Id,
                Reviewer = reviewer,
                AnnotatorId = annotator.Id,
                Annotator = annotator,
                Status = TaskStatusConstants.Approved,
                ReviewLogs = new List<ReviewLog>
                {
                    new ReviewLog
                    {
                        Id = 102,
                        AssignmentId = 2,
                        ReviewerId = reviewer.Id,
                        Verdict = "Rejected"
                    }
                }
            };

            var rejectedWithoutDisputeAssignment = new Assignment
            {
                Id = 3,
                ProjectId = 10,
                DataItemId = 203,
                ReviewerId = reviewer.Id,
                Reviewer = reviewer,
                AnnotatorId = annotator.Id,
                Annotator = annotator,
                Status = TaskStatusConstants.Rejected,
                ReviewLogs = new List<ReviewLog>
                {
                    new ReviewLog
                    {
                        Id = 103,
                        AssignmentId = 3,
                        ReviewerId = reviewer.Id,
                        Verdict = "Rejected"
                    }
                }
            };

            var project = new Project
            {
                Id = 10,
                Name = "Statistics Project",
                LabelClasses = new List<LabelClass>(),
                DataItems = new List<DataItem>
                {
                    new DataItem
                    {
                        Id = 201,
                        Status = TaskStatusConstants.Approved,
                        Assignments = new List<Assignment> { approvedAssignment }
                    },
                    new DataItem
                    {
                        Id = 202,
                        Status = TaskStatusConstants.Approved,
                        Assignments = new List<Assignment> { overturnedRejectedAssignment }
                    },
                    new DataItem
                    {
                        Id = 203,
                        Status = TaskStatusConstants.Rejected,
                        Assignments = new List<Assignment> { rejectedWithoutDisputeAssignment }
                    }
                }
            };

            var reviewerStat = new UserProjectStat
            {
                UserId = reviewer.Id,
                ProjectId = 10,
                TotalReviewsDone = 3,
                TotalReviewerManagerDecisions = 2,
                TotalReviewerCorrectByManager = 1
            };

            _projectRepoMock
                .Setup(r => r.GetProjectWithStatsDataAsync(10))
                .ReturnsAsync(project);
            _projectRepoMock
                .Setup(r => r.GetProjectLabelCountsAsync(10))
                .ReturnsAsync(new Dictionary<int, int>());
            _statsRepoMock
                .Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<UserProjectStat, bool>>>()))
                .ReturnsAsync(new List<UserProjectStat> { reviewerStat });
            _disputeRepoMock
                .Setup(r => r.GetDisputesByProjectAsync(10))
                .ReturnsAsync(new List<Dispute>
                {
                    new Dispute
                    {
                        Id = 301,
                        AssignmentId = 2,
                        AnnotatorId = annotator.Id,
                        Status = "Resolved"
                    }
                });

            var result = await _projectService.GetProjectStatisticsAsync(10);

            var reviewerPerformance = Assert.Single(result.ReviewerPerformances);
            Assert.Equal(3, reviewerPerformance.TotalReviews);
            Assert.Equal(2, reviewerPerformance.CorrectDecisions);
            Assert.Equal(3, reviewerPerformance.TotalManagerDecisions);
            Assert.Equal(66.67, reviewerPerformance.ReviewerAccuracy);
        }

        [Fact]
        public async Task GetProjectStatisticsAsync_ReviewerAccuracy_UsesLatestSubmissionCycleOnly()
        {
            var reviewer = new User
            {
                Id = "reviewer-1",
                FullName = "Reviewer One",
                Email = "reviewer@test.com",
                Role = UserRoles.Reviewer
            };
            var annotator = new User
            {
                Id = "annotator-1",
                FullName = "Annotator One",
                Email = "annotator@test.com",
                Role = UserRoles.Annotator
            };
            var latestSubmittedAt = new DateTime(2026, 4, 1, 9, 0, 0, DateTimeKind.Utc);

            var assignment = new Assignment
            {
                Id = 11,
                ProjectId = 11,
                DataItemId = 301,
                ReviewerId = reviewer.Id,
                Reviewer = reviewer,
                AnnotatorId = annotator.Id,
                Annotator = annotator,
                Status = TaskStatusConstants.Approved,
                SubmittedAt = latestSubmittedAt,
                RejectCount = 1,
                ReviewLogs = new List<ReviewLog>
                {
                    new ReviewLog
                    {
                        Id = 1001,
                        AssignmentId = 11,
                        ReviewerId = reviewer.Id,
                        Verdict = "Rejected",
                        CreatedAt = latestSubmittedAt.AddHours(-2)
                    },
                    new ReviewLog
                    {
                        Id = 1002,
                        AssignmentId = 11,
                        ReviewerId = reviewer.Id,
                        Verdict = "Approved",
                        CreatedAt = latestSubmittedAt.AddMinutes(5)
                    }
                }
            };

            var project = new Project
            {
                Id = 11,
                Name = "Latest Cycle Project",
                LabelClasses = new List<LabelClass>(),
                DataItems = new List<DataItem>
                {
                    new DataItem
                    {
                        Id = 301,
                        Status = TaskStatusConstants.Approved,
                        Assignments = new List<Assignment> { assignment }
                    }
                }
            };

            _projectRepoMock
                .Setup(r => r.GetProjectWithStatsDataAsync(11))
                .ReturnsAsync(project);
            _projectRepoMock
                .Setup(r => r.GetProjectLabelCountsAsync(11))
                .ReturnsAsync(new Dictionary<int, int>());
            _statsRepoMock
                .Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<UserProjectStat, bool>>>()))
                .ReturnsAsync(new List<UserProjectStat>
                {
                    new UserProjectStat
                    {
                        UserId = reviewer.Id,
                        ProjectId = 11,
                        TotalReviewsDone = 2
                    }
                });
            _disputeRepoMock
                .Setup(r => r.GetDisputesByProjectAsync(11))
                .ReturnsAsync(new List<Dispute>());

            var result = await _projectService.GetProjectStatisticsAsync(11);

            var reviewerPerformance = Assert.Single(result.ReviewerPerformances);
            Assert.Equal(2, reviewerPerformance.TotalReviews);
            Assert.Equal(1, reviewerPerformance.TotalManagerDecisions);
            Assert.Equal(1, reviewerPerformance.CorrectDecisions);
            Assert.Equal(100, reviewerPerformance.ReviewerAccuracy);

            var annotatorPerformance = Assert.Single(result.AnnotatorPerformances);
            Assert.Equal(1, annotatorPerformance.ResolvedTasks);
            Assert.Equal(100, annotatorPerformance.FinalAccuracy);
            Assert.Equal(0, annotatorPerformance.FirstPassAccuracy);
            Assert.Null(annotatorPerformance.AverageQualityScore);
        }

        [Fact]
        public async Task GetProjectStatisticsAsync_ManagerReject_DropsAnnotatorAndReviewerAccuracyToZero()
        {
            var reviewer = new User
            {
                Id = "reviewer-2",
                FullName = "Reviewer Two",
                Email = "reviewer2@test.com",
                Role = UserRoles.Reviewer
            };
            var annotator = new User
            {
                Id = "annotator-2",
                FullName = "Annotator Two",
                Email = "annotator2@test.com",
                Role = UserRoles.Annotator
            };

            var assignment = new Assignment
            {
                Id = 21,
                ProjectId = 12,
                DataItemId = 401,
                ReviewerId = reviewer.Id,
                Reviewer = reviewer,
                AnnotatorId = annotator.Id,
                Annotator = annotator,
                Status = TaskStatusConstants.Rejected,
                SubmittedAt = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc),
                RejectCount = 1,
                ManagerDecision = "reject",
                ReviewLogs = new List<ReviewLog>
                {
                    new ReviewLog
                    {
                        Id = 2001,
                        AssignmentId = 21,
                        ReviewerId = reviewer.Id,
                        Verdict = "Approved",
                        CreatedAt = new DateTime(2026, 4, 1, 10, 5, 0, DateTimeKind.Utc)
                    }
                }
            };

            var project = new Project
            {
                Id = 12,
                Name = "Manager Reject Project",
                LabelClasses = new List<LabelClass>(),
                DataItems = new List<DataItem>
                {
                    new DataItem
                    {
                        Id = 401,
                        Status = TaskStatusConstants.Rejected,
                        Assignments = new List<Assignment> { assignment }
                    }
                }
            };

            _projectRepoMock
                .Setup(r => r.GetProjectWithStatsDataAsync(12))
                .ReturnsAsync(project);
            _projectRepoMock
                .Setup(r => r.GetProjectLabelCountsAsync(12))
                .ReturnsAsync(new Dictionary<int, int>());
            _statsRepoMock
                .Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<UserProjectStat, bool>>>()))
                .ReturnsAsync(new List<UserProjectStat>
                {
                    new UserProjectStat
                    {
                        UserId = annotator.Id,
                        ProjectId = 12,
                        AverageQualityScore = 72,
                        TotalReviewedTasks = 1
                    },
                    new UserProjectStat
                    {
                        UserId = reviewer.Id,
                        ProjectId = 12,
                        TotalReviewsDone = 1
                    }
                });
            _disputeRepoMock
                .Setup(r => r.GetDisputesByProjectAsync(12))
                .ReturnsAsync(new List<Dispute>());

            var result = await _projectService.GetProjectStatisticsAsync(12);

            Assert.Equal(0, result.FinalAccuracy);
            Assert.Equal(0, result.ProjectAccuracy);
            Assert.Equal(1, result.TotalReworks);
            Assert.Equal(1, result.TotalSubmittedTasks);

            var annotatorPerformance = Assert.Single(result.AnnotatorPerformances);
            Assert.Equal(1, annotatorPerformance.ResolvedTasks);
            Assert.Equal(0, annotatorPerformance.FinalAccuracy);
            Assert.Equal(0, annotatorPerformance.AnnotatorAccuracy);
            Assert.Equal(72, annotatorPerformance.AverageQualityScore);
            Assert.Equal(100, annotatorPerformance.ReworkRate);

            var reviewerPerformance = Assert.Single(result.ReviewerPerformances);
            Assert.Equal(1, reviewerPerformance.TotalManagerDecisions);
            Assert.Equal(0, reviewerPerformance.CorrectDecisions);
            Assert.Equal(0, reviewerPerformance.ReviewerAccuracy);
        }

        #endregion

        #region CompleteProjectAsync Tests

        [Fact]
        public async Task CompleteProjectAsync_AllTasksApproved_CompletesProject()
        {
            var manager = new User
            {
                Id = "manager-1",
                FullName = "Manager One",
                Email = "manager@test.com",
                Role = UserRoles.Manager
            };
            var annotator = new User
            {
                Id = "annotator-1",
                FullName = "Annotator One",
                Email = "annotator@test.com",
                Role = UserRoles.Annotator
            };

            var project = new Project
            {
                Id = 1,
                Name = "Test Project",
                ManagerId = "manager-1",
                Status = ProjectStatusConstants.Active,
                DataItems = new List<DataItem>
                {
                    new DataItem
                    {
                        Id = 1,
                        Assignments = new List<Assignment>
                        {
                            new Assignment { Id = 1, Status = TaskStatusConstants.Approved, AnnotatorId = annotator.Id }
                        }
                    }
                }
            };

            _projectRepoMock.Setup(r => r.GetProjectWithDetailsAsync(1)).ReturnsAsync(project);
            _projectRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _activityLogRepoMock.Setup(r => r.AddAsync(It.IsAny<ActivityLog>())).Returns(Task.CompletedTask);
            _activityLogRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _userRepoMock.Setup(r => r.GetByIdAsync("manager-1")).ReturnsAsync(manager);
            _userRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<User> { manager, annotator });
            _statsRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<UserProjectStat>());

            await _projectService.CompleteProjectAsync(1, "manager-1");

            Assert.Equal(ProjectStatusConstants.Completed, project.Status);
            _workflowEmailServiceMock.Verify(
                w => w.SendProjectCompletedEmailsAsync(
                    project,
                    manager,
                    It.Is<IReadOnlyCollection<User>>(users => users.Count == 1 && users.First().Id == annotator.Id),
                    It.Is<IReadOnlyCollection<Assignment>>(assignments => assignments.Count == 1),
                    It.Is<IReadOnlyCollection<UserProjectStat>>(stats => stats.Count == 0)),
                Times.Once);
        }

        [Fact]
        public async Task CompleteProjectAsync_WhenCompletionEmailFails_StillCompletesProject()
        {
            var manager = new User
            {
                Id = "manager-1",
                FullName = "Manager One",
                Email = "manager@test.com",
                Role = UserRoles.Manager
            };
            var annotator = new User
            {
                Id = "annotator-1",
                FullName = "Annotator One",
                Email = "annotator@test.com",
                Role = UserRoles.Annotator
            };

            var project = new Project
            {
                Id = 1,
                Name = "Resilient Completion Project",
                ManagerId = "manager-1",
                Status = ProjectStatusConstants.Active,
                DataItems = new List<DataItem>
                {
                    new DataItem
                    {
                        Id = 1,
                        Assignments = new List<Assignment>
                        {
                            new Assignment { Id = 1, Status = TaskStatusConstants.Approved, AnnotatorId = annotator.Id }
                        }
                    }
                }
            };

            var activityLogs = new List<ActivityLog>();

            _projectRepoMock.Setup(r => r.GetProjectWithDetailsAsync(1)).ReturnsAsync(project);
            _projectRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _activityLogRepoMock
                .Setup(r => r.AddAsync(It.IsAny<ActivityLog>()))
                .Callback<ActivityLog>(log => activityLogs.Add(log))
                .Returns(Task.CompletedTask);
            _activityLogRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _userRepoMock.Setup(r => r.GetByIdAsync("manager-1")).ReturnsAsync(manager);
            _userRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<User> { manager, annotator });
            _statsRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<UserProjectStat>());
            _workflowEmailServiceMock
                .Setup(w => w.SendProjectCompletedEmailsAsync(
                    It.IsAny<Project>(),
                    It.IsAny<User>(),
                    It.IsAny<IReadOnlyCollection<User>>(),
                    It.IsAny<IReadOnlyCollection<Assignment>>(),
                    It.IsAny<IReadOnlyCollection<UserProjectStat>>()))
                .ThrowsAsync(new InvalidOperationException("SMTP is unavailable"));

            await _projectService.CompleteProjectAsync(1, "manager-1");

            Assert.Equal(ProjectStatusConstants.Completed, project.Status);
            Assert.Contains(activityLogs, log =>
                log.ActionType == "CompleteProjectEmailError" &&
                log.Description.Contains("SMTP is unavailable"));
        }

        [Fact]
        public async Task CompleteProjectAsync_NotAllTasksApproved_ThrowsException()
        {
            var project = new Project
            {
                Id = 1,
                Name = "Test Project",
                ManagerId = "manager-1",
                Status = ProjectStatusConstants.Active,
                DataItems = new List<DataItem>
                {
                    new DataItem
                    {
                        Id = 1,
                        Assignments = new List<Assignment>
                        {
                            new Assignment { Id = 1, Status = TaskStatusConstants.Submitted }
                        }
                    }
                }
            };

            _projectRepoMock.Setup(r => r.GetProjectWithDetailsAsync(1)).ReturnsAsync(project);

            var ex = await Assert.ThrowsAsync<Exception>(() =>
                _projectService.CompleteProjectAsync(1, "manager-1"));

            Assert.Contains("BR-MNG-33", ex.Message);
        }

        [Fact]
        public async Task CompleteProjectAsync_NotActiveProject_ThrowsException()
        {
            var project = new Project
            {
                Id = 1,
                ManagerId = "manager-1",
                Status = ProjectStatusConstants.Draft,
                DataItems = new List<DataItem>()
            };

            _projectRepoMock.Setup(r => r.GetProjectWithDetailsAsync(1)).ReturnsAsync(project);

            var ex = await Assert.ThrowsAsync<Exception>(() =>
                _projectService.CompleteProjectAsync(1, "manager-1"));

            Assert.Contains("BR-MNG-33", ex.Message);
        }

        [Fact]
        public async Task CompleteProjectAsync_NotManager_ThrowsUnauthorized()
        {
            var project = new Project
            {
                Id = 1,
                ManagerId = "manager-1",
                Status = ProjectStatusConstants.Active,
                DataItems = new List<DataItem>()
            };

            _projectRepoMock.Setup(r => r.GetProjectWithDetailsAsync(1)).ReturnsAsync(project);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                _projectService.CompleteProjectAsync(1, "other-manager"));
        }

        [Fact]
        public async Task GetProjectCompletionReviewAsync_WhenAwaitingManagerConfirmation_ReturnsApprovedItemsWithHistory()
        {
            var annotator = new User
            {
                Id = "annotator-1",
                FullName = "Annotator One",
                Email = "annotator@test.com",
                Role = UserRoles.Annotator
            };
            var reviewerOne = new User
            {
                Id = "reviewer-1",
                FullName = "Reviewer One",
                Email = "reviewer1@test.com",
                Role = UserRoles.Reviewer
            };
            var reviewerTwo = new User
            {
                Id = "reviewer-2",
                FullName = "Reviewer Two",
                Email = "reviewer2@test.com",
                Role = UserRoles.Reviewer
            };

            var submittedAt = new DateTime(2026, 4, 1, 8, 0, 0, DateTimeKind.Utc);
            var project = new Project
            {
                Id = 1,
                Name = "Completion Review Project",
                ManagerId = "manager-1",
                Status = ProjectStatusConstants.Active,
                DataItems = new List<DataItem>
                {
                    new DataItem
                    {
                        Id = 101,
                        StorageUrl = "https://cdn.example.com/image-101.png",
                        Status = TaskStatusConstants.Approved,
                        Assignments = new List<Assignment>
                        {
                            new Assignment
                            {
                                Id = 501,
                                ProjectId = 1,
                                DataItemId = 101,
                                AnnotatorId = annotator.Id,
                                Annotator = annotator,
                                ReviewerId = reviewerOne.Id,
                                Reviewer = reviewerOne,
                                Status = TaskStatusConstants.Approved,
                                RejectCount = 1,
                                SubmittedAt = submittedAt,
                                Annotations = new List<Annotation>
                                {
                                    new Annotation
                                    {
                                        AssignmentId = 501,
                                        CreatedAt = submittedAt,
                                        DataJSON = "[{\"id\":\"a1\",\"labelId\":7,\"labelName\":\"Car\",\"x\":10,\"y\":20,\"width\":30,\"height\":40}]"
                                    }
                                },
                                ReviewLogs = new List<ReviewLog>
                                {
                                    new ReviewLog
                                    {
                                        Id = 801,
                                        AssignmentId = 501,
                                        ReviewerId = reviewerOne.Id,
                                        Reviewer = reviewerOne,
                                        Verdict = "Approved",
                                        Comment = "Looks good",
                                        CreatedAt = submittedAt.AddMinutes(20)
                                    }
                                }
                            },
                            new Assignment
                            {
                                Id = 502,
                                ProjectId = 1,
                                DataItemId = 101,
                                AnnotatorId = annotator.Id,
                                Annotator = annotator,
                                ReviewerId = reviewerTwo.Id,
                                Reviewer = reviewerTwo,
                                Status = TaskStatusConstants.Approved,
                                RejectCount = 1,
                                SubmittedAt = submittedAt,
                                ReviewLogs = new List<ReviewLog>
                                {
                                    new ReviewLog
                                    {
                                        Id = 802,
                                        AssignmentId = 502,
                                        ReviewerId = reviewerTwo.Id,
                                        Reviewer = reviewerTwo,
                                        Verdict = "Rejected",
                                        Comment = "Need tighter box",
                                        CreatedAt = submittedAt.AddMinutes(25)
                                    }
                                }
                            }
                        }
                    }
                }
            };

            _projectRepoMock
                .Setup(r => r.GetProjectWithStatsDataAsync(1))
                .ReturnsAsync(project);

            var result = await _projectService.GetProjectCompletionReviewAsync(1, "manager-1");

            Assert.Equal(ProjectStatusConstants.AwaitingManagerConfirmation, result.Status);
            Assert.True(result.IsAwaitingManagerConfirmation);
            Assert.True(result.CanManagerConfirmCompletion);
            Assert.Equal(1, result.TotalDataItems);
            Assert.Equal(1, result.ApprovedItems);

            var item = Assert.Single(result.Items);
            Assert.Equal(101, item.DataItemId);
            Assert.Equal("https://cdn.example.com/image-101.png", item.DataItemUrl);
            Assert.Equal(1, item.RejectCount);
            Assert.Equal(2, item.ReviewerCount);
            Assert.Equal(2, item.ReviewerFeedbacks.Count);
            Assert.Equal(2, item.ReviewHistory.Count);
            Assert.Contains(item.ReviewerFeedbacks, feedback => feedback.ReviewerId == reviewerOne.Id);
            Assert.Contains(item.ReviewerFeedbacks, feedback => feedback.ReviewerId == reviewerTwo.Id);
        }

        [Fact]
        public async Task ReturnProjectItemForReworkAsync_WhenProjectCompleted_ReopensProjectAndRejectsAssignmentGroup()
        {
            var annotator = new User
            {
                Id = "annotator-1",
                FullName = "Annotator One",
                Email = "annotator@test.com",
                Role = UserRoles.Annotator
            };
            var reviewer = new User
            {
                Id = "reviewer-1",
                FullName = "Reviewer One",
                Email = "reviewer@test.com",
                Role = UserRoles.Reviewer
            };

            var dataItem = new DataItem
            {
                Id = 301,
                StorageUrl = "https://cdn.example.com/image-301.png",
                Status = TaskStatusConstants.Approved
            };

            var assignments = new List<Assignment>
            {
                new Assignment
                {
                    Id = 901,
                    ProjectId = 7,
                    DataItemId = dataItem.Id,
                    DataItem = dataItem,
                    AnnotatorId = annotator.Id,
                    Annotator = annotator,
                    ReviewerId = reviewer.Id,
                    Reviewer = reviewer,
                    Status = TaskStatusConstants.Approved,
                    RejectCount = 2,
                    SubmittedAt = new DateTime(2026, 4, 1, 7, 0, 0, DateTimeKind.Utc)
                },
                new Assignment
                {
                    Id = 902,
                    ProjectId = 7,
                    DataItemId = dataItem.Id,
                    DataItem = dataItem,
                    AnnotatorId = annotator.Id,
                    Annotator = annotator,
                    ReviewerId = reviewer.Id,
                    Reviewer = reviewer,
                    Status = TaskStatusConstants.Approved,
                    RejectCount = 2,
                    SubmittedAt = new DateTime(2026, 4, 1, 7, 0, 0, DateTimeKind.Utc)
                }
            };

            dataItem.Assignments = assignments;

            var project = new Project
            {
                Id = 7,
                Name = "Closed Project",
                ManagerId = "manager-1",
                Status = ProjectStatusConstants.Completed,
                DataItems = new List<DataItem> { dataItem }
            };

            _projectRepoMock
                .Setup(r => r.GetProjectWithStatsDataAsync(7))
                .ReturnsAsync(project);
            _projectRepoMock
                .Setup(r => r.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
                .Returns((Func<Task> operation, CancellationToken _) => operation());
            _projectRepoMock
                .Setup(r => r.SaveChangesAsync())
                .Returns(Task.CompletedTask);
            _assignmentRepoMock
                .Setup(r => r.SaveChangesAsync())
                .Returns(Task.CompletedTask);
            _activityLogRepoMock
                .Setup(r => r.AddAsync(It.IsAny<ActivityLog>()))
                .Returns(Task.CompletedTask);
            _activityLogRepoMock
                .Setup(r => r.SaveChangesAsync())
                .Returns(Task.CompletedTask);

            await _projectService.ReturnProjectItemForReworkAsync(
                7,
                901,
                "manager-1",
                "The final QA found a mislabeled object.");

            Assert.Equal(ProjectStatusConstants.Active, project.Status);
            Assert.Equal(TaskStatusConstants.Rejected, dataItem.Status);
            Assert.All(assignments, assignment =>
            {
                Assert.Equal(TaskStatusConstants.Rejected, assignment.Status);
                Assert.False(assignment.IsEscalated);
                Assert.Equal(3, assignment.RejectCount);
                Assert.Equal("Reject", assignment.ManagerDecision);
                Assert.Equal("The final QA found a mislabeled object.", assignment.ManagerComment);
            });
            _notificationMock.Verify(
                n => n.SendNotificationAsync(
                    annotator.Id,
                    It.Is<string>(message => message.Contains("returned image #301", StringComparison.OrdinalIgnoreCase)),
                    "Warning",
                    null,
                    null,
                    null,
                    null),
                Times.Once);
        }

        #endregion

        #region ActivateProjectAsync Tests

        [Fact]
        public async Task ActivateProjectAsync_DraftProjectWithData_ActivatesProject()
        {
            var project = new Project
            {
                Id = 1,
                Name = "Test Project",
                ManagerId = "manager-1",
                Status = ProjectStatusConstants.Draft,
                DataItems = new List<DataItem>
                {
                    new DataItem { Id = 1 }
                }
            };

            _projectRepoMock.Setup(r => r.GetProjectWithDetailsAsync(1)).ReturnsAsync(project);
            _projectRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _activityLogRepoMock.Setup(r => r.AddAsync(It.IsAny<ActivityLog>())).Returns(Task.CompletedTask);
            _activityLogRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            await _projectService.ActivateProjectAsync(1, "manager-1");

            Assert.Equal(ProjectStatusConstants.Active, project.Status);
        }

        [Fact]
        public async Task ActivateProjectAsync_NoDataItems_ThrowsException()
        {
            var project = new Project
            {
                Id = 1,
                ManagerId = "manager-1",
                Status = ProjectStatusConstants.Draft,
                DataItems = new List<DataItem>()
            };

            _projectRepoMock.Setup(r => r.GetProjectWithDetailsAsync(1)).ReturnsAsync(project);

            var ex = await Assert.ThrowsAsync<Exception>(() =>
                _projectService.ActivateProjectAsync(1, "manager-1"));

            Assert.Contains("without data items", ex.Message);
        }

        [Fact]
        public async Task ActivateProjectAsync_NotDraftProject_ThrowsException()
        {
            var project = new Project
            {
                Id = 1,
                ManagerId = "manager-1",
                Status = ProjectStatusConstants.Active,
                DataItems = new List<DataItem> { new DataItem() }
            };

            _projectRepoMock.Setup(r => r.GetProjectWithDetailsAsync(1)).ReturnsAsync(project);

            var ex = await Assert.ThrowsAsync<Exception>(() =>
                _projectService.ActivateProjectAsync(1, "manager-1"));

            Assert.Contains("Draft", ex.Message);
        }

        #endregion

        #region ArchiveProjectAsync Tests

        [Fact]
        public async Task ArchiveProjectAsync_ActiveProject_ArchivesProject()
        {
            var project = new Project
            {
                Id = 1,
                Name = "Test Project",
                ManagerId = "manager-1",
                Status = ProjectStatusConstants.Active,
                DataItems = new List<DataItem>()
            };

            _projectRepoMock.Setup(r => r.GetProjectWithDetailsAsync(1)).ReturnsAsync(project);
            _projectRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _activityLogRepoMock.Setup(r => r.AddAsync(It.IsAny<ActivityLog>())).Returns(Task.CompletedTask);
            _activityLogRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            await _projectService.ArchiveProjectAsync(1, "manager-1");

            Assert.Equal(ProjectStatusConstants.Archived, project.Status);
        }

        [Fact]
        public async Task ArchiveProjectAsync_DraftProject_ThrowsException()
        {
            var project = new Project
            {
                Id = 1,
                ManagerId = "manager-1",
                Status = ProjectStatusConstants.Draft
            };

            _projectRepoMock.Setup(r => r.GetProjectWithDetailsAsync(1)).ReturnsAsync(project);

            var ex = await Assert.ThrowsAsync<Exception>(() =>
                _projectService.ArchiveProjectAsync(1, "manager-1"));

            Assert.Contains("Draft", ex.Message);
        }

        #endregion

        #region GetProjectsByManagerAsync Tests

        [Fact]
        public async Task GetProjectsByManagerAsync_ReturnsProjects()
        {
            var projects = new List<Project>
            {
                new Project
                {
                    Id = 1,
                    Name = "Project 1",
                    Deadline = DateTime.UtcNow.AddDays(30),
                    DataItems = new List<DataItem>()
                },
                new Project
                {
                    Id = 2,
                    Name = "Project 2",
                    Deadline = DateTime.UtcNow.AddDays(15),
                    DataItems = new List<DataItem>
                    {
                        new DataItem
                        {
                            Id = 1,
                            Status = TaskStatusConstants.Approved,
                            Assignments = new List<Assignment>()
                        }
                    }
                }
            };

            _projectRepoMock.Setup(r => r.GetProjectsByManagerIdAsync("manager-1")).ReturnsAsync(projects);

            var result = await _projectService.GetProjectsByManagerAsync("manager-1");

            Assert.Equal(2, result.Count);
            Assert.Contains(result, p => p.Name == "Project 1");
            Assert.Contains(result, p => p.Name == "Project 2");
        }

        [Fact]
        public async Task GetProjectsByManagerAsync_WhenAllItemsApprovedButManagerNotConfirmed_ReturnsAwaitingManagerConfirmation()
        {
            var deadline = DateTime.UtcNow.AddDays(5);
            var projects = new List<Project>
            {
                new Project
                {
                    Id = 8,
                    Name = "Awaiting Manager Confirmation",
                    Status = ProjectStatusConstants.Active,
                    Deadline = deadline,
                    DataItems = new List<DataItem>
                    {
                        new DataItem
                        {
                            Id = 801,
                            Status = TaskStatusConstants.Approved,
                            Assignments = new List<Assignment>()
                        }
                    }
                }
            };

            _projectRepoMock.Setup(r => r.GetProjectsByManagerIdAsync("manager-1")).ReturnsAsync(projects);

            var result = await _projectService.GetProjectsByManagerAsync("manager-1");

            var managerProject = Assert.Single(result);
            Assert.Equal(ProjectStatusConstants.AwaitingManagerConfirmation, managerProject.Status);
            Assert.True(managerProject.IsAwaitingManagerConfirmation);
            Assert.True(managerProject.CanManagerConfirmCompletion);
            Assert.Equal(100m, managerProject.Progress);
        }

        #endregion

        #region GetAllProjectsForAdminAsync Tests

        [Fact]
        public async Task GetAllProjectsForAdminAsync_ReturnsAllProjects()
        {
            var projects = new List<Project>
            {
                new Project
                {
                    Id = 1,
                    Name = "Project 1",
                    Deadline = DateTime.UtcNow.AddDays(30),
                    DataItems = new List<DataItem>()
                }
            };

            _projectRepoMock.Setup(r => r.GetAllProjectsForAdminStatsAsync()).ReturnsAsync(projects);

            var result = await _projectService.GetAllProjectsForAdminAsync();

            Assert.Single(result);
            Assert.Equal("Project 1", result[0].Name);
        }

        #endregion

        #region ImportDataItemsAsync Tests

        [Fact]
        public async Task ImportDataItemsAsync_ProjectExists_ImportsItems()
        {
            var project = new Project
            {
                Id = 1,
                Name = "Test Project",
                ManagerId = "manager-1",
                DataItems = new List<DataItem>()
            };

            _projectRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(project);
            _projectRepoMock.Setup(r => r.GetProjectDataItemsCountAsync(1)).ReturnsAsync(0);
            _projectRepoMock.Setup(r => r.AddDataItemsAsync(It.IsAny<List<DataItem>>())).Returns(Task.CompletedTask);
            _projectRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _activityLogRepoMock.Setup(r => r.AddAsync(It.IsAny<ActivityLog>())).Returns(Task.CompletedTask);
            _activityLogRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            var urls = new List<string> { "url1.jpg", "url2.jpg", "url3.jpg" };

            await _projectService.ImportDataItemsAsync(1, urls);


            _projectRepoMock.Verify(r => r.AddDataItemsAsync(It.Is<List<DataItem>>(items =>
                items.Count == 3 &&
                items.All(d => d.Status == TaskStatusConstants.New) &&
                items.All(d => d.ProjectId == 1)
            )), Times.Once);
        }

        [Fact]
        public async Task ImportDataItemsAsync_AddingOneItem_ShouldReturnCountOfOne()
        {
            var project = new Project
            {
                Id = 1,
                Name = "Test Project",
                ManagerId = "manager-1",
                DataItems = new List<DataItem>()
            };

            List<DataItem>? capturedItems = null;
            _projectRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(project);
            _projectRepoMock.Setup(r => r.GetProjectDataItemsCountAsync(1)).ReturnsAsync(0);
            _projectRepoMock.Setup(r => r.AddDataItemsAsync(It.IsAny<List<DataItem>>()))
                .Callback<List<DataItem>>(items => capturedItems = items)
                .Returns(Task.CompletedTask);
            _projectRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _activityLogRepoMock.Setup(r => r.AddAsync(It.IsAny<ActivityLog>())).Returns(Task.CompletedTask);
            _activityLogRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            var urls = new List<string> { "single_image.jpg" };

            await _projectService.ImportDataItemsAsync(1, urls);

            Assert.NotNull(capturedItems);
            Assert.Single(capturedItems);
            Assert.Equal("single_image.jpg", capturedItems[0].StorageUrl);
            Assert.Equal(TaskStatusConstants.New, capturedItems[0].Status);
            Assert.Equal(1, capturedItems[0].BucketId);
        }

        [Fact]
        public async Task ImportDataItemsAsync_BucketIdCalculation_ShouldBeCorrect()
        {
            var project = new Project
            {
                Id = 1,
                Name = "Test Project",
                ManagerId = "manager-1",
                DataItems = new List<DataItem>()
            };

            List<DataItem>? capturedItems = null;
            _projectRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(project);
            _projectRepoMock.Setup(r => r.GetProjectDataItemsCountAsync(1)).ReturnsAsync(0); 
            _projectRepoMock.Setup(r => r.AddDataItemsAsync(It.IsAny<List<DataItem>>()))
                .Callback<List<DataItem>>(items => capturedItems = items)
                .Returns(Task.CompletedTask);
            _projectRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _activityLogRepoMock.Setup(r => r.AddAsync(It.IsAny<ActivityLog>())).Returns(Task.CompletedTask);
            _activityLogRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            var urls = Enumerable.Range(1, 55).Select(i => $"image_{i}.jpg").ToList();

            await _projectService.ImportDataItemsAsync(1, urls);

            Assert.NotNull(capturedItems);
            Assert.Equal(55, capturedItems.Count);

            for (int i = 0; i < 50; i++)
            {
                Assert.Equal(1, capturedItems[i].BucketId);
            }

            for (int i = 50; i < 55; i++)
            {
                Assert.Equal(2, capturedItems[i].BucketId);
            }
        }

        [Fact]
        public async Task ImportDataItemsAsync_ProjectNotFound_ThrowsException()
        {
            _projectRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ReturnsAsync((Project?)null);

            await Assert.ThrowsAsync<Exception>(() =>
                _projectService.ImportDataItemsAsync(1, new List<string> { "url.jpg" }));
        }

        #endregion

        #region GetManagerStatsAsync Tests

        [Fact]
        public async Task GetManagerStatsAsync_ReturnsCorrectStats()
        {
            var projects = new List<Project>
            {
                new Project { Id = 1, DataItems = new List<DataItem> { new DataItem(), new DataItem() } }
            };
            var managedUsers = new List<User>
            {
                new User { Id = "u1", ManagerId = "manager-1" },
                new User { Id = "u2", ManagerId = "manager-1" }
            };

            _projectRepoMock.Setup(r => r.GetProjectsByManagerIdAsync("manager-1")).ReturnsAsync(projects);
            _userRepoMock.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>()))
                .ReturnsAsync(managedUsers);

            var result = await _projectService.GetManagerStatsAsync("manager-1");

            Assert.Equal(1, result.TotalProjects);
            Assert.Equal(2, result.TotalMembers);
            Assert.Equal(2, result.TotalDataItems);
        }

        [Fact]
        public async Task RemoveUserFromProjectAsync_RemovingReviewer_NotifiesUserAndClearsReviewerAssignment()
        {
            var reviewer = new User
            {
                Id = "reviewer-1",
                FullName = "Reviewer One",
                Email = "reviewer@test.com",
                Role = UserRoles.Reviewer
            };
            var assignment = new Assignment
            {
                Id = 1,
                ReviewerId = reviewer.Id,
                Status = TaskStatusConstants.Submitted,
                DataItemId = 10
            };
            var project = new Project
            {
                Id = 1,
                Name = "Project Alpha",
                ManagerId = "manager-1",
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
            _userRepoMock.Setup(r => r.GetByIdAsync(reviewer.Id)).ReturnsAsync(reviewer);
            _assignmentRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _projectRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _activityLogRepoMock.Setup(r => r.AddAsync(It.IsAny<ActivityLog>())).Returns(Task.CompletedTask);
            _activityLogRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            await _projectService.RemoveUserFromProjectAsync(1, reviewer.Id);

            Assert.Null(assignment.ReviewerId);
            _notificationMock.Verify(n => n.SendNotificationAsync(
                reviewer.Id,
                It.Is<string>(message => message.Contains("Project Alpha") && message.Contains("removed")),
                "Warning"), Times.Once);
        }

        [Fact]
        public async Task ToggleUserLockAsync_LockReviewer_NotifiesUserAndRevokesProjectAccess()
        {
            var reviewer = new User
            {
                Id = "reviewer-1",
                FullName = "Reviewer One",
                Email = "reviewer@test.com",
                Role = UserRoles.Reviewer
            };
            var assignment = new Assignment
            {
                Id = 1,
                ReviewerId = reviewer.Id,
                Status = TaskStatusConstants.Submitted,
                DataItemId = 10
            };
            var stats = new List<UserProjectStat>
            {
                new UserProjectStat { UserId = reviewer.Id, ProjectId = 1, IsLocked = false }
            };
            var project = new Project
            {
                Id = 1,
                Name = "Project Alpha",
                ManagerId = "manager-1",
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
            _userRepoMock.Setup(r => r.GetByIdAsync(reviewer.Id)).ReturnsAsync(reviewer);
            _statsRepoMock.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<UserProjectStat, bool>>>())).ReturnsAsync(stats);
            _assignmentRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _statsRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _activityLogRepoMock.Setup(r => r.AddAsync(It.IsAny<ActivityLog>())).Returns(Task.CompletedTask);
            _activityLogRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            await _projectService.ToggleUserLockAsync(1, reviewer.Id, true, "manager-1");

            Assert.Null(assignment.ReviewerId);
            Assert.True(stats[0].IsLocked);
            _notificationMock.Verify(n => n.SendNotificationAsync(
                reviewer.Id,
                It.Is<string>(message => message.Contains("Project Alpha") && message.Contains("locked")),
                "Warning"), Times.Once);
        }

        [Fact]
        public async Task ExportProjectDataAsync_WhenProjectHasNoDataItems_ThrowsInvalidOperationException()
        {
            var project = new Project
            {
                Id = 99,
                Name = "Empty Project",
                ManagerId = "manager-1",
                DataItems = new List<DataItem>()
            };
            var manager = new User
            {
                Id = "manager-1",
                Email = "manager@test.com",
                Role = UserRoles.Manager
            };

            _projectRepoMock
                .Setup(r => r.GetProjectForExportAsync(99))
                .ReturnsAsync(project);
            _userRepoMock
                .Setup(r => r.GetByIdAsync(manager.Id))
                .ReturnsAsync(manager);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _projectService.ExportProjectDataAsync(99, manager.Id));

            Assert.Contains("No data items available", ex.Message);
        }

        [Fact]
        public async Task ExportProjectDataAsync_ReturnsOnlyEssentialApprovedPayload()
        {
            var manager = new User
            {
                Id = "manager-1",
                Email = "manager@test.com",
                Role = UserRoles.Manager
            };
            var annotator = new User
            {
                Id = "annotator-1",
                Email = "annotator@test.com",
                Role = UserRoles.Annotator
            };
            var reviewer = new User
            {
                Id = "reviewer-1",
                Email = "reviewer@test.com",
                Role = UserRoles.Reviewer
            };
            var approvedAt = new DateTime(2026, 4, 1, 8, 30, 0, DateTimeKind.Utc);
            var uploadedAt = new DateTime(2026, 3, 31, 5, 0, 0, DateTimeKind.Utc);

            var project = new Project
            {
                Id = 99,
                Name = "Approved Export Project",
                Description = "Ready for export",
                GuidelineVersion = "2.1",
                Deadline = new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc),
                ManagerId = manager.Id,
                LabelClasses = new List<LabelClass>
                {
                    new LabelClass { Id = 7, Name = "Helmet", Color = "#12ABEF" }
                },
                DataItems = new List<DataItem>
                {
                    new DataItem
                    {
                        Id = 201,
                        BucketId = 4,
                        StorageUrl = "https://cdn.example.com/uploads/project-99/image-201.png",
                        UploadedDate = uploadedAt,
                        Status = TaskStatusConstants.Approved,
                        Assignments = new List<Assignment>
                        {
                            new Assignment
                            {
                                Id = 501,
                                ProjectId = 99,
                                DataItemId = 201,
                                AnnotatorId = annotator.Id,
                                ReviewerId = reviewer.Id,
                                Status = TaskStatusConstants.Approved,
                                SubmittedAt = approvedAt,
                                Annotator = annotator,
                                Reviewer = reviewer,
                                Annotations = new List<Annotation>
                                {
                                    new Annotation
                                    {
                                        AssignmentId = 501,
                                        CreatedAt = approvedAt,
                                        DataJSON = "[{\"label\":\"Helmet\",\"x\":10,\"y\":20}]"
                                    }
                                }
                            }
                        }
                    }
                }
            };

            _projectRepoMock
                .Setup(r => r.GetProjectForExportAsync(99))
                .ReturnsAsync(project);
            _userRepoMock
                .Setup(r => r.GetByIdAsync(manager.Id))
                .ReturnsAsync(manager);
            _activityLogRepoMock
                .Setup(r => r.AddAsync(It.IsAny<ActivityLog>()))
                .Returns(Task.CompletedTask);
            _activityLogRepoMock
                .Setup(r => r.SaveChangesAsync())
                .Returns(Task.CompletedTask);

            var result = await _projectService.ExportProjectDataAsync(99, manager.Id);
            var json = Encoding.UTF8.GetString(result);
            using var document = JsonDocument.Parse(json);

            Assert.True(document.RootElement.TryGetProperty("Project", out var projectElement));
            Assert.True(document.RootElement.TryGetProperty("Items", out var itemsElement));
            Assert.False(document.RootElement.TryGetProperty("Data", out _));
            Assert.False(document.RootElement.TryGetProperty("Members", out _));

            Assert.Equal(99, projectElement.GetProperty("Id").GetInt32());
            Assert.Equal("Approved Export Project", projectElement.GetProperty("Name").GetString());
            Assert.Equal(1, projectElement.GetProperty("ExportedItems").GetInt32());

            var items = itemsElement.EnumerateArray().ToList();
            var item = Assert.Single(items);
            Assert.Equal(201, item.GetProperty("DataItemId").GetInt32());
            Assert.Equal("image-201.png", item.GetProperty("FileName").GetString());
            Assert.Equal("annotator@test.com", item.GetProperty("AnnotatorEmail").GetString());
            Assert.Equal("reviewer@test.com", item.GetProperty("ReviewerEmail").GetString());
            Assert.Equal(1, item.GetProperty("AnnotationCount").GetInt32());
            Assert.Equal(JsonValueKind.Array, item.GetProperty("AnnotationData").ValueKind);
            Assert.False(item.TryGetProperty("Assignments", out _));
        }

        [Fact]
        public async Task ExportProjectDataAsync_WhenAnnotationPayloadContainsAnnotationsArray_ReturnsStructuredDataAndCorrectCount()
        {
            var manager = new User
            {
                Id = "manager-2",
                Email = "manager2@test.com",
                Role = UserRoles.Manager
            };
            var annotator = new User
            {
                Id = "annotator-2",
                Email = "annotator2@test.com",
                Role = UserRoles.Annotator
            };

            var project = new Project
            {
                Id = 100,
                Name = "Structured Export Project",
                Description = "Structured annotation payload",
                GuidelineVersion = "3.0",
                Deadline = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
                ManagerId = manager.Id,
                DataItems = new List<DataItem>
                {
                    new DataItem
                    {
                        Id = 401,
                        BucketId = 8,
                        StorageUrl = "https://cdn.example.com/uploads/project-100/image-401.png",
                        UploadedDate = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                        Status = TaskStatusConstants.Approved,
                        Assignments = new List<Assignment>
                        {
                            new Assignment
                            {
                                Id = 701,
                                ProjectId = 100,
                                DataItemId = 401,
                                AnnotatorId = annotator.Id,
                                Status = TaskStatusConstants.Approved,
                                SubmittedAt = new DateTime(2026, 4, 1, 11, 0, 0, DateTimeKind.Utc),
                                Annotator = annotator,
                                Annotations = new List<Annotation>
                                {
                                    new Annotation
                                    {
                                        AssignmentId = 701,
                                        CreatedAt = new DateTime(2026, 4, 1, 11, 0, 0, DateTimeKind.Utc),
                                        DataJSON = "{\"annotations\":[{\"label\":\"Helmet\"},{\"label\":\"Vest\"}],\"__checklist\":{\"7\":[true,true]},\"__defaultFlags\":[]}"
                                    }
                                }
                            }
                        }
                    }
                }
            };

            _projectRepoMock
                .Setup(r => r.GetProjectForExportAsync(100))
                .ReturnsAsync(project);
            _userRepoMock
                .Setup(r => r.GetByIdAsync(manager.Id))
                .ReturnsAsync(manager);
            _activityLogRepoMock
                .Setup(r => r.AddAsync(It.IsAny<ActivityLog>()))
                .Returns(Task.CompletedTask);
            _activityLogRepoMock
                .Setup(r => r.SaveChangesAsync())
                .Returns(Task.CompletedTask);

            var result = await _projectService.ExportProjectDataAsync(100, manager.Id);
            var json = Encoding.UTF8.GetString(result);
            using var document = JsonDocument.Parse(json);

            Assert.True(document.RootElement.TryGetProperty("Project", out _));
            var item = Assert.Single(document.RootElement.GetProperty("Items").EnumerateArray().ToList());
            Assert.Equal(2, item.GetProperty("AnnotationCount").GetInt32());
            Assert.Equal(JsonValueKind.Object, item.GetProperty("AnnotationData").ValueKind);
            Assert.Equal(
                2,
                item.GetProperty("AnnotationData").GetProperty("annotations").GetArrayLength());
            Assert.True(item.GetProperty("AnnotationData").TryGetProperty("__checklist", out _));
            Assert.True(item.GetProperty("AnnotationData").TryGetProperty("__defaultFlags", out _));
        }

        [Fact]
        public async Task ExportProjectCsvAsync_ReturnsApprovedRowsWithEssentialColumns()
        {
            var manager = new User
            {
                Id = "manager-1",
                Email = "manager@test.com",
                Role = UserRoles.Manager
            };
            var annotator = new User
            {
                Id = "annotator-1",
                Email = "annotator@test.com",
                Role = UserRoles.Annotator
            };

            var project = new Project
            {
                Id = 99,
                Name = "CSV Export Project",
                ManagerId = manager.Id,
                DataItems = new List<DataItem>
                {
                    new DataItem
                    {
                        Id = 301,
                        BucketId = 2,
                        StorageUrl = "/uploads/99/csv-image.png",
                        UploadedDate = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                        Status = TaskStatusConstants.Approved,
                        Assignments = new List<Assignment>
                        {
                            new Assignment
                            {
                                Id = 601,
                                ProjectId = 99,
                                DataItemId = 301,
                                AnnotatorId = annotator.Id,
                                Status = TaskStatusConstants.Approved,
                                SubmittedAt = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc),
                                Annotator = annotator,
                                Annotations = new List<Annotation>
                                {
                                    new Annotation
                                    {
                                        AssignmentId = 601,
                                        CreatedAt = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc),
                                        DataJSON = "[{\"label\":\"Vest\"}]"
                                    }
                                }
                            }
                        }
                    }
                }
            };

            _projectRepoMock
                .Setup(r => r.GetProjectForExportAsync(99))
                .ReturnsAsync(project);
            _userRepoMock
                .Setup(r => r.GetByIdAsync(manager.Id))
                .ReturnsAsync(manager);
            _activityLogRepoMock
                .Setup(r => r.AddAsync(It.IsAny<ActivityLog>()))
                .Returns(Task.CompletedTask);
            _activityLogRepoMock
                .Setup(r => r.SaveChangesAsync())
                .Returns(Task.CompletedTask);

            var result = await _projectService.ExportProjectCsvAsync(99, manager.Id);
            var csv = Encoding.UTF8.GetString(result);

            Assert.Contains("ProjectId,ProjectName,DataItemId,FileName,StorageUrl,BucketId,UploadedAt,AnnotatorEmail,ReviewerEmail,ApprovedAt,AnnotationCount,AnnotationData", csv);
            Assert.Contains("\"CSV Export Project\"", csv);
            Assert.Contains("\"csv-image.png\"", csv);
            Assert.Contains("\"annotator@test.com\"", csv);
            Assert.DoesNotContain("AssignmentId", csv);
        }

        [Fact]
        public async Task AssignReviewersAsync_EnsuresEveryReviewerForEachAnnotatorAndDataItemGroup()
        {
            var reviewerOne = new User { Id = "reviewer-1", Role = UserRoles.Reviewer };
            var reviewerTwo = new User { Id = "reviewer-2", Role = UserRoles.Reviewer };

            var project = new Project
            {
                Id = 99,
                Name = "Reviewer Matrix",
                ManagerId = "manager-1",
                DataItems = new List<DataItem>
                {
                    new DataItem
                    {
                        Id = 10,
                        Assignments = new List<Assignment>
                        {
                            new Assignment
                            {
                                Id = 1,
                                ProjectId = 99,
                                DataItemId = 10,
                                AnnotatorId = "annotator-1",
                                ReviewerId = null,
                                Status = TaskStatusConstants.Submitted,
                                SubmittedAt = DateTime.UtcNow,
                                Annotations = new List<Annotation>
                                {
                                    new Annotation { AssignmentId = 1, DataJSON = "[{\"x\":10}]", CreatedAt = DateTime.UtcNow, ClassId = 1 }
                                }
                            }
                        }
                    },
                    new DataItem
                    {
                        Id = 20,
                        Assignments = new List<Assignment>
                        {
                            new Assignment
                            {
                                Id = 2,
                                ProjectId = 99,
                                DataItemId = 20,
                                AnnotatorId = "annotator-2",
                                ReviewerId = null,
                                Status = TaskStatusConstants.Submitted,
                                SubmittedAt = DateTime.UtcNow,
                                Annotations = new List<Annotation>
                                {
                                    new Annotation { AssignmentId = 2, DataJSON = "[{\"x\":20}]", CreatedAt = DateTime.UtcNow, ClassId = 2 }
                                }
                            }
                        }
                    }
                }
            };

            var addedAssignments = new List<Assignment>();
            var updatedAssignments = new List<Assignment>();

            _projectRepoMock
                .Setup(r => r.GetProjectForExportAsync(99))
                .ReturnsAsync(project);
            _userRepoMock
                .Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>()))
                .ReturnsAsync(new List<User> { reviewerOne, reviewerTwo });
            _assignmentRepoMock
                .Setup(r => r.AddAsync(It.IsAny<Assignment>()))
                .Callback<Assignment>(assignment => addedAssignments.Add(assignment))
                .Returns(Task.CompletedTask);
            _assignmentRepoMock
                .Setup(r => r.Update(It.IsAny<Assignment>()))
                .Callback<Assignment>(assignment => updatedAssignments.Add(assignment));
            _assignmentRepoMock
                .Setup(r => r.SaveChangesAsync())
                .Returns(Task.CompletedTask);
            _activityLogRepoMock
                .Setup(r => r.AddAsync(It.IsAny<ActivityLog>()))
                .Returns(Task.CompletedTask);
            _activityLogRepoMock
                .Setup(r => r.SaveChangesAsync())
                .Returns(Task.CompletedTask);

            await _projectService.AssignReviewersAsync(new AssignReviewersRequest
            {
                ProjectId = 99,
                ReviewerIds = new List<string> { reviewerOne.Id, reviewerTwo.Id }
            });

            Assert.Equal(2, updatedAssignments.Count);
            Assert.Equal(2, addedAssignments.Count);
            Assert.All(updatedAssignments, a => Assert.Equal(reviewerOne.Id, a.ReviewerId));
            Assert.All(addedAssignments, a =>
            {
                Assert.Equal(reviewerTwo.Id, a.ReviewerId);
                Assert.Equal(TaskStatusConstants.Submitted, a.Status);
                Assert.Single(a.Annotations);
            });
        }

        [Fact]
        public async Task AssignReviewersAsync_WhenNotificationFails_StillAssignsReviewerAndSendsEmail()
        {
            var reviewer = new User
            {
                Id = "reviewer-1",
                FullName = "Reviewer One",
                Email = "reviewer@test.com",
                Role = UserRoles.Reviewer
            };
            var manager = new User
            {
                Id = "manager-1",
                FullName = "Manager One",
                Email = "manager@test.com",
                Role = UserRoles.Manager
            };

            var project = new Project
            {
                Id = 99,
                Name = "Reviewer Resilience",
                ManagerId = manager.Id,
                DataItems = new List<DataItem>
                {
                    new DataItem
                    {
                        Id = 10,
                        Assignments = new List<Assignment>
                        {
                            new Assignment
                            {
                                Id = 1,
                                ProjectId = 99,
                                DataItemId = 10,
                                AnnotatorId = "annotator-1",
                                ReviewerId = null,
                                Status = TaskStatusConstants.Submitted,
                                SubmittedAt = DateTime.UtcNow,
                                Annotations = new List<Annotation>
                                {
                                    new Annotation { AssignmentId = 1, DataJSON = "[{\"x\":10}]", CreatedAt = DateTime.UtcNow, ClassId = 1 }
                                }
                            }
                        }
                    }
                }
            };

            var updatedAssignments = new List<Assignment>();
            var activityLogs = new List<ActivityLog>();

            _projectRepoMock
                .Setup(r => r.GetProjectForExportAsync(99))
                .ReturnsAsync(project);
            _userRepoMock
                .Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>()))
                .ReturnsAsync(new List<User> { reviewer });
            _userRepoMock
                .Setup(r => r.GetByIdAsync(manager.Id))
                .ReturnsAsync(manager);
            _assignmentRepoMock
                .Setup(r => r.Update(It.IsAny<Assignment>()))
                .Callback<Assignment>(assignment => updatedAssignments.Add(assignment));
            _assignmentRepoMock
                .Setup(r => r.SaveChangesAsync())
                .Returns(Task.CompletedTask);
            _activityLogRepoMock
                .Setup(r => r.AddAsync(It.IsAny<ActivityLog>()))
                .Callback<ActivityLog>(log => activityLogs.Add(log))
                .Returns(Task.CompletedTask);
            _activityLogRepoMock
                .Setup(r => r.SaveChangesAsync())
                .Returns(Task.CompletedTask);
            _notificationMock
                .Setup(n => n.SendNotificationAsync(reviewer.Id, It.IsAny<string>(), "Info"))
                .ThrowsAsync(new InvalidOperationException("Notification hub is unavailable"));

            await _projectService.AssignReviewersAsync(new AssignReviewersRequest
            {
                ProjectId = 99,
                ReviewerIds = new List<string> { reviewer.Id }
            });

            var updatedAssignment = Assert.Single(updatedAssignments);
            Assert.Equal(reviewer.Id, updatedAssignment.ReviewerId);
            _workflowEmailServiceMock.Verify(
                w => w.SendReviewerAssignmentEmailAsync(project, manager, reviewer, 1, 1),
                Times.Once);
            Assert.Contains(activityLogs, log =>
                log.ActionType == "AssignReviewersNotificationError" &&
                log.Description.Contains(reviewer.Id));
        }

        #endregion
    }
}

