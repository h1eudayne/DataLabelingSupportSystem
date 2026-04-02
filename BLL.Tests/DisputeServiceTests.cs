using BLL.Interfaces;
using BLL.Services;
using Core.Constants;
using Core.DTOs.Requests;
using Core.DTOs.Responses;
using Core.Entities;
using Core.Interfaces;
using Moq;
using Xunit;

namespace BLL.Tests
{
    public class DisputeServiceTests
    {
        private readonly Mock<IDisputeRepository> _disputeRepoMock;
        private readonly Mock<IAssignmentRepository> _assignmentRepoMock;
        private readonly Mock<IStatisticService> _statisticServiceMock;
        private readonly Mock<IRepository<ReviewLog>> _reviewLogRepoMock;
        private readonly Mock<IProjectRepository> _projectRepoMock;
        private readonly Mock<IRepository<DataItem>> _dataItemRepoMock;
        private readonly Mock<IAppNotificationService> _notificationMock;
        private readonly Mock<IActivityLogService> _logServiceMock;
        private readonly Mock<IUserRepository> _userRepoMock;
        private readonly Mock<IWorkflowEmailService> _workflowEmailServiceMock;

        private readonly DisputeService _disputeService;

        public DisputeServiceTests()
        {
            _disputeRepoMock = new Mock<IDisputeRepository>();
            _assignmentRepoMock = new Mock<IAssignmentRepository>();
            _statisticServiceMock = new Mock<IStatisticService>();
            _reviewLogRepoMock = new Mock<IRepository<ReviewLog>>();
            _projectRepoMock = new Mock<IProjectRepository>();
            _dataItemRepoMock = new Mock<IRepository<DataItem>>();
            _notificationMock = new Mock<IAppNotificationService>();
            _logServiceMock = new Mock<IActivityLogService>();
            _userRepoMock = new Mock<IUserRepository>();
            _workflowEmailServiceMock = new Mock<IWorkflowEmailService>();

            _disputeService = new DisputeService(
                _disputeRepoMock.Object,
                _assignmentRepoMock.Object,
                _statisticServiceMock.Object,
                _reviewLogRepoMock.Object,
                _projectRepoMock.Object,
                _notificationMock.Object,
                _dataItemRepoMock.Object,
                _logServiceMock.Object,
                _userRepoMock.Object,
                _workflowEmailServiceMock.Object
            );

            _assignmentRepoMock
                .Setup(r => r.GetRelatedAssignmentsForDisputeAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(new List<Assignment>());
            _disputeRepoMock
                .Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Dispute, bool>>>()))
                .ReturnsAsync(new List<Dispute>());
        }

        #region CreateDisputeAsync Tests

        [Fact]
        public async Task CreateDisputeAsync_WithValidData_CreatesDispute()
        {
            string annotatorId = "annotator-1";
            int assignmentId = 1;

            var assignment = new Assignment
            {
                Id = assignmentId,
                AnnotatorId = annotatorId,
                Status = TaskStatusConstants.Rejected,
                Annotator = new User { Id = annotatorId, FullName = "Test Annotator" },
                ReviewLogs = new List<ReviewLog>
                {
                    new ReviewLog { ReviewerId = "reviewer-1", Verdict = "Rejected", CreatedAt = DateTime.UtcNow }
                }
            };

            _assignmentRepoMock.Setup(r => r.GetAssignmentWithDetailsAsync(assignmentId))
                .ReturnsAsync(assignment);

            _disputeRepoMock.Setup(r => r.AddAsync(It.IsAny<Dispute>()))
                .Returns(Task.CompletedTask);

            var request = new CreateDisputeRequest
            {
                AssignmentId = assignmentId,
                Reason = "Test dispute reason"
            };

            var result = await _disputeService.CreateDisputeAsync(annotatorId, request);

            Assert.NotNull(result);
            Assert.Equal(assignmentId, result.AssignmentId);
            Assert.Equal("Pending", result.Status);
            Assert.Equal("Test dispute reason", result.Reason);
            _disputeRepoMock.Verify(r => r.AddAsync(It.IsAny<Dispute>()), Times.Once);
        }

        [Fact]
        public async Task CreateDisputeAsync_AssignmentNotFound_ThrowsException()
        {
            _assignmentRepoMock.Setup(r => r.GetAssignmentWithDetailsAsync(It.IsAny<int>()))
                .ReturnsAsync((Assignment?)null);

            var request = new CreateDisputeRequest { AssignmentId = 999, Reason = "Test" };

            await Assert.ThrowsAsync<Exception>(() =>
                _disputeService.CreateDisputeAsync("annotator-1", request));
        }

