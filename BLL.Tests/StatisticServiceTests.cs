using BLL.Interfaces;
using BLL.Services;
using Core.Entities;
using Core.DTOs.Responses;
using Core.Interfaces;
using Moq;
using Xunit;

namespace BLL.Tests
{
    public class StatisticServiceTests
    {
        private readonly Mock<IRepository<UserProjectStat>> _statsRepoMock;
        private readonly Mock<IProjectRepository> _projectRepoMock;
        private readonly Mock<IAssignmentRepository> _assignmentRepoMock;
        private readonly Mock<IRepository<ReviewLog>> _reviewLogRepoMock;
        private readonly Mock<IDisputeRepository> _disputeRepoMock;

        private readonly StatisticService _statisticService;

        public StatisticServiceTests()
        {
            _statsRepoMock = new Mock<IRepository<UserProjectStat>>();
            _projectRepoMock = new Mock<IProjectRepository>();
            _assignmentRepoMock = new Mock<IAssignmentRepository>();
            _reviewLogRepoMock = new Mock<IRepository<ReviewLog>>();
            _disputeRepoMock = new Mock<IDisputeRepository>();

            _statisticService = new StatisticService(
                _statsRepoMock.Object,
                _projectRepoMock.Object,
                _assignmentRepoMock.Object,
                _reviewLogRepoMock.Object,
                _disputeRepoMock.Object
            );
        }

        #region TrackNewAssignmentAsync Tests

        [Fact]
        public async Task TrackNewAssignmentAsync_NewUser_CreatesStatAndUpdates()
        {
            _statsRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<UserProjectStat>());
            _statsRepoMock.Setup(r => r.AddAsync(It.IsAny<UserProjectStat>())).Returns(Task.CompletedTask);
            _statsRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            await _statisticService.TrackNewAssignmentAsync("user-1", 1, 10);

            _statsRepoMock.Verify(r => r.AddAsync(It.Is<UserProjectStat>(s =>
                s.UserId == "user-1" &&
                s.ProjectId == 1 &&
                s.TotalAssigned == 10
            )), Times.Once);
            _statsRepoMock.Verify(r => r.SaveChangesAsync(), Times.Once);
        }

        [Fact]
        public async Task TrackNewAssignmentAsync_ExistingUser_UpdatesTotalAssigned()
        {
            var existingStat = new UserProjectStat
            {
                UserId = "user-1",
                ProjectId = 1,
                TotalAssigned = 5,
                TotalApproved = 3,
                EfficiencyScore = 60
            };

            _statsRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<UserProjectStat> { existingStat });
            _statsRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            await _statisticService.TrackNewAssignmentAsync("user-1", 1, 5);