        [Fact]
        public async Task CreateDisputeAsync_NotOwner_ThrowsUnauthorizedAccessException()
        {
            var assignment = new Assignment
            {
                Id = 1,
                AnnotatorId = "other-annotator",
                Status = TaskStatusConstants.Rejected
            };

            _assignmentRepoMock.Setup(r => r.GetAssignmentWithDetailsAsync(1))
                .ReturnsAsync(assignment);

            var request = new CreateDisputeRequest { AssignmentId = 1, Reason = "Test" };

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                _disputeService.CreateDisputeAsync("annotator-1", request));
        }

        [Fact]
        public async Task CreateDisputeAsync_NotRejected_ThrowsException()
        {
            var assignment = new Assignment
            {
                Id = 1,
                AnnotatorId = "annotator-1",
                Status = TaskStatusConstants.Submitted
            };

            _assignmentRepoMock.Setup(r => r.GetAssignmentWithDetailsAsync(1))
                .ReturnsAsync(assignment);

            var request = new CreateDisputeRequest { AssignmentId = 1, Reason = "Test" };

            var ex = await Assert.ThrowsAsync<Exception>(() =>
                _disputeService.CreateDisputeAsync("annotator-1", request));
            Assert.Contains("rejected", ex.Message);
        }

        [Fact]
        public async Task CreateDisputeAsync_After48Hours_ThrowsException()
        {
            var assignment = new Assignment
            {
                Id = 1,
                AnnotatorId = "annotator-1",
                Status = TaskStatusConstants.Rejected,
                ReviewLogs = new List<ReviewLog>
                {
                    new ReviewLog
                    {
                        ReviewerId = "reviewer-1",
                        Verdict = "Rejected",
                        CreatedAt = DateTime.UtcNow.AddHours(-50)
                    }
                }
            };

            _assignmentRepoMock.Setup(r => r.GetAssignmentWithDetailsAsync(1))
                .ReturnsAsync(assignment);

            var request = new CreateDisputeRequest { AssignmentId = 1, Reason = "Test" };

            var ex = await Assert.ThrowsAsync<Exception>(() =>
                _disputeService.CreateDisputeAsync("annotator-1", request));
            Assert.Contains("48", ex.Message);
        }

        [Fact]
        public async Task CreateDisputeAsync_SendsNotificationToReviewerAndManager()
        {
            var assignment = new Assignment
            {
                Id = 1,
                AnnotatorId = "annotator-1",
                ReviewerId = "reviewer-1",
                ProjectId = 1,
                Status = TaskStatusConstants.Rejected,
                Project = new Project { Id = 1, ManagerId = "manager-1", Name = "Test Project" },
                ReviewLogs = new List<ReviewLog>
                {
                    new ReviewLog { ReviewerId = "reviewer-1", Verdict = "Rejected", CreatedAt = DateTime.UtcNow }
                }
            };

            _assignmentRepoMock.Setup(r => r.GetAssignmentWithDetailsAsync(1))
                .ReturnsAsync(assignment);
            _disputeRepoMock.Setup(r => r.AddAsync(It.IsAny<Dispute>()))
                .Returns(Task.CompletedTask);

            var request = new CreateDisputeRequest { AssignmentId = 1, Reason = "Test dispute" };

            await _disputeService.CreateDisputeAsync("annotator-1", request);

            _notificationMock.Verify(n => n.SendNotificationAsync(
                "reviewer-1", It.Is<string>(m => m.Contains("filed a dispute")), "Warning"), Times.Once);
            _notificationMock.Verify(n => n.SendNotificationAsync(
                "manager-1", It.Is<string>(m => m.Contains("Reason: Test dispute")), "Warning"), Times.Once);
            _statisticServiceMock.Verify(s => s.TrackDisputeCountAsync("reviewer-1", 1), Times.Once);
        }

        [Fact]
        public async Task CreateDisputeAsync_SendsDisputeEmailsToReviewerAndManager()
        {
            var assignment = new Assignment
            {
                Id = 1,
                AnnotatorId = "annotator-1",
                ReviewerId = "reviewer-1",
                ProjectId = 1,
                DataItemId = 99,
                Status = TaskStatusConstants.Rejected,
                Project = new Project { Id = 1, ManagerId = "manager-1", Name = "Test Project" },
                ReviewLogs = new List<ReviewLog>
                {
                    new ReviewLog { ReviewerId = "reviewer-1", Verdict = "Rejected", CreatedAt = DateTime.UtcNow }
                }
            };

            _assignmentRepoMock.Setup(r => r.GetAssignmentWithDetailsAsync(1))
                .ReturnsAsync(assignment);
            _disputeRepoMock.Setup(r => r.AddAsync(It.IsAny<Dispute>()))
                .Returns(Task.CompletedTask);
            _userRepoMock.Setup(r => r.GetByIdAsync("annotator-1")).ReturnsAsync(new User
            {
                Id = "annotator-1",
                FullName = "Annotator One",
                Email = "annotator@test.com",
                Role = UserRoles.Annotator
            });
            _userRepoMock.Setup(r => r.GetByIdAsync("reviewer-1")).ReturnsAsync(new User
            {
                Id = "reviewer-1",
                FullName = "Reviewer One",
                Email = "reviewer@test.com",
                Role = UserRoles.Reviewer
            });
            _userRepoMock.Setup(r => r.GetByIdAsync("manager-1")).ReturnsAsync(new User
            {
                Id = "manager-1",
                FullName = "Manager One",
                Email = "manager@test.com",
                Role = UserRoles.Manager
            });

            await _disputeService.CreateDisputeAsync("annotator-1", new CreateDisputeRequest
            {
                AssignmentId = 1,
                Reason = "Test dispute"
            });

            _workflowEmailServiceMock.Verify(w => w.SendDisputeCreatedEmailsAsync(
                It.Is<Project>(p => p.Id == 1),
                It.Is<User>(u => u.Id == "annotator-1"),
                It.Is<Assignment>(a => a.Id == 1),
                It.Is<IReadOnlyCollection<User>>(users => users.Count == 1 && users.First().Id == "reviewer-1"),
                It.Is<User?>(u => u != null && u.Id == "manager-1"),
                "Test dispute"), Times.Once);
        }

        [Fact]
        public async Task CreateDisputeAsync_WhenReviewerNotificationFails_StillAttemptsManagerNotification()
        {
            var assignment = new Assignment
            {
                Id = 1,
                AnnotatorId = "annotator-1",
                ReviewerId = "reviewer-1",
                ProjectId = 1,
                Status = TaskStatusConstants.Rejected,
                Project = new Project { Id = 1, ManagerId = "manager-1", Name = "Test Project" },
                ReviewLogs = new List<ReviewLog>
                {
                    new ReviewLog { ReviewerId = "reviewer-1", Verdict = "Rejected", CreatedAt = DateTime.UtcNow }
                }
            };

            _assignmentRepoMock.Setup(r => r.GetAssignmentWithDetailsAsync(1))
                .ReturnsAsync(assignment);
            _disputeRepoMock.Setup(r => r.AddAsync(It.IsAny<Dispute>()))
                .Returns(Task.CompletedTask);
            _notificationMock
                .Setup(n => n.SendNotificationAsync("reviewer-1", It.IsAny<string>(), "Warning"))
                .ThrowsAsync(new InvalidOperationException("SignalR is unavailable"));

            await _disputeService.CreateDisputeAsync("annotator-1", new CreateDisputeRequest
            {
                AssignmentId = 1,
                Reason = "Test dispute"
            });

            _notificationMock.Verify(n => n.SendNotificationAsync(
                "manager-1", It.Is<string>(m => m.Contains("Reason: Test dispute")), "Warning"), Times.Once);
            _logServiceMock.Verify(l => l.LogActionAsync(
                "annotator-1",
                "CreateDisputeNotificationError",
                "Dispute",
                It.IsAny<string>(),
                It.Is<string>(message => message.Contains("reviewer-1")),
                null), Times.Once);
        }

        [Fact]
        public async Task CreateDisputeAsync_WhenAnotherReviewerIsStillVoting_ThrowsException()
        {
            var submittedAt = DateTime.UtcNow;
            var assignment = new Assignment
            {
                Id = 1,
                AnnotatorId = "annotator-1",
                ReviewerId = "reviewer-1",
                ProjectId = 1,
                DataItemId = 99,
                SubmittedAt = submittedAt,
                Status = TaskStatusConstants.Rejected,
                ReviewLogs = new List<ReviewLog>
                {
                    new ReviewLog { ReviewerId = "reviewer-1", Verdict = "Rejected", CreatedAt = submittedAt.AddMinutes(5) }
                }
            };

            var pendingReviewerAssignment = new Assignment
            {
                Id = 2,
                AnnotatorId = "annotator-1",
                ReviewerId = "reviewer-2",
                ProjectId = 1,
                DataItemId = 99,
                SubmittedAt = submittedAt,
                Status = TaskStatusConstants.Submitted,
                ReviewLogs = new List<ReviewLog>()
            };

            _assignmentRepoMock.Setup(r => r.GetAssignmentWithDetailsAsync(1))
                .ReturnsAsync(assignment);
            _assignmentRepoMock.Setup(r => r.GetRelatedAssignmentsForDisputeAsync(1, "annotator-1", 99))
                .ReturnsAsync(new List<Assignment> { pendingReviewerAssignment });

            var ex = await Assert.ThrowsAsync<Exception>(() =>
                _disputeService.CreateDisputeAsync("annotator-1", new CreateDisputeRequest
                {
                    AssignmentId = 1,
                    Reason = "Still waiting"
                }));

            Assert.Contains("still voting", ex.Message);
        }

        [Fact]
        public async Task CreateDisputeAsync_WhenManagerAlreadyRejectedCurrentSubmission_ThrowsException()
        {
            var assignment = new Assignment
            {
                Id = 1,
                AnnotatorId = "annotator-1",
                Status = TaskStatusConstants.Rejected,
                ManagerDecision = "reject",
                ManagerComment = "Please revise and resubmit",
                ReviewLogs = new List<ReviewLog>
                {
                    new ReviewLog { ReviewerId = "reviewer-1", Verdict = "Rejected", CreatedAt = DateTime.UtcNow }
                }
            };

            _assignmentRepoMock.Setup(r => r.GetAssignmentWithDetailsAsync(1))
                .ReturnsAsync(assignment);

            var ex = await Assert.ThrowsAsync<Exception>(() =>
                _disputeService.CreateDisputeAsync("annotator-1", new CreateDisputeRequest
                {
                    AssignmentId = 1,
                    Reason = "I want to dispute again"
                }));

            Assert.Contains("final manager rejection", ex.Message);
        }

        #endregion

        #region ResolveDisputeAsync Tests

        [Fact]
        public async Task ResolveDisputeAsync_WhenAccepted_TracksAccuracyForAnnotatorAndAllReviewers()
        {
            string managerId = "manager-1";
            int projectId = 1;
            string annotatorId = "annotator-1";
            int dataItemId = 1;

            var reviewers = Enumerable.Range(1, 10).Select(i => $"reviewer-{i}").ToList();
            var reviewerUsers = reviewers.Select(id => new User
            {
                Id = id,
                FullName = id,
                Email = $"{id}@test.com",
                Role = UserRoles.Reviewer
            }).ToList();

            var disputedAssignment = new Assignment
            {
                Id = 1,
                ProjectId = projectId,
                DataItemId = dataItemId,
                AnnotatorId = annotatorId,
                ReviewerId = reviewers[0],
                Status = TaskStatusConstants.Rejected,
                ReviewLogs = new List<ReviewLog> { new ReviewLog { ReviewerId = reviewers[0], Verdict = "Rejected" } }
            };

            var relatedAssignments = Enumerable.Range(1, 9)
                .Select(i => new Assignment
                {
                    Id = i + 1,
                    ProjectId = projectId,
                    DataItemId = dataItemId,
                    AnnotatorId = annotatorId,
                    ReviewerId = reviewers[i],
                    Status = TaskStatusConstants.Submitted,
                    ReviewLogs = new List<ReviewLog> { new ReviewLog { ReviewerId = reviewers[i], Verdict = "Approved" } }
                }).ToList();

            var dispute = new Dispute
            {
                Id = 100,
                AssignmentId = disputedAssignment.Id,
                Assignment = disputedAssignment,
                Status = "Pending",
                Reason = "Test",
                CreatedAt = DateTime.UtcNow
            };

            _disputeRepoMock.Setup(r => r.GetDisputeWithDetailsAsync(100)).ReturnsAsync(dispute);
            _projectRepoMock.Setup(r => r.GetByIdAsync(projectId)).ReturnsAsync(new Project { Id = projectId, GuidelineVersion = "1.0" });
            _dataItemRepoMock.Setup(r => r.GetByIdAsync(dataItemId)).ReturnsAsync(new DataItem { Id = dataItemId, Status = TaskStatusConstants.Rejected });
            _assignmentRepoMock.Setup(r => r.GetRelatedAssignmentsForDisputeAsync(disputedAssignment.Id, annotatorId, dataItemId)).ReturnsAsync(relatedAssignments);
            _userRepoMock.Setup(r => r.GetByIdAsync(managerId)).ReturnsAsync(new User { Id = managerId, FullName = "Manager", Email = "manager@test.com", Role = UserRoles.Manager });
            _userRepoMock.Setup(r => r.GetByIdAsync(annotatorId)).ReturnsAsync(new User { Id = annotatorId, FullName = "Annotator", Email = "annotator@test.com", Role = UserRoles.Annotator });
            _userRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(reviewerUsers);

            var request = new ResolveDisputeRequest
            {
                DisputeId = 100,
                IsAccepted = true,
                ManagerComment = "Correct per guideline v1.0"
            };

            await _disputeService.ResolveDisputeAsync(managerId, request);

            _statisticServiceMock.Verify(s => s.TrackDisputeResolutionAsync(
                annotatorId,
                It.Is<List<(string reviewerId, bool wasCorrect)>>(results => results.Count == 10),
                projectId,
                annotatorWasCorrect: true), Times.Once);
            _workflowEmailServiceMock.Verify(w => w.SendDisputeResolutionEmailsAsync(
                It.Is<Project>(p => p.Id == projectId),
                It.Is<User>(u => u.Id == managerId),
                It.Is<User>(u => u.Id == annotatorId),
                It.Is<Assignment>(a => a.Id == disputedAssignment.Id),
                It.Is<IReadOnlyCollection<User>>(users => users.Count == 10),
                It.Is<IReadOnlyCollection<ReviewLog>>(logs => logs.Count == 10),
                true,
                It.Is<string>(comment => comment.Contains("guideline", StringComparison.OrdinalIgnoreCase))),
                Times.Once);
            _notificationMock.Verify(n => n.SendNotificationAsync(
                annotatorId,
                It.Is<string>(m => m.Contains("has been accepted", StringComparison.OrdinalIgnoreCase)),
                "Success"), Times.Once);
            foreach (var reviewerId in reviewers)
            {
                _notificationMock.Verify(n => n.SendNotificationAsync(
                    reviewerId,
                    It.Is<string>(m => m.Contains("resolved a dispute", StringComparison.OrdinalIgnoreCase)),
                    It.IsAny<string>()), Times.Once);
            }
        }

        [Fact]
        public async Task ResolveDisputeAsync_WhenRejected_NotifiesAnnotatorAndReviewer()
        {
            const string managerId = "manager-1";
            const string annotatorId = "annotator-1";
            const string reviewerId = "reviewer-1";
            const int projectId = 2;
            const int dataItemId = 9;

            var assignment = new Assignment
            {
                Id = 15,
                ProjectId = projectId,
                DataItemId = dataItemId,
                AnnotatorId = annotatorId,
                ReviewerId = reviewerId,
                Status = TaskStatusConstants.Rejected,
                ReviewLogs = new List<ReviewLog>
                {
                    new ReviewLog { ReviewerId = reviewerId, Verdict = "Rejected", CreatedAt = DateTime.UtcNow }
                }
            };

            var dispute = new Dispute
            {
                Id = 200,
                AssignmentId = assignment.Id,
                Assignment = assignment,
                Status = "Pending",
                Reason = "Need review",
                CreatedAt = DateTime.UtcNow
            };

            _disputeRepoMock.Setup(r => r.GetDisputeWithDetailsAsync(dispute.Id)).ReturnsAsync(dispute);
            _projectRepoMock.Setup(r => r.GetByIdAsync(projectId)).ReturnsAsync(new Project
            {
                Id = projectId,
                Name = "Rejected Dispute Project",
                GuidelineVersion = "2.0"
            });
            _assignmentRepoMock.Setup(r => r.GetRelatedAssignmentsForDisputeAsync(assignment.Id, annotatorId, dataItemId))
                .ReturnsAsync(new List<Assignment>());
            _userRepoMock.Setup(r => r.GetByIdAsync(managerId)).ReturnsAsync(new User { Id = managerId, Role = UserRoles.Manager });
            _userRepoMock.Setup(r => r.GetByIdAsync(annotatorId)).ReturnsAsync(new User { Id = annotatorId, Role = UserRoles.Annotator });
            _userRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<User>
            {
                new User { Id = reviewerId, Role = UserRoles.Reviewer }
            });

            await _disputeService.ResolveDisputeAsync(managerId, new ResolveDisputeRequest
            {
                DisputeId = dispute.Id,
                IsAccepted = false,
                ManagerComment = "Still incorrect per guideline v2.0"
            });

            Assert.Equal("reject", assignment.ManagerDecision);
            Assert.Equal("Still incorrect per guideline v2.0", assignment.ManagerComment);
            _notificationMock.Verify(n => n.SendNotificationAsync(
                annotatorId,
                It.Is<string>(m => m.Contains("has been rejected", StringComparison.OrdinalIgnoreCase)),
                "Error"), Times.Once);
            _notificationMock.Verify(n => n.SendNotificationAsync(
                reviewerId,
                It.Is<string>(m => m.Contains("resolved a dispute", StringComparison.OrdinalIgnoreCase) &&
                                   m.Contains("upheld the reviewer side", StringComparison.OrdinalIgnoreCase)),
                "Info"), Times.Once);
        }

        [Fact]
        public async Task ResolveDisputeAsync_WhenNotificationsFail_StillPersistsDecisionAndSendsEmails()
        {
            const string managerId = "manager-1";
            const string annotatorId = "annotator-1";
            const string reviewerId = "reviewer-1";
            const int projectId = 2;
            const int dataItemId = 9;

            var assignment = new Assignment
            {
                Id = 15,
                ProjectId = projectId,
                DataItemId = dataItemId,
                AnnotatorId = annotatorId,
                ReviewerId = reviewerId,
                Status = TaskStatusConstants.Rejected,
                ReviewLogs = new List<ReviewLog>
                {
                    new ReviewLog { ReviewerId = reviewerId, Verdict = "Rejected", CreatedAt = DateTime.UtcNow }
                }
            };

            var dispute = new Dispute
            {
                Id = 200,
                AssignmentId = assignment.Id,
                Assignment = assignment,
                Status = "Pending",
                Reason = "Need review",
                CreatedAt = DateTime.UtcNow
            };

            _disputeRepoMock.Setup(r => r.GetDisputeWithDetailsAsync(dispute.Id)).ReturnsAsync(dispute);
            _projectRepoMock.Setup(r => r.GetByIdAsync(projectId)).ReturnsAsync(new Project
            {
                Id = projectId,
                Name = "Rejected Dispute Project",
                GuidelineVersion = "2.0"
            });
            _assignmentRepoMock.Setup(r => r.GetRelatedAssignmentsForDisputeAsync(assignment.Id, annotatorId, dataItemId))
                .ReturnsAsync(new List<Assignment>());
            _userRepoMock.Setup(r => r.GetByIdAsync(managerId)).ReturnsAsync(new User
            {
                Id = managerId,
                FullName = "Manager One",
                Email = "manager@test.com",
                Role = UserRoles.Manager
            });
            _userRepoMock.Setup(r => r.GetByIdAsync(annotatorId)).ReturnsAsync(new User
            {
                Id = annotatorId,
                FullName = "Annotator One",
                Email = "annotator@test.com",
                Role = UserRoles.Annotator
            });
            _userRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<User>
            {
                new User { Id = reviewerId, FullName = "Reviewer One", Email = "reviewer@test.com", Role = UserRoles.Reviewer }
            });
            _notificationMock
                .Setup(n => n.SendNotificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("Notification hub is unavailable"));

            await _disputeService.ResolveDisputeAsync(managerId, new ResolveDisputeRequest
            {
                DisputeId = dispute.Id,
                IsAccepted = false,
                ManagerComment = "Still incorrect per guideline v2.0"
            });

            Assert.Equal("Rejected", dispute.Status);
            _disputeRepoMock.Verify(r => r.SaveChangesAsync(), Times.Once);
            _workflowEmailServiceMock.Verify(w => w.SendDisputeResolutionEmailsAsync(
                It.Is<Project>(p => p.Id == projectId),
                It.Is<User>(u => u.Id == managerId),
                It.Is<User>(u => u.Id == annotatorId),
                It.Is<Assignment>(a => a.Id == assignment.Id),
                It.Is<IReadOnlyCollection<User>>(users => users.Count == 1 && users.First().Id == reviewerId),
                It.Is<IReadOnlyCollection<ReviewLog>>(logs => logs.Count == 1),
                false,
                It.Is<string>(comment => comment.Contains("guideline", StringComparison.OrdinalIgnoreCase))),
                Times.Once);
            _logServiceMock.Verify(l => l.LogActionAsync(
                managerId,
                "ResolveDisputeNotificationError",
                "Dispute",
                dispute.Id.ToString(),
                It.IsAny<string>(),
                null), Times.Exactly(2));
        }

        [Fact]
        public async Task ResolveDisputeAsync_RequiresManagerComment()
        {
            var assignment = new Assignment { Id = 1, AnnotatorId = "annotator-1", Status = TaskStatusConstants.Rejected };
            var dispute = new Dispute { Id = 100, AssignmentId = 1, Assignment = assignment, Status = "Pending" };

            _disputeRepoMock.Setup(r => r.GetDisputeWithDetailsAsync(100)).ReturnsAsync(dispute);
            _projectRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new Project { Id = 1, GuidelineVersion = "1.0" });

            var request = new ResolveDisputeRequest { DisputeId = 100, IsAccepted = true, ManagerComment = "" };

            await Assert.ThrowsAsync<Exception>(() => _disputeService.ResolveDisputeAsync("manager-1", request));
        }

        [Fact]
        public async Task ResolveDisputeAsync_OnlyAllowsPendingDisputes()
        {
            var dispute = new Dispute { Id = 100, Status = "Resolved", Assignment = new Assignment { Id = 1 } };
            _disputeRepoMock.Setup(r => r.GetDisputeWithDetailsAsync(100)).ReturnsAsync(dispute);

            var request = new ResolveDisputeRequest { DisputeId = 100, IsAccepted = true, ManagerComment = "Test" };

            await Assert.ThrowsAsync<Exception>(() => _disputeService.ResolveDisputeAsync("manager-1", request));
        }

        [Fact]
        public async Task ResolveDisputeAsync_UpdatesDataItemStatus_WhenAccepted()
        {
            int dataItemId = 1;
            var assignment = new Assignment
            {
                Id = 1,
                DataItemId = dataItemId,
                AnnotatorId = "annotator-1",
                Status = TaskStatusConstants.Rejected,
                ProjectId = 1
            };
            var dispute = new Dispute
            {
                Id = 100,
                AssignmentId = 1,
                Assignment = assignment,
                Status = "Pending"
            };
            var dataItem = new DataItem { Id = dataItemId, ProjectId = 1, Status = TaskStatusConstants.Rejected };

            _disputeRepoMock.Setup(r => r.GetDisputeWithDetailsAsync(100)).ReturnsAsync(dispute);
            _projectRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new Project { Id = 1, GuidelineVersion = "1.0", Name = "Test" });
            _dataItemRepoMock.Setup(r => r.GetByIdAsync(dataItemId)).ReturnsAsync(dataItem);
            _assignmentRepoMock.Setup(r => r.GetRelatedAssignmentsForDisputeAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(new List<Assignment>());

            var request = new ResolveDisputeRequest { DisputeId = 100, IsAccepted = true, ManagerComment = "Correct" };

            await _disputeService.ResolveDisputeAsync("manager-1", request);

            Assert.Equal(TaskStatusConstants.Approved, dataItem.Status);
        }

        #endregion

        #region GetDisputesAsync Tests

        [Fact]
        public async Task GetDisputesAsync_AsManager_ReturnsAllProjectDisputes()
        {
            var disputes = new List<Dispute>
            {
                new Dispute { Id = 1, Status = "Pending", Assignment = new Assignment { ProjectId = 1 } },
                new Dispute { Id = 2, Status = "Resolved", Assignment = new Assignment { ProjectId = 1 } }
            };

            _disputeRepoMock.Setup(r => r.GetDisputesByProjectAsync(1)).ReturnsAsync(disputes);

            var result = await _disputeService.GetDisputesAsync(1, "manager-1", UserRoles.Manager);

            Assert.Equal(2, result.Count);
        }

        [Fact]
        public async Task GetDisputesAsync_AsAnnotator_ReturnsOnlyOwnDisputes()
        {
            var disputes = new List<Dispute> { new Dispute { Id = 1, AnnotatorId = "annotator-1", Status = "Pending" } };
            _disputeRepoMock.Setup(r => r.GetDisputesByAnnotatorAsync("annotator-1")).ReturnsAsync(disputes);

            var result = await _disputeService.GetDisputesAsync(1, "annotator-1", UserRoles.Annotator);

            Assert.Single(result);
        }

        [Fact]
        public async Task GetDisputesAsync_AsReviewer_ReturnsOnlyReviewerDisputes()
        {
            var disputes = new List<Dispute>
            {
                new Dispute
                {
                    Id = 3,
                    Status = "Resolved",
                    Assignment = new Assignment
                    {
                        ProjectId = 5,
                        ReviewerId = "reviewer-1"
                    }
                }
            };

            _disputeRepoMock
                .Setup(r => r.GetDisputesByReviewerAsync("reviewer-1", 5))
                .ReturnsAsync(disputes);

            var result = await _disputeService.GetDisputesAsync(5, "reviewer-1", UserRoles.Reviewer);

            Assert.Single(result);
            Assert.Equal(3, result[0].Id);
        }

        [Fact]
        public async Task GetDisputesAsync_ReturnsEvidenceMetadataForManagerDecision()
        {
            var submittedAt = DateTime.UtcNow.AddMinutes(-20);
            var latestAnnotationJson = "{\"annotations\":[{\"id\":\"bbox-1\",\"labelId\":1,\"x\":10,\"y\":20,\"width\":30,\"height\":40}],\"__checklist\":{\"1\":[true,false]}}";

            var currentAssignment = new Assignment
            {
                Id = 5,
                ProjectId = 1,
                DataItemId = 9,
                AnnotatorId = "annotator-1",
                SubmittedAt = submittedAt,
                Status = TaskStatusConstants.Rejected,
                RejectCount = 3,
                DataItem = new DataItem { Id = 9, StorageUrl = "https://example.com/image.jpg" },
                Project = new Project
                {
                    Id = 1,
                    Name = "Plate Project",
                    AllowGeometryTypes = "BBOX",
                    GuidelineVersion = "2.1"
                },
                Reviewer = new User { Id = "reviewer-1", FullName = "Reviewer One" },
                Annotations = new List<Annotation>
                {
                    new Annotation
                    {
                        Id = 100,
                        DataJSON = latestAnnotationJson,
                        CreatedAt = submittedAt
                    }
                },
                ReviewLogs = new List<ReviewLog>
                {
                    new ReviewLog
                    {
                        Id = 501,
                        ReviewerId = "reviewer-1",
                        Verdict = "Rejected",
                        Decision = "Reject",
                        Comment = "Plate box is too loose.",
                        ErrorCategory = "[\"Loose box\"]",
                        CreatedAt = submittedAt.AddMinutes(2),
                        ScorePenalty = 10,
                        IsApproved = false
                    }
                }
            };

            var relatedAssignment = new Assignment
            {
                Id = 6,
                ProjectId = 1,
                DataItemId = 9,
                AnnotatorId = "annotator-1",
                SubmittedAt = submittedAt,
                RejectCount = 3,
                Reviewer = new User { Id = "reviewer-2", FullName = "Reviewer Two" },
                ReviewLogs = new List<ReviewLog>
                {
                    new ReviewLog
                    {
                        Id = 502,
                        ReviewerId = "reviewer-2",
                        Verdict = "Approved",
                        Decision = "Approve",
                        Comment = "Looks correct.",
                        CreatedAt = submittedAt.AddMinutes(3),
                        IsApproved = true,
                        IsAudited = true,
                        AuditResult = "Agree"
                    }
                }
            };

            var disputes = new List<Dispute>
            {
                new Dispute
                {
                    Id = 15,
                    AssignmentId = currentAssignment.Id,
                    AnnotatorId = "annotator-1",
                    Annotator = new User { Id = "annotator-1", FullName = "Annotator One" },
                    Reason = "Reviewer rejected the correct plate.",
                    Status = "Resolved",
                    ManagerComment = "Aligned with guideline v2.1.",
                    Manager = new User { Id = "manager-1", FullName = "Manager One" },
                    CreatedAt = submittedAt.AddMinutes(5),
                    ResolvedAt = submittedAt.AddMinutes(10),
                    Assignment = currentAssignment
                }
            };

            _disputeRepoMock.Setup(r => r.GetDisputesByProjectAsync(1)).ReturnsAsync(disputes);
            _assignmentRepoMock
                .Setup(r => r.GetRelatedAssignmentsForDisputeAsync(currentAssignment.Id, "annotator-1", 9))
                .ReturnsAsync(new List<Assignment> { relatedAssignment });

            var result = await _disputeService.GetDisputesAsync(1, "manager-1", UserRoles.Manager);

            var response = Assert.Single(result);
            Assert.Equal(latestAnnotationJson, response.AnnotationData);
            Assert.Equal(9, response.DataItemId);
            Assert.Equal("BBOX", response.ProjectType);
            Assert.Equal("2.1", response.GuidelineVersion);
            Assert.Equal("annotator_correct", response.ResolutionType);
            Assert.Equal("Manager One", response.ManagerName);
            Assert.Equal(3, response.RejectCount);
            Assert.Equal(2, response.ReviewerFeedbacks.Count);
            Assert.Contains(response.ReviewerFeedbacks, feedback =>
                feedback.ReviewLogId == 501 &&
                feedback.Decision == "Reject" &&
                feedback.ScorePenalty == 10);
            Assert.Contains(response.ReviewerFeedbacks, feedback =>
                feedback.ReviewLogId == 502 &&
                feedback.IsAudited &&
                feedback.AuditResult == "Agree");
        }

        [Fact]
        public async Task GetDisputesAsync_WhenCurrentAssignmentHasNoAnnotation_FallsBackToRelatedAssignmentEvidence()
        {
            var submittedAt = DateTime.UtcNow.AddMinutes(-10);
            var fallbackAnnotationJson = "{\"annotations\":[{\"id\":\"poly-1\",\"labelId\":2,\"points\":[{\"x\":10,\"y\":10},{\"x\":40,\"y\":12},{\"x\":36,\"y\":42}]}]}";

            var currentAssignment = new Assignment
            {
                Id = 50,
                ProjectId = 2,
                DataItemId = 20,
                AnnotatorId = "annotator-1",
                SubmittedAt = submittedAt,
                Status = TaskStatusConstants.Rejected,
                DataItem = new DataItem { Id = 20, StorageUrl = "https://example.com/dispute-image.jpg" },
                Project = new Project
                {
                    Id = 2,
                    Name = "Fallback Evidence Project",
                    AllowGeometryTypes = "POLYGON",
                    GuidelineVersion = "5.0"
                },
                Reviewer = new User { Id = "reviewer-1", FullName = "Reviewer One" },
                Annotations = new List<Annotation>(),
                ReviewLogs = new List<ReviewLog>
                {
                    new ReviewLog
                    {
                        Id = 9001,
                        ReviewerId = "reviewer-1",
                        Verdict = "Rejected",
                        Decision = "Reject",
                        Comment = "Need another pass.",
                        CreatedAt = submittedAt.AddMinutes(2),
                        IsApproved = false
                    }
                }
            };

            var relatedAssignment = new Assignment
            {
                Id = 51,
                ProjectId = 2,
                DataItemId = 20,
                AnnotatorId = "annotator-1",
                SubmittedAt = submittedAt,
                Reviewer = new User { Id = "reviewer-2", FullName = "Reviewer Two" },
                Annotations = new List<Annotation>
                {
                    new Annotation
                    {
                        Id = 200,
                        DataJSON = fallbackAnnotationJson,
                        CreatedAt = submittedAt.AddMinutes(1)
                    }
                },
                ReviewLogs = new List<ReviewLog>
                {
                    new ReviewLog
                    {
                        Id = 9002,
                        ReviewerId = "reviewer-2",
                        Verdict = "Approved",
                        Decision = "Approve",
                        Comment = "Polygon looks good.",
                        CreatedAt = submittedAt.AddMinutes(3),
                        IsApproved = true
                    }
                }
            };

            _disputeRepoMock.Setup(r => r.GetDisputesByProjectAsync(2)).ReturnsAsync(
                new List<Dispute>
                {
                    new Dispute
                    {
                        Id = 25,
                        AssignmentId = currentAssignment.Id,
                        AnnotatorId = "annotator-1",
                        Annotator = new User { Id = "annotator-1", FullName = "Annotator One" },
                        Reason = "The polygon was correct.",
                        Status = "Pending",
                        CreatedAt = submittedAt.AddMinutes(4),
                        Assignment = currentAssignment
                    }
                });

            _assignmentRepoMock
                .Setup(r => r.GetRelatedAssignmentsForDisputeAsync(currentAssignment.Id, "annotator-1", 20))
                .ReturnsAsync(new List<Assignment> { relatedAssignment });

            var result = await _disputeService.GetDisputesAsync(2, "manager-1", UserRoles.Manager);

            var response = Assert.Single(result);
            Assert.Equal(fallbackAnnotationJson, response.AnnotationData);
            Assert.Equal(2, response.ReviewerFeedbacks.Count);
        }

        #endregion
    }
}