            Assert.Equal(10, existingStat.TotalAssigned);
            Assert.Equal(30f, existingStat.EfficiencyScore, 0.01f);
        }

        #endregion

        #region TrackReviewResultAsync Tests

        [Fact]
        public async Task TrackReviewResultAsync_Approval_IncrementsCorrectCounters()
        {
            var annotatorStat = new UserProjectStat
            {
                UserId = "annotator-1",
                ProjectId = 1,
                TotalApproved = 5,
                TotalAssigned = 10,
                AverageQualityScore = 80,
                TotalReviewedTasks = 1
            };
            var reviewerStat = new UserProjectStat
            {
                UserId = "reviewer-1",
                ProjectId = 1
            };

            _statsRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<UserProjectStat> { annotatorStat, reviewerStat });
            _statsRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            await _statisticService.TrackReviewResultAsync(
                "annotator-1",
                "reviewer-1",
                1,
                isApproved: true,
                taskScore: 100,
                isCriticalError: false);

            Assert.Equal(6, annotatorStat.TotalApproved);
            Assert.Equal(1, annotatorStat.TotalManagerDecisions);
            Assert.Equal(2, annotatorStat.TotalReviewedTasks);
            Assert.Equal(1, reviewerStat.TotalReviewsDone);
        }

        [Fact]
        public async Task TrackReviewResultAsync_RejectionWithCriticalError_IncrementsCriticalErrors()
        {
            var annotatorStat = new UserProjectStat
            {
                UserId = "annotator-1",
                ProjectId = 1,
                TotalCriticalErrors = 0
            };
            var reviewerStat = new UserProjectStat
            {
                UserId = "reviewer-1",
                ProjectId = 1
            };

            _statsRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<UserProjectStat> { annotatorStat, reviewerStat });
            _statsRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            await _statisticService.TrackReviewResultAsync(
                "annotator-1",
                "reviewer-1",
                1,
                isApproved: false,
                taskScore: 70,
                isCriticalError: true);

            Assert.Equal(1, annotatorStat.TotalRejected);
            Assert.Equal(1, annotatorStat.TotalCriticalErrors);
        }

        #endregion

        #region TrackAuditResultAsync Tests

        [Fact]
        public async Task TrackAuditResultAsync_CorrectDecision_UpdatesCorrectDecisions()
        {
            var reviewerStat = new UserProjectStat
            {
                UserId = "reviewer-1",
                ProjectId = 1,
                TotalAuditedReviews = 0,
                TotalCorrectDecisions = 0,
                ReviewerQualityScore = 0
            };

            _statsRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<UserProjectStat> { reviewerStat });
            _statsRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            await _statisticService.TrackAuditResultAsync("reviewer-1", 1, isCorrectDecision: true);

            Assert.Equal(1, reviewerStat.TotalAuditedReviews);
            Assert.Equal(1, reviewerStat.TotalCorrectDecisions);
            Assert.Equal(100, reviewerStat.ReviewerQualityScore);
        }

        [Fact]
        public async Task TrackAuditResultAsync_IncorrectDecision_UpdatesCountersWithoutAccuracyIncrease()
        {
            var reviewerStat = new UserProjectStat
            {
                UserId = "reviewer-1",
                ProjectId = 1,
                TotalAuditedReviews = 1,
                TotalCorrectDecisions = 1,
                ReviewerQualityScore = 100
            };

            _statsRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<UserProjectStat> { reviewerStat });
            _statsRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            await _statisticService.TrackAuditResultAsync("reviewer-1", 1, isCorrectDecision: false);

            Assert.Equal(2, reviewerStat.TotalAuditedReviews);
            Assert.Equal(1, reviewerStat.TotalCorrectDecisions);
            Assert.Equal(50, reviewerStat.ReviewerQualityScore);
        }

        #endregion

        #region TrackDisputeResolutionAsync Tests

        [Fact]
        public async Task TrackDisputeResolutionAsync_AnnotatorCorrect_UpdatesAnnotatorAndReviewers()
        {
            var annotatorStat = new UserProjectStat
            {
                UserId = "annotator-1",
                ProjectId = 1,
                TotalApproved = 5,
                TotalRejected = 3
            };
            var reviewer1Stat = new UserProjectStat { UserId = "reviewer-1", ProjectId = 1 };
            var reviewer2Stat = new UserProjectStat { UserId = "reviewer-2", ProjectId = 1 };

            var reviewerResults = new List<(string reviewerId, bool wasCorrect)>
            {
                ("reviewer-1", true),
                ("reviewer-2", false)
            };

            _statsRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(
                new List<UserProjectStat> { annotatorStat, reviewer1Stat, reviewer2Stat });
            _statsRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            await _statisticService.TrackDisputeResolutionAsync(
                "annotator-1",
                reviewerResults,
                1,
                annotatorWasCorrect: true);

            Assert.Equal(1, annotatorStat.TotalManagerDecisions);
            Assert.Equal(1, annotatorStat.TotalCorrectByManager);
            Assert.Equal(6, annotatorStat.TotalApproved);
            Assert.Equal(1, reviewer1Stat.TotalReviewerManagerDecisions);
            Assert.Equal(1, reviewer1Stat.TotalReviewerCorrectByManager);
            Assert.Equal(1, reviewer2Stat.TotalReviewerManagerDecisions);
            Assert.Equal(0, reviewer2Stat.TotalReviewerCorrectByManager);
        }

        [Fact]
        public async Task TrackDisputeResolutionAsync_AnnotatorIncorrect_UpdatesRejectedCount()
        {
            var annotatorStat = new UserProjectStat
            {
                UserId = "annotator-1",
                ProjectId = 1,
                TotalApproved = 5,
                TotalRejected = 3
            };

            var reviewerResults = new List<(string reviewerId, bool wasCorrect)>
            {
                ("reviewer-1", false)
            };

            _statsRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<UserProjectStat> { annotatorStat });
            _statsRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            await _statisticService.TrackDisputeResolutionAsync(
                "annotator-1",
                reviewerResults,
                1,
                annotatorWasCorrect: false);

            Assert.Equal(1, annotatorStat.TotalManagerDecisions);
            Assert.Equal(4, annotatorStat.TotalRejected);
        }

        #endregion

        #region TrackFirstPassCorrectAsync Tests

        [Fact]
        public async Task TrackFirstPassCorrectAsync_IncrementsBothUsers()
        {
            var annotatorStat = new UserProjectStat { UserId = "annotator-1", ProjectId = 1 };
            var reviewerStat = new UserProjectStat { UserId = "reviewer-1", ProjectId = 1 };

            _statsRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(
                new List<UserProjectStat> { annotatorStat, reviewerStat });
            _statsRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            await _statisticService.TrackFirstPassCorrectAsync("annotator-1", "reviewer-1", 1);

            Assert.Equal(1, annotatorStat.TotalFirstPassCorrect);
            Assert.Equal(1, reviewerStat.TotalReviewerManagerDecisions);
            Assert.Equal(1, reviewerStat.TotalReviewerCorrectByManager);
        }

        #endregion

        #region TrackOverrideCountAsync Tests

        [Fact]
        public async Task TrackOverrideCountAsync_IncrementsOverrideCount()
        {
            var reviewerStat = new UserProjectStat { UserId = "reviewer-1", ProjectId = 1, OverrideCount = 0 };

            _statsRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<UserProjectStat> { reviewerStat });
            _statsRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            await _statisticService.TrackOverrideCountAsync("reviewer-1", 1);

            Assert.Equal(1, reviewerStat.OverrideCount);
        }

        #endregion

        #region TrackDisputeCountAsync Tests

        [Fact]
        public async Task TrackDisputeCountAsync_IncrementsDisputeCount()
        {
            var reviewerStat = new UserProjectStat { UserId = "reviewer-1", ProjectId = 1, DisputeCount = 0 };

            _statsRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<UserProjectStat> { reviewerStat });
            _statsRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            await _statisticService.TrackDisputeCountAsync("reviewer-1", 1);

            Assert.Equal(1, reviewerStat.DisputeCount);
        }

        #endregion

        #region GetReviewerStatsAsync Tests

        [Fact]
        public async Task GetReviewerStatsAsync_WithData_ReturnsCorrectStats()
        {
            var reviewerStat = new UserProjectStat
            {
                UserId = "reviewer-1",
                ProjectId = 1,
                TotalAuditedReviews = 5,
                TotalCorrectDecisions = 4,
                ReviewerQualityScore = 80,
                DisputeCount = 2
            };
            var reviewLogs = new List<ReviewLog>
            {
                new ReviewLog { Id = 1, ReviewerId = "reviewer-1", Verdict = "Approved", AssignmentId = 1 },
                new ReviewLog { Id = 2, ReviewerId = "reviewer-1", Verdict = "Approved", AssignmentId = 2 },
                new ReviewLog { Id = 3, ReviewerId = "reviewer-1", Verdict = "Rejected", AssignmentId = 3 }
            };
            var assignments = new List<Assignment>
            {
                new Assignment { Id = 1, ProjectId = 1 },
                new Assignment { Id = 2, ProjectId = 1 },
                new Assignment { Id = 3, ProjectId = 1 }
            };
            var project = new Project { Id = 1, Name = "Test Project" };

            _statsRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<UserProjectStat> { reviewerStat });
            _reviewLogRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(reviewLogs);
            _disputeRepoMock.Setup(r => r.GetDisputesByReviewerAsync("reviewer-1", 0)).ReturnsAsync(new List<Dispute>());
            _assignmentRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => assignments.FirstOrDefault(a => a.Id == id));
            _projectRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(project);

            var result = await _statisticService.GetReviewerStatsAsync("reviewer-1");

            Assert.Equal("reviewer-1", result.ReviewerId);
            Assert.Equal(3, result.TotalReviews);
            Assert.Equal(2, result.TotalApproved);
            Assert.Equal(1, result.TotalRejected);
            Assert.Equal(66.67, result.ApprovalRate, 0.01);
        }

        [Fact]
        public async Task GetReviewerStatsAsync_NoData_ReturnsDefaultStats()
        {
            _statsRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<UserProjectStat>());
            _reviewLogRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<ReviewLog>());
            _disputeRepoMock.Setup(r => r.GetDisputesByReviewerAsync("reviewer-1", 0)).ReturnsAsync(new List<Dispute>());

            var result = await _statisticService.GetReviewerStatsAsync("reviewer-1");

            Assert.Equal(0, result.TotalReviews);
            Assert.Equal(0, result.TotalApproved);
            Assert.Equal(100, result.KQSScore);
        }

        [Fact]
        public async Task GetReviewerStatsAsync_WhenReviewerLosesDispute_UpdatesTotalDisputesAndOverridden()
        {
            var reviewerStat = new UserProjectStat
            {
                UserId = "reviewer-1",
                ProjectId = 1,
                ReviewerQualityScore = 80
            };

            var reviewLogs = new List<ReviewLog>
            {
                new ReviewLog { Id = 1, ReviewerId = "reviewer-1", Verdict = "Rejected", AssignmentId = 1 }
            };

            var project = new Project { Id = 1, Name = "Test Project" };
            var disputes = new List<Dispute>
            {
                new Dispute
                {
                    Id = 1,
                    AssignmentId = 1,
                    Status = "Resolved",
                    Assignment = new Assignment
                    {
                        Id = 1,
                        ProjectId = 1,
                        ReviewerId = "reviewer-1"
                    }
                }
            };

            _statsRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<UserProjectStat> { reviewerStat });
            _reviewLogRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(reviewLogs);
            _disputeRepoMock.Setup(r => r.GetDisputesByReviewerAsync("reviewer-1", 0)).ReturnsAsync(disputes);
            _projectRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(project);
            _assignmentRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new Assignment { Id = 1, ProjectId = 1 });

            var result = await _statisticService.GetReviewerStatsAsync("reviewer-1");

            Assert.Equal(1, result.TotalDisputes);
            Assert.Equal(1, result.TotalOverridden);
            Assert.Equal(100, result.DisputeRate);
            Assert.Equal(100, result.OverrideRate);
        }

        #endregion

        #region DeductReliabilityScoreForOverdueTasksAsync Tests

        [Fact]
        public async Task DeductReliabilityScoreForOverdueTasksAsync_OverdueAssignments_DeductsScore()
        {
            var overdueAssignment = new Assignment
            {
                Id = 1,
                AnnotatorId = "user-1",
                ProjectId = 1,
                Status = Core.Constants.TaskStatusConstants.InProgress,
                Project = new Project { Id = 1, Deadline = DateTime.UtcNow.AddDays(-5) }
            };
            var userStat = new UserProjectStat
            {
                UserId = "user-1",
                ProjectId = 1,
                EfficiencyScore = 100,
                IsLocked = false
            };

            _assignmentRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(
                new List<Assignment> { overdueAssignment });
            _statsRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<UserProjectStat> { userStat });
            _statsRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            await _statisticService.DeductReliabilityScoreForOverdueTasksAsync();

            Assert.Equal(90, userStat.EfficiencyScore);
        }

        [Fact]
        public async Task DeductReliabilityScoreForOverdueTasksAsync_LockedUser_NoDeduction()
        {
            var overdueAssignment = new Assignment
            {
                Id = 1,
                AnnotatorId = "user-1",
                Status = Core.Constants.TaskStatusConstants.InProgress,
                Project = new Project { Id = 1, Deadline = DateTime.UtcNow.AddDays(-5) }
            };
            var userStat = new UserProjectStat
            {
                UserId = "user-1",
                ProjectId = 1,
                EfficiencyScore = 100,
                IsLocked = true
            };

            _assignmentRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(
                new List<Assignment> { overdueAssignment });
            _statsRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<UserProjectStat> { userStat });
            _statsRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            var initialScore = userStat.EfficiencyScore;

            await _statisticService.DeductReliabilityScoreForOverdueTasksAsync();

            Assert.Equal(100, userStat.EfficiencyScore);
        }

        #endregion
    }
}

