using BLL.Interfaces;
using BLL.Services;
using Core.Constants;
using Core.DTOs.Requests;
using Core.Entities;
using Core.Interfaces;
using Moq;
using Xunit;

namespace BLL.Tests
{
    public class TaskServiceAssignmentTests
    {
        private readonly Mock<IAssignmentRepository> _assignmentRepoMock;
        private readonly Mock<IRepository<DataItem>> _dataItemRepoMock;
        private readonly Mock<IRepository<Annotation>> _annotationRepoMock;
        private readonly Mock<IStatisticService> _statisticServiceMock;
        private readonly Mock<IUserRepository> _userRepoMock;
        private readonly Mock<IProjectRepository> _projectRepoMock;
        private readonly Mock<IActivityLogService> _logServiceMock;
        private readonly Mock<IAppNotificationService> _notificationMock;
        private readonly Mock<IWorkflowEmailService> _workflowEmailServiceMock;

        private readonly TaskService _taskService;

        public TaskServiceAssignmentTests()
        {
            _assignmentRepoMock = new Mock<IAssignmentRepository>();
            _dataItemRepoMock = new Mock<IRepository<DataItem>>();
            _annotationRepoMock = new Mock<IRepository<Annotation>>();
            _statisticServiceMock = new Mock<IStatisticService>();
            _userRepoMock = new Mock<IUserRepository>();
            _projectRepoMock = new Mock<IProjectRepository>();
            _logServiceMock = new Mock<IActivityLogService>();
            _notificationMock = new Mock<IAppNotificationService>();
            _workflowEmailServiceMock = new Mock<IWorkflowEmailService>();
            _assignmentRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _dataItemRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _annotationRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _projectRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            _taskService = new TaskService(
                _assignmentRepoMock.Object,
                _dataItemRepoMock.Object,
                _annotationRepoMock.Object,
                _statisticServiceMock.Object,
                _userRepoMock.Object,
                _projectRepoMock.Object,
                _notificationMock.Object,
                _logServiceMock.Object,
                _workflowEmailServiceMock.Object
            );
        }

        [Fact]
        public async Task AssignTeamAsync_With10AnnotatorsAnd10Reviewers_Creates1000AssignmentsPerReviewer()
        {
            string managerId = "manager-1";
            int projectId = 1;
            int totalItems = 100;
            int numAnnotators = 10;
            int numReviewers = 10;

            var project = new Project
            {
                Id = projectId,
                Name = "Test Project",
                ManagerId = managerId,
                Status = "InProgress"
            };

            var annotators = Enumerable.Range(1, numAnnotators)
                .Select(i => new User
                {
                    Id = $"annotator-{i}",
                    Username = $"annotator{i}",
                    Email = $"annotator{i}@test.com",
                    Role = UserRoles.Annotator
                })
                .ToList();

            var reviewers = Enumerable.Range(1, numReviewers)
                .Select(i => new User
                {
                    Id = $"reviewer-{i}",
                    Username = $"reviewer{i}",
                    Email = $"reviewer{i}@test.com",
                    Role = UserRoles.Reviewer
                })
                .ToList();

            var dataItems = Enumerable.Range(1, totalItems)
                .Select(i => new DataItem
                {
                    Id = i,
                    ProjectId = projectId,
                    StorageUrl = $"https://example.com/item-{i}.jpg",
                    Status = TaskStatusConstants.New
                })
                .ToList();

            var allUsers = annotators.Concat(reviewers).ToList();

            _projectRepoMock.Setup(r => r.GetByIdAsync(projectId))
                .ReturnsAsync(project);

            _userRepoMock.Setup(r => r.GetAllAsync())
                .ReturnsAsync(allUsers);

            _assignmentRepoMock.Setup(r => r.GetUnassignedDataItemsAsync(projectId, totalItems))
                .ReturnsAsync(dataItems);

            var capturedAssignments = new List<Assignment>();
            _assignmentRepoMock.Setup(r => r.AddAsync(It.IsAny<Assignment>()))
                .Callback<Assignment>(a => capturedAssignments.Add(a))
                .Returns(Task.CompletedTask);

            var request = new AssignTeamRequest
            {
                ProjectId = projectId,
                TotalQuantity = totalItems,
                AnnotatorIds = annotators.Select(a => a.Id).ToList(),
                ReviewerIds = reviewers.Select(r => r.Id).ToList()
            };

            await _taskService.AssignTeamAsync(managerId, request);

            int expectedTotal = totalItems * numAnnotators * numReviewers;
            int expectedPerReviewer = totalItems * numAnnotators;

            Assert.Equal(expectedTotal, capturedAssignments.Count);

            foreach (var reviewer in reviewers)
            {
                var reviewerAssignments = capturedAssignments
                    .Where(a => a.ReviewerId == reviewer.Id)
                    .ToList();

                Assert.Equal(expectedPerReviewer, reviewerAssignments.Count);
            }

            foreach (var annotator in annotators)
            {
                var annotatorAssignments = capturedAssignments
                    .Where(a => a.AnnotatorId == annotator.Id)
                    .ToList();

                Assert.Equal(totalItems * numReviewers, annotatorAssignments.Count);
            }
        }

        [Fact]
        public async Task AssignTeamAsync_WithNoReviewers_CreatesOnlyAnnotatorAssignments()
        {
            string managerId = "manager-1";
            int projectId = 1;
            int totalItems = 100;
            int numAnnotators = 5;

            var project = new Project
            {
                Id = projectId,
                Name = "Test Project",
                ManagerId = managerId,
                Status = "InProgress"
            };

            var annotators = Enumerable.Range(1, numAnnotators)
                .Select(i => new User
                {
                    Id = $"annotator-{i}",
                    Username = $"annotator{i}",
                    Email = $"annotator{i}@test.com",
                    Role = UserRoles.Annotator
                })
                .ToList();

            var dataItems = Enumerable.Range(1, totalItems)
                .Select(i => new DataItem
                {
                    Id = i,
                    ProjectId = projectId,
                    StorageUrl = $"https://example.com/item-{i}.jpg",
                    Status = TaskStatusConstants.New
                })
                .ToList();

            _projectRepoMock.Setup(r => r.GetByIdAsync(projectId))
                .ReturnsAsync(project);

            _userRepoMock.Setup(r => r.GetAllAsync())
                .ReturnsAsync(annotators);

            _assignmentRepoMock.Setup(r => r.GetUnassignedDataItemsAsync(projectId, totalItems))
                .ReturnsAsync(dataItems);

            var capturedAssignments = new List<Assignment>();
            _assignmentRepoMock.Setup(r => r.AddAsync(It.IsAny<Assignment>()))
                .Callback<Assignment>(a => capturedAssignments.Add(a))
                .Returns(Task.CompletedTask);

            var request = new AssignTeamRequest
            {
                ProjectId = projectId,
                TotalQuantity = totalItems,
                AnnotatorIds = annotators.Select(a => a.Id).ToList(),
                ReviewerIds = new List<string>()
            };

            await _taskService.AssignTeamAsync(managerId, request);

            int expectedTotal = totalItems * numAnnotators;

            Assert.Equal(expectedTotal, capturedAssignments.Count);

            foreach (var assignment in capturedAssignments)
            {
                Assert.Null(assignment.ReviewerId);
            }
        }

        [Fact]
        public async Task AssignTeamAsync_WithCompletedProject_ThrowsException()
        {
            string managerId = "manager-1";
            int projectId = 1;

            var project = new Project
            {
                Id = projectId,
                Name = "Test Project",
                ManagerId = managerId,
                Status = "Completed"
            };

            _projectRepoMock.Setup(r => r.GetByIdAsync(projectId))
                .ReturnsAsync(project);

            var request = new AssignTeamRequest
            {
                ProjectId = projectId,
                TotalQuantity = 10,
                AnnotatorIds = new List<string> { "annotator-1" },
                ReviewerIds = new List<string> { "reviewer-1" }
            };

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => _taskService.AssignTeamAsync(managerId, request)
            );

            Assert.Contains("BR-MNG-20", exception.Message);
        }

        [Fact]
        public async Task AssignTeamAsync_ManagerAssignsToSelf_ThrowsException()
        {
            string managerId = "manager-1";
            int projectId = 1;

            var project = new Project
            {
                Id = projectId,
                Name = "Test Project",
                ManagerId = managerId,
                Status = "InProgress"
            };

            _projectRepoMock.Setup(r => r.GetByIdAsync(projectId))
                .ReturnsAsync(project);

            var request = new AssignTeamRequest
            {
                ProjectId = projectId,
                TotalQuantity = 10,
                AnnotatorIds = new List<string> { managerId },
                ReviewerIds = new List<string> { "reviewer-1" }
            };

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => _taskService.AssignTeamAsync(managerId, request)
            );

            Assert.Contains("BR-MNG-27", exception.Message);
        }

        [Fact]
        public async Task AssignTeamAsync_EachAnnotatorReceivesAllItems()
        {
            string managerId = "manager-1";
            int projectId = 1;
            int totalItems = 50;
            int numAnnotators = 3;

            var project = new Project
            {
                Id = projectId,
                Name = "Test Project",
                ManagerId = managerId,
                Status = "InProgress"
            };

            var annotators = Enumerable.Range(1, numAnnotators)
                .Select(i => new User
                {
                    Id = $"annotator-{i}",
                    Username = $"annotator{i}",
                    Email = $"annotator{i}@test.com",
                    Role = UserRoles.Annotator
                })
                .ToList();

            var reviewers = new List<User>
            {
                new User { Id = "reviewer-1", Username = "reviewer1", Email = "reviewer1@test.com", Role = UserRoles.Reviewer }
            };

            var dataItems = Enumerable.Range(1, totalItems)
                .Select(i => new DataItem
                {
                    Id = i,
                    ProjectId = projectId,
                    StorageUrl = $"https://example.com/item-{i}.jpg",
                    Status = TaskStatusConstants.New
                })
                .ToList();

            var allUsers = annotators.Concat(reviewers).ToList();

            _projectRepoMock.Setup(r => r.GetByIdAsync(projectId))
                .ReturnsAsync(project);

            _userRepoMock.Setup(r => r.GetAllAsync())
                .ReturnsAsync(allUsers);

            _assignmentRepoMock.Setup(r => r.GetUnassignedDataItemsAsync(projectId, totalItems))
                .ReturnsAsync(dataItems);

            var capturedAssignments = new List<Assignment>();
            _assignmentRepoMock.Setup(r => r.AddAsync(It.IsAny<Assignment>()))
                .Callback<Assignment>(a => capturedAssignments.Add(a))
                .Returns(Task.CompletedTask);

            var request = new AssignTeamRequest
            {
                ProjectId = projectId,
                TotalQuantity = totalItems,
                AnnotatorIds = annotators.Select(a => a.Id).ToList(),
                ReviewerIds = reviewers.Select(r => r.Id).ToList()
            };

            await _taskService.AssignTeamAsync(managerId, request);

            foreach (var annotator in annotators)
            {
                var annotatorAssignments = capturedAssignments
                    .Where(a => a.AnnotatorId == annotator.Id)
                    .ToList();

                Assert.Equal(totalItems, annotatorAssignments.Count);

                var assignedItemIds = annotatorAssignments.Select(a => a.DataItemId).OrderBy(id => id).ToList();
                var expectedItemIds = dataItems.Select(d => d.Id).OrderBy(id => id).ToList();

                Assert.Equal(expectedItemIds, assignedItemIds);
            }
        }

        [Fact]
        public async Task AssignTeamAsync_WithDuplicateReviewerIds_DeduplicatesAssignments()
        {
            string managerId = "manager-1";
            int projectId = 1;
            int totalItems = 5;

            var project = new Project
            {
                Id = projectId,
                Name = "Test Project",
                ManagerId = managerId,
                Status = "InProgress"
            };

            var annotator = new User
            {
                Id = "annotator-1",
                Username = "annotator1",
                Email = "annotator1@test.com",
                Role = UserRoles.Annotator
            };

            var reviewers = new List<User>
            {
                new User { Id = "reviewer-1", Username = "reviewer1", Email = "reviewer1@test.com", Role = UserRoles.Reviewer },
                new User { Id = "reviewer-2", Username = "reviewer2", Email = "reviewer2@test.com", Role = UserRoles.Reviewer }
            };

            var dataItems = Enumerable.Range(1, totalItems)
                .Select(i => new DataItem
                {
                    Id = i,
                    ProjectId = projectId,
                    StorageUrl = $"https://example.com/item-{i}.jpg",
                    Status = TaskStatusConstants.New
                })
                .ToList();

            _projectRepoMock.Setup(r => r.GetByIdAsync(projectId))
                .ReturnsAsync(project);

            _userRepoMock.Setup(r => r.GetAllAsync())
                .ReturnsAsync(new List<User> { annotator }.Concat(reviewers).ToList());

            _assignmentRepoMock.Setup(r => r.GetUnassignedDataItemsAsync(projectId, totalItems))
                .ReturnsAsync(dataItems);

            var capturedAssignments = new List<Assignment>();
            _assignmentRepoMock.Setup(r => r.AddAsync(It.IsAny<Assignment>()))
                .Callback<Assignment>(a => capturedAssignments.Add(a))
                .Returns(Task.CompletedTask);

            var request = new AssignTeamRequest
            {
                ProjectId = projectId,
                TotalQuantity = totalItems,
                AnnotatorIds = new List<string> { annotator.Id },
                ReviewerIds = new List<string> { "reviewer-1", "reviewer-1", "reviewer-2" }
            };

            await _taskService.AssignTeamAsync(managerId, request);

            Assert.Equal(totalItems * 2, capturedAssignments.Count);
            Assert.Equal(totalItems, capturedAssignments.Count(a => a.ReviewerId == "reviewer-1"));
            Assert.Equal(totalItems, capturedAssignments.Count(a => a.ReviewerId == "reviewer-2"));
        }

        [Fact]
        public async Task AssignTeamAsync_SendsAssignmentEmailsToAnnotatorsAndReviewers()
        {
            const string managerId = "manager-1";
            const int projectId = 1;

            var manager = new User
            {
                Id = managerId,
                FullName = "Manager One",
                Email = "manager@test.com",
                Role = UserRoles.Manager
            };

            var project = new Project
            {
                Id = projectId,
                Name = "Email Project",
                ManagerId = managerId,
                Status = "InProgress",
                Deadline = DateTime.UtcNow.AddDays(7)
            };

            var annotator = new User
            {
                Id = "annotator-1",
                Email = "annotator@test.com",
                FullName = "Annotator One",
                Role = UserRoles.Annotator
            };

            var reviewer = new User
            {
                Id = "reviewer-1",
                Email = "reviewer@test.com",
                FullName = "Reviewer One",
                Role = UserRoles.Reviewer
            };

            var dataItems = new List<DataItem>
            {
                new DataItem { Id = 1, ProjectId = projectId, StorageUrl = "image-1.jpg", Status = TaskStatusConstants.New },
                new DataItem { Id = 2, ProjectId = projectId, StorageUrl = "image-2.jpg", Status = TaskStatusConstants.New }
            };

            _projectRepoMock.Setup(r => r.GetByIdAsync(projectId)).ReturnsAsync(project);
            _userRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<User> { annotator, reviewer, manager });
            _userRepoMock.Setup(r => r.GetByIdAsync(managerId)).ReturnsAsync(manager);
            _assignmentRepoMock.Setup(r => r.GetUnassignedDataItemsAsync(projectId, 2)).ReturnsAsync(dataItems);
            _assignmentRepoMock.Setup(r => r.AddAsync(It.IsAny<Assignment>())).Returns(Task.CompletedTask);

            await _taskService.AssignTeamAsync(managerId, new AssignTeamRequest
            {
                ProjectId = projectId,
                TotalQuantity = 2,
                AnnotatorIds = new List<string> { annotator.Id },
                ReviewerIds = new List<string> { reviewer.Id }
            });

            _workflowEmailServiceMock.Verify(
                w => w.SendAnnotatorAssignmentEmailAsync(project, manager, annotator, 2, 1),
                Times.Once);
            _workflowEmailServiceMock.Verify(
                w => w.SendReviewerAssignmentEmailAsync(project, manager, reviewer, 2, 1),
                Times.Once);
        }

        [Fact]
        public async Task AssignTeamAsync_WhenAnnotatorNotificationFails_StillCreatesAssignmentsAndContinuesOtherSideEffects()
        {
            const string managerId = "manager-1";
            const int projectId = 1;

            var manager = new User
            {
                Id = managerId,
                FullName = "Manager One",
                Email = "manager@test.com",
                Role = UserRoles.Manager
            };

            var project = new Project
            {
                Id = projectId,
                Name = "Resilient Assignment Project",
                ManagerId = managerId,
                Status = "InProgress",
                Deadline = DateTime.UtcNow.AddDays(7)
            };

            var annotator = new User
            {
                Id = "annotator-1",
                Email = "annotator@test.com",
                FullName = "Annotator One",
                Role = UserRoles.Annotator
            };

            var reviewer = new User
            {
                Id = "reviewer-1",
                Email = "reviewer@test.com",
                FullName = "Reviewer One",
                Role = UserRoles.Reviewer
            };

            var dataItems = new List<DataItem>
            {
                new DataItem { Id = 1, ProjectId = projectId, StorageUrl = "image-1.jpg", Status = TaskStatusConstants.New }
            };

            _projectRepoMock.Setup(r => r.GetByIdAsync(projectId)).ReturnsAsync(project);
            _userRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<User> { annotator, reviewer, manager });
            _userRepoMock.Setup(r => r.GetByIdAsync(managerId)).ReturnsAsync(manager);
            _assignmentRepoMock.Setup(r => r.GetUnassignedDataItemsAsync(projectId, 1)).ReturnsAsync(dataItems);
            _assignmentRepoMock.Setup(r => r.AddAsync(It.IsAny<Assignment>())).Returns(Task.CompletedTask);
            _notificationMock
                .Setup(n => n.SendNotificationAsync(annotator.Id, It.IsAny<string>(), "Success"))
                .ThrowsAsync(new InvalidOperationException("SignalR is unavailable"));

            await _taskService.AssignTeamAsync(managerId, new AssignTeamRequest
            {
                ProjectId = projectId,
                TotalQuantity = 1,
                AnnotatorIds = new List<string> { annotator.Id },
                ReviewerIds = new List<string> { reviewer.Id }
            });

            _assignmentRepoMock.Verify(r => r.AddAsync(It.IsAny<Assignment>()), Times.Once);
            _notificationMock.Verify(
                n => n.SendNotificationAsync(reviewer.Id, It.IsAny<string>(), "Info"),
                Times.Once);
            _workflowEmailServiceMock.Verify(
                w => w.SendAnnotatorAssignmentEmailAsync(project, manager, annotator, 1, 1),
                Times.Once);
            _workflowEmailServiceMock.Verify(
                w => w.SendReviewerAssignmentEmailAsync(project, manager, reviewer, 1, 1),
                Times.Once);
            _logServiceMock.Verify(
                l => l.LogActionAsync(
                    managerId,
                    "AssignTeamNotificationError",
                    "Project",
                    projectId.ToString(),
                    It.Is<string>(message => message.Contains(annotator.Id)),
                    null),
                Times.Once);
        }

        [Fact]
        public async Task GetTaskImagesAsync_WithMultipleReviewerCopies_ReturnsDistinctDataItems()
        {
            const string annotatorId = "annotator-1";
            const int projectId = 7;

            var assignments = new List<Assignment>
            {
                new Assignment
                {
                    Id = 11,
                    ProjectId = projectId,
                    DataItemId = 101,
                    AnnotatorId = annotatorId,
                    ReviewerId = "reviewer-1",
                    Status = TaskStatusConstants.Assigned,
                    AssignedDate = DateTime.UtcNow,
                    Project = new Project { Id = projectId, Deadline = DateTime.UtcNow.AddDays(1), MaxTaskDurationHours = 24 },
                    DataItem = new DataItem { Id = 101, StorageUrl = "image-101.jpg" },
                    Annotations = new List<Annotation>
                    {
                        new Annotation { AssignmentId = 11, DataJSON = "[{\"x\":1}]", CreatedAt = DateTime.UtcNow }
                    }
                },
                new Assignment
                {
                    Id = 12,
                    ProjectId = projectId,
                    DataItemId = 101,
                    AnnotatorId = annotatorId,
                    ReviewerId = "reviewer-2",
                    Status = TaskStatusConstants.Assigned,
                    AssignedDate = DateTime.UtcNow,
                    Project = new Project { Id = projectId, Deadline = DateTime.UtcNow.AddDays(1), MaxTaskDurationHours = 24 },
                    DataItem = new DataItem { Id = 101, StorageUrl = "image-101.jpg" }
                },
                new Assignment
                {
                    Id = 21,
                    ProjectId = projectId,
                    DataItemId = 202,
                    AnnotatorId = annotatorId,
                    ReviewerId = "reviewer-1",
                    Status = TaskStatusConstants.Rejected,
                    AssignedDate = DateTime.UtcNow,
                    Project = new Project { Id = projectId, Deadline = DateTime.UtcNow.AddDays(1), MaxTaskDurationHours = 24 },
                    DataItem = new DataItem { Id = 202, StorageUrl = "image-202.jpg" },
                    ReviewLogs = new List<ReviewLog>
                    {
                        new ReviewLog { AssignmentId = 21, ReviewerId = "reviewer-1", Comment = "Need fix", ErrorCategory = "bbox", CreatedAt = DateTime.UtcNow }
                    }
                },
                new Assignment
                {
                    Id = 22,
                    ProjectId = projectId,
                    DataItemId = 202,
                    AnnotatorId = annotatorId,
                    ReviewerId = "reviewer-2",
                    Status = TaskStatusConstants.Approved,
                    AssignedDate = DateTime.UtcNow,
                    Project = new Project { Id = projectId, Deadline = DateTime.UtcNow.AddDays(1), MaxTaskDurationHours = 24 },
                    DataItem = new DataItem { Id = 202, StorageUrl = "image-202.jpg" }
                }
            };

            _assignmentRepoMock
                .Setup(r => r.GetAssignmentsByAnnotatorAsync(annotatorId, projectId, null))
                .ReturnsAsync(assignments);

            var result = await _taskService.GetTaskImagesAsync(projectId, annotatorId);

            Assert.Equal(2, result.Count);

            var firstImage = Assert.Single(result, r => r.DataItemId == 101);
            Assert.Equal(TaskStatusConstants.Assigned, firstImage.Status);
            Assert.Equal("[{\"x\":1}]", firstImage.AnnotationData);

            var secondImage = Assert.Single(result, r => r.DataItemId == 202);
            Assert.Equal("Escalated", secondImage.Status);
            Assert.True(string.IsNullOrEmpty(secondImage.RejectionReason));
            Assert.True(string.IsNullOrEmpty(secondImage.ErrorCategory));
        }

        [Fact]
        public async Task GetAssignedProjectsAsync_WithMultipleReviewerCopies_CountsDistinctImages()
        {
            const string annotatorId = "annotator-1";
            const int projectId = 9;
            var deadline = DateTime.UtcNow.AddDays(3);

            var assignments = new List<Assignment>
            {
                new Assignment
                {
                    Id = 1,
                    ProjectId = projectId,
                    DataItemId = 1,
                    AnnotatorId = annotatorId,
                    ReviewerId = "reviewer-1",
                    Status = TaskStatusConstants.Approved,
                    AssignedDate = DateTime.UtcNow,
                    Project = new Project { Id = projectId, Name = "Matrix Project", Description = "Desc", Deadline = deadline },
                    DataItem = new DataItem { Id = 1, StorageUrl = "image-1.jpg" }
                },
                new Assignment
                {
                    Id = 2,
                    ProjectId = projectId,
                    DataItemId = 1,
                    AnnotatorId = annotatorId,
                    ReviewerId = "reviewer-2",
                    Status = TaskStatusConstants.Approved,
                    AssignedDate = DateTime.UtcNow,
                    Project = new Project { Id = projectId, Name = "Matrix Project", Description = "Desc", Deadline = deadline },
                    DataItem = new DataItem { Id = 1, StorageUrl = "image-1.jpg" }
                },
                new Assignment
                {
                    Id = 3,
                    ProjectId = projectId,
                    DataItemId = 2,
                    AnnotatorId = annotatorId,
                    ReviewerId = "reviewer-1",
                    Status = TaskStatusConstants.Submitted,
                    AssignedDate = DateTime.UtcNow,
                    Project = new Project { Id = projectId, Name = "Matrix Project", Description = "Desc", Deadline = deadline },
                    DataItem = new DataItem { Id = 2, StorageUrl = "image-2.jpg" }
                },
                new Assignment
                {
                    Id = 4,
                    ProjectId = projectId,
                    DataItemId = 2,
                    AnnotatorId = annotatorId,
                    ReviewerId = "reviewer-2",
                    Status = TaskStatusConstants.Submitted,
                    AssignedDate = DateTime.UtcNow,
                    Project = new Project { Id = projectId, Name = "Matrix Project", Description = "Desc", Deadline = deadline },
                    DataItem = new DataItem { Id = 2, StorageUrl = "image-2.jpg" }
                }
            };

            _assignmentRepoMock
                .Setup(r => r.GetAssignmentsByAnnotatorAsync(annotatorId, 0, null))
                .ReturnsAsync(assignments);
            _projectRepoMock
                .Setup(r => r.GetProjectsByIdsAsync(It.Is<List<int>>(ids => ids.Count == 1 && ids.Contains(projectId))))
                .ReturnsAsync(new List<Project>
                {
                    new Project
                    {
                        Id = projectId,
                        Name = "Matrix Project",
                        Status = ProjectStatusConstants.Active,
                        Deadline = deadline,
                        DataItems = new List<DataItem>
                        {
                            new DataItem { Id = 1, Status = TaskStatusConstants.Approved },
                            new DataItem { Id = 2, Status = TaskStatusConstants.New }
                        }
                    }
                });

            var result = await _taskService.GetAssignedProjectsAsync(annotatorId);

            var project = Assert.Single(result);
            Assert.Equal(2, project.TotalImages);
            Assert.Equal(1, project.CompletedImages);
            Assert.Equal("InProgress", project.Status);
        }

        [Fact]
        public async Task GetAssignedProjectsAsync_PrioritizesProjectsWithReturnedImages()
        {
            const string annotatorId = "annotator-1";
            var now = DateTime.UtcNow;

            var assignments = new List<Assignment>
            {
                new Assignment
                {
                    Id = 11,
                    ProjectId = 100,
                    DataItemId = 1001,
                    AnnotatorId = annotatorId,
                    ReviewerId = "reviewer-1",
                    Status = TaskStatusConstants.Rejected,
                    AssignedDate = now.AddHours(-1),
                    Project = new Project { Id = 100, Name = "Returned Project", Description = "Needs relabel", Deadline = now.AddDays(1) },
                    DataItem = new DataItem { Id = 1001, StorageUrl = "returned.jpg" }
                },
                new Assignment
                {
                    Id = 12,
                    ProjectId = 200,
                    DataItemId = 2001,
                    AnnotatorId = annotatorId,
                    ReviewerId = "reviewer-2",
                    Status = TaskStatusConstants.InProgress,
                    AssignedDate = now,
                    Project = new Project { Id = 200, Name = "Regular Project", Description = "Normal work", Deadline = now.AddHours(6) },
                    DataItem = new DataItem { Id = 2001, StorageUrl = "regular.jpg" }
                },
                new Assignment
                {
                    Id = 13,
                    ProjectId = 300,
                    DataItemId = 3001,
                    AnnotatorId = annotatorId,
                    ReviewerId = "reviewer-3",
                    Status = TaskStatusConstants.Approved,
                    AssignedDate = now.AddHours(-2),
                    Project = new Project { Id = 300, Name = "Completed Project", Description = "Done", Deadline = now.AddDays(2) },
                    DataItem = new DataItem { Id = 3001, StorageUrl = "done.jpg" }
                }
            };

            _assignmentRepoMock
                .Setup(r => r.GetAssignmentsByAnnotatorAsync(annotatorId, 0, null))
                .ReturnsAsync(assignments);
            _projectRepoMock
                .Setup(r => r.GetProjectsByIdsAsync(It.Is<List<int>>(ids =>
                    ids.Count == 3 &&
                    ids.Contains(100) &&
                    ids.Contains(200) &&
                    ids.Contains(300))))
                .ReturnsAsync(new List<Project>
                {
                    new Project
                    {
                        Id = 100,
                        Name = "Returned Project",
                        Status = ProjectStatusConstants.Active,
                        Deadline = now.AddDays(1),
                        DataItems = new List<DataItem>
                        {
                            new DataItem { Id = 1001, Status = TaskStatusConstants.Rejected }
                        }
                    },
                    new Project
                    {
                        Id = 200,
                        Name = "Regular Project",
                        Status = ProjectStatusConstants.Active,
                        Deadline = now.AddHours(6),
                        DataItems = new List<DataItem>
                        {
                            new DataItem { Id = 2001, Status = TaskStatusConstants.InProgress }
                        }
                    },
                    new Project
                    {
                        Id = 300,
                        Name = "Completed Project",
                        Status = ProjectStatusConstants.Completed,
                        Deadline = now.AddDays(2),
                        DataItems = new List<DataItem>
                        {
                            new DataItem { Id = 3001, Status = TaskStatusConstants.Approved }
                        }
                    }
                });

            var result = await _taskService.GetAssignedProjectsAsync(annotatorId);

            Assert.Equal(3, result.Count);
            Assert.Equal(100, result[0].ProjectId);
            Assert.Equal("InProgress", result[0].Status);
            Assert.Equal(200, result[1].ProjectId);
            Assert.Equal(300, result[2].ProjectId);
            Assert.Equal("Completed", result[2].Status);
        }

        [Fact]
        public async Task GetAssignedProjectsAsync_WhenAllImagesApprovedButManagerNotConfirmed_ReturnsAwaitingManagerConfirmation()
        {
            const string annotatorId = "annotator-1";
            const int projectId = 400;
            var deadline = DateTime.UtcNow.AddDays(2);

            var assignments = new List<Assignment>
            {
                new Assignment
                {
                    Id = 21,
                    ProjectId = projectId,
                    DataItemId = 4001,
                    AnnotatorId = annotatorId,
                    ReviewerId = "reviewer-1",
                    Status = TaskStatusConstants.Approved,
                    AssignedDate = DateTime.UtcNow.AddHours(-2),
                    Project = new Project { Id = projectId, Name = "Awaiting Confirmation Project", Description = "Waiting for manager", Deadline = deadline },
                    DataItem = new DataItem { Id = 4001, StorageUrl = "approved.jpg" }
                }
            };

            var project = new Project
            {
                Id = projectId,
                Name = "Awaiting Confirmation Project",
                Status = ProjectStatusConstants.Active,
                Deadline = deadline,
                DataItems = new List<DataItem>
                {
                    new DataItem
                    {
                        Id = 4001,
                        Status = TaskStatusConstants.Approved
                    }
                }
            };

            _assignmentRepoMock
                .Setup(r => r.GetAssignmentsByAnnotatorAsync(annotatorId, 0, null))
                .ReturnsAsync(assignments);
            _projectRepoMock
                .Setup(r => r.GetProjectsByIdsAsync(It.Is<List<int>>(ids => ids.Count == 1 && ids.Contains(projectId))))
                .ReturnsAsync(new List<Project> { project });

            var result = await _taskService.GetAssignedProjectsAsync(annotatorId);

            var assignedProject = Assert.Single(result);
            Assert.Equal(ProjectStatusConstants.AwaitingManagerConfirmation, assignedProject.Status);
            Assert.True(assignedProject.IsAwaitingManagerConfirmation);
            Assert.Equal(1, assignedProject.CompletedImages);
        }

        [Fact]
        public async Task SubmitTaskAsync_WithMultipleReviewerCopies_SubmitsAllUnreviewedReviewerAssignments()
        {
            const string annotatorId = "annotator-1";
            const int projectId = 15;

            var assignmentOne = new Assignment
            {
                Id = 1001,
                ProjectId = projectId,
                DataItemId = 500,
                AnnotatorId = annotatorId,
                ReviewerId = "reviewer-1",
                Status = TaskStatusConstants.InProgress,
                AssignedDate = DateTime.UtcNow
            };

            var assignmentTwo = new Assignment
            {
                Id = 1002,
                ProjectId = projectId,
                DataItemId = 500,
                AnnotatorId = annotatorId,
                ReviewerId = "reviewer-2",
                Status = TaskStatusConstants.Assigned,
                AssignedDate = DateTime.UtcNow
            };

            var groupedAssignments = new List<Assignment> { assignmentOne, assignmentTwo };
            var capturedAnnotations = new List<Annotation>();

            _assignmentRepoMock
                .Setup(r => r.GetAssignmentWithDetailsAsync(assignmentOne.Id))
                .ReturnsAsync(assignmentOne);
            _assignmentRepoMock
                .Setup(r => r.GetAssignmentsByAnnotatorAsync(annotatorId, projectId, null))
                .ReturnsAsync(groupedAssignments);
            _annotationRepoMock
                .Setup(r => r.AddAsync(It.IsAny<Annotation>()))
                .Callback<Annotation>(annotation => capturedAnnotations.Add(annotation))
                .Returns(Task.CompletedTask);
            _projectRepoMock
                .Setup(r => r.GetByIdAsync(projectId))
                .ReturnsAsync(new Project { Id = projectId, Name = "Submit Project", ManagerId = "manager-1" });

            await _taskService.SubmitTaskAsync(annotatorId, new SubmitAnnotationRequest
            {
                AssignmentId = assignmentOne.Id,
                DataJSON = "[{\"label\":\"cat\"}]",
                ClassId = 3
            });

            Assert.Equal(TaskStatusConstants.Submitted, assignmentOne.Status);
            Assert.Equal(TaskStatusConstants.Submitted, assignmentTwo.Status);
            Assert.Equal(2, capturedAnnotations.Count);
            Assert.Contains(capturedAnnotations, a => a.AssignmentId == assignmentOne.Id);
            Assert.Contains(capturedAnnotations, a => a.AssignmentId == assignmentTwo.Id);

            _notificationMock.Verify(
                n => n.SendNotificationAsync("manager-1", It.IsAny<string>(), "Info"),
                Times.Once);
            _notificationMock.Verify(
                n => n.SendNotificationAsync("reviewer-1", It.IsAny<string>(), "Info"),
                Times.Once);
            _notificationMock.Verify(
                n => n.SendNotificationAsync("reviewer-2", It.IsAny<string>(), "Info"),
                Times.Once);
        }

        [Fact]
        public async Task SubmitTaskAsync_WhenResubmittingRejectedImage_ResubmitsAllReviewerCopies()
        {
            const string annotatorId = "annotator-1";
            const int projectId = 16;

            var assignmentOne = new Assignment
            {
                Id = 2001,
                ProjectId = projectId,
                DataItemId = 700,
                AnnotatorId = annotatorId,
                ReviewerId = "reviewer-1",
                Status = TaskStatusConstants.Rejected,
                ManagerDecision = "reject",
                ManagerComment = "Previous escalation rejected",
                AssignedDate = DateTime.UtcNow,
                ReviewLogs = new List<ReviewLog>
                {
                    new ReviewLog { AssignmentId = 2001, ReviewerId = "reviewer-1", Verdict = "Rejected", CreatedAt = DateTime.UtcNow.AddMinutes(-2) }
                }
            };

            var assignmentTwo = new Assignment
            {
                Id = 2002,
                ProjectId = projectId,
                DataItemId = 700,
                AnnotatorId = annotatorId,
                ReviewerId = "reviewer-2",
                Status = TaskStatusConstants.Approved,
                ManagerDecision = "reject",
                ManagerComment = "Previous escalation rejected",
                AssignedDate = DateTime.UtcNow,
                ReviewLogs = new List<ReviewLog>
                {
                    new ReviewLog { AssignmentId = 2002, ReviewerId = "reviewer-2", Verdict = "Approved", CreatedAt = DateTime.UtcNow.AddMinutes(-1) }
                }
            };

            var groupedAssignments = new List<Assignment> { assignmentOne, assignmentTwo };
            var capturedAnnotations = new List<Annotation>();

            _assignmentRepoMock
                .Setup(r => r.GetAssignmentWithDetailsAsync(assignmentOne.Id))
                .ReturnsAsync(assignmentOne);
            _assignmentRepoMock
                .Setup(r => r.GetAssignmentsByAnnotatorAsync(annotatorId, projectId, null))
                .ReturnsAsync(groupedAssignments);
            _annotationRepoMock
                .Setup(r => r.AddAsync(It.IsAny<Annotation>()))
                .Callback<Annotation>(annotation => capturedAnnotations.Add(annotation))
                .Returns(Task.CompletedTask);
            _projectRepoMock
                .Setup(r => r.GetByIdAsync(projectId))
                .ReturnsAsync(new Project { Id = projectId, Name = "Resubmit Project", ManagerId = "manager-1" });

            await _taskService.SubmitTaskAsync(annotatorId, new SubmitAnnotationRequest
            {
                AssignmentId = assignmentOne.Id,
                DataJSON = "[{\"label\":\"fixed\"}]",
                ClassId = 9
            });

            Assert.Equal(TaskStatusConstants.Submitted, assignmentOne.Status);
            Assert.Equal(TaskStatusConstants.Submitted, assignmentTwo.Status);
            Assert.True(assignmentOne.SubmittedAt.HasValue);
            Assert.True(assignmentTwo.SubmittedAt.HasValue);
            Assert.Equal(2, capturedAnnotations.Count);
            Assert.Contains(capturedAnnotations, a => a.AssignmentId == assignmentOne.Id);
            Assert.Contains(capturedAnnotations, a => a.AssignmentId == assignmentTwo.Id);
            Assert.Null(assignmentOne.ManagerDecision);
            Assert.Null(assignmentOne.ManagerComment);
            Assert.Null(assignmentTwo.ManagerDecision);
            Assert.Null(assignmentTwo.ManagerComment);
        }

        [Fact]
        public async Task SubmitMultipleTasksAsync_WithMultipleReviewerCopies_NotifiesManagerAndReviewersAfterCommit()
        {
            const string annotatorId = "annotator-1";
            const int projectId = 18;

            var assignmentOne = new Assignment
            {
                Id = 4001,
                ProjectId = projectId,
                DataItemId = 1001,
                AnnotatorId = annotatorId,
                ReviewerId = "reviewer-1",
                Status = TaskStatusConstants.InProgress,
                ManagerDecision = "reject",
                ManagerComment = "Old manager feedback",
                AssignedDate = DateTime.UtcNow,
                Annotations = new List<Annotation>
                {
                    new Annotation { AssignmentId = 4001, DataJSON = "[{\"label\":\"cat\"}]", ClassId = 3, CreatedAt = DateTime.UtcNow.AddMinutes(-2) }
                }
            };

            var assignmentTwo = new Assignment
            {
                Id = 4002,
                ProjectId = projectId,
                DataItemId = 1001,
                AnnotatorId = annotatorId,
                ReviewerId = "reviewer-2",
                Status = TaskStatusConstants.Assigned,
                ManagerDecision = "reject",
                ManagerComment = "Old manager feedback",
                AssignedDate = DateTime.UtcNow,
                Annotations = new List<Annotation>()
            };

            var groupedAssignments = new List<Assignment> { assignmentOne, assignmentTwo };
            _assignmentRepoMock
                .Setup(r => r.GetAssignmentWithDetailsAsync(assignmentOne.Id))
                .ReturnsAsync(assignmentOne);
            _assignmentRepoMock
                .Setup(r => r.GetAssignmentsByAnnotatorAsync(annotatorId, projectId, null))
                .ReturnsAsync(groupedAssignments);
            _projectRepoMock
                .Setup(r => r.GetByIdAsync(projectId))
                .ReturnsAsync(new Project { Id = projectId, Name = "Batch Submit Project", ManagerId = "manager-1" });

            var result = await _taskService.SubmitMultipleTasksAsync(annotatorId, new SubmitMultipleTasksRequest
            {
                AssignmentIds = new List<int> { assignmentOne.Id }
            });

            Assert.Equal(1, result.SuccessCount);
            Assert.Equal(TaskStatusConstants.Submitted, assignmentOne.Status);
            Assert.Equal(TaskStatusConstants.Submitted, assignmentTwo.Status);
            Assert.Null(assignmentOne.ManagerDecision);
            Assert.Null(assignmentOne.ManagerComment);
            Assert.Null(assignmentTwo.ManagerDecision);
            Assert.Null(assignmentTwo.ManagerComment);

            _assignmentRepoMock.Verify(r => r.SaveChangesAsync(), Times.Once);
            _notificationMock.Verify(
                n => n.SendNotificationAsync("manager-1", It.Is<string>(m => m.Contains("batch submitted 1 tasks")), "Info"),
                Times.Once);
            _notificationMock.Verify(
                n => n.SendNotificationAsync("reviewer-1", It.Is<string>(m => m.Contains("batch submitted 1 tasks")), "Info"),
                Times.Once);
            _notificationMock.Verify(
                n => n.SendNotificationAsync("reviewer-2", It.Is<string>(m => m.Contains("batch submitted 1 tasks")), "Info"),
                Times.Once);
        }

        [Fact]
        public async Task SubmitMultipleTasksAsync_WhenNotificationFails_StillReturnsSuccess()
        {
            const string annotatorId = "annotator-1";
            const int projectId = 19;

            var assignmentOne = new Assignment
            {
                Id = 5001,
                ProjectId = projectId,
                DataItemId = 1005,
                AnnotatorId = annotatorId,
                ReviewerId = "reviewer-1",
                Status = TaskStatusConstants.InProgress,
                AssignedDate = DateTime.UtcNow,
                Annotations = new List<Annotation>
                {
                    new Annotation { AssignmentId = 5001, DataJSON = "[{\"label\":\"cat\"}]", ClassId = 3, CreatedAt = DateTime.UtcNow.AddMinutes(-2) }
                }
            };

            var assignmentTwo = new Assignment
            {
                Id = 5002,
                ProjectId = projectId,
                DataItemId = 1005,
                AnnotatorId = annotatorId,
                ReviewerId = "reviewer-2",
                Status = TaskStatusConstants.Assigned,
                AssignedDate = DateTime.UtcNow,
                Annotations = new List<Annotation>()
            };

            var groupedAssignments = new List<Assignment> { assignmentOne, assignmentTwo };
            _assignmentRepoMock
                .Setup(r => r.GetAssignmentWithDetailsAsync(assignmentOne.Id))
                .ReturnsAsync(assignmentOne);
            _assignmentRepoMock
                .Setup(r => r.GetAssignmentsByAnnotatorAsync(annotatorId, projectId, null))
                .ReturnsAsync(groupedAssignments);
            _projectRepoMock
                .Setup(r => r.GetByIdAsync(projectId))
                .ReturnsAsync(new Project { Id = projectId, Name = "Batch Resilient Project", ManagerId = "manager-1" });
            _notificationMock
                .Setup(n => n.SendNotificationAsync("reviewer-1", It.IsAny<string>(), "Info"))
                .ThrowsAsync(new InvalidOperationException("SignalR offline"));

            var result = await _taskService.SubmitMultipleTasksAsync(annotatorId, new SubmitMultipleTasksRequest
            {
                AssignmentIds = new List<int> { assignmentOne.Id }
            });

            Assert.Equal(1, result.SuccessCount);
            Assert.Equal(0, result.FailureCount);
            Assert.Equal(TaskStatusConstants.Submitted, assignmentOne.Status);
            Assert.Equal(TaskStatusConstants.Submitted, assignmentTwo.Status);

            _assignmentRepoMock.Verify(r => r.SaveChangesAsync(), Times.Once);
            _notificationMock.Verify(
                n => n.SendNotificationAsync("manager-1", It.Is<string>(m => m.Contains("batch submitted 1 tasks")), "Info"),
                Times.Once);
            _notificationMock.Verify(
                n => n.SendNotificationAsync("reviewer-2", It.Is<string>(m => m.Contains("batch submitted 1 tasks")), "Info"),
                Times.Once);
            _logServiceMock.Verify(
                l => l.LogActionAsync(
                    annotatorId,
                    "SubmitBatchNotificationError",
                    "Project",
                    projectId.ToString(),
                    It.Is<string>(message => message.Contains("reviewer-1")),
                    null),
                Times.Once);
        }

        [Fact]
        public async Task GetTaskImagesAsync_WhenAnotherReviewerStillPending_KeepsImageInSubmittedState()
        {
            const string annotatorId = "annotator-1";
            const int projectId = 17;

            var assignments = new List<Assignment>
            {
                new Assignment
                {
                    Id = 301,
                    ProjectId = projectId,
                    DataItemId = 901,
                    AnnotatorId = annotatorId,
                    ReviewerId = "reviewer-1",
                    Status = TaskStatusConstants.Rejected,
                    AssignedDate = DateTime.UtcNow,
                    Project = new Project { Id = projectId, Deadline = DateTime.UtcNow.AddDays(1), MaxTaskDurationHours = 24 },
                    DataItem = new DataItem { Id = 901, StorageUrl = "image-901.jpg" },
                    ReviewLogs = new List<ReviewLog>
                    {
                        new ReviewLog { AssignmentId = 301, ReviewerId = "reviewer-1", Verdict = "Rejected", Comment = "Need fix", CreatedAt = DateTime.UtcNow }
                    }
                },
                new Assignment
                {
                    Id = 302,
                    ProjectId = projectId,
                    DataItemId = 901,
                    AnnotatorId = annotatorId,
                    ReviewerId = "reviewer-2",
                    Status = TaskStatusConstants.Submitted,
                    AssignedDate = DateTime.UtcNow,
                    Project = new Project { Id = projectId, Deadline = DateTime.UtcNow.AddDays(1), MaxTaskDurationHours = 24 },
                    DataItem = new DataItem { Id = 901, StorageUrl = "image-901.jpg" }
                }
            };

            _assignmentRepoMock
                .Setup(r => r.GetAssignmentsByAnnotatorAsync(annotatorId, projectId, null))
                .ReturnsAsync(assignments);

            var result = await _taskService.GetTaskImagesAsync(projectId, annotatorId);

            var image = Assert.Single(result);
            Assert.Equal(TaskStatusConstants.Submitted, image.Status);
            Assert.True(string.IsNullOrEmpty(image.RejectionReason));
            Assert.True(string.IsNullOrEmpty(image.ErrorCategory));
        }

        [Fact]
        public async Task GetTaskImagesAsync_WhenManagerResolvedRejectedImage_ReturnsManagerFeedback()
        {
            const string annotatorId = "annotator-1";
            const int projectId = 22;
            var reviewTimestamp = DateTime.UtcNow;

            var assignments = new List<Assignment>
            {
                new Assignment
                {
                    Id = 701,
                    ProjectId = projectId,
                    DataItemId = 1701,
                    AnnotatorId = annotatorId,
                    ReviewerId = "reviewer-1",
                    Status = TaskStatusConstants.Rejected,
                    ManagerDecision = "reject",
                    ManagerComment = "Penalty kept after manager review",
                    AssignedDate = DateTime.UtcNow,
                    Project = new Project { Id = projectId, Deadline = DateTime.UtcNow.AddDays(1), MaxTaskDurationHours = 24 },
                    DataItem = new DataItem { Id = 1701, StorageUrl = "image-1701.jpg" },
                    ReviewLogs = new List<ReviewLog>
                    {
                        new ReviewLog
                        {
                            AssignmentId = 701,
                            ReviewerId = "reviewer-1",
                            Verdict = "Rejected",
                            Comment = "Need tighter boxes",
                            ErrorCategory = "bbox",
                            CreatedAt = reviewTimestamp
                        }
                    }
                }
            };

            _assignmentRepoMock
                .Setup(r => r.GetAssignmentsByAnnotatorAsync(annotatorId, projectId, null))
                .ReturnsAsync(assignments);

            var result = await _taskService.GetTaskImagesAsync(projectId, annotatorId);

            var image = Assert.Single(result);
            Assert.Equal(TaskStatusConstants.Rejected, image.Status);
            Assert.Equal("Need tighter boxes", image.RejectionReason);
            Assert.Equal("bbox", image.ErrorCategory);
            Assert.Equal("reject", image.ManagerDecision);
            Assert.Equal("Penalty kept after manager review", image.ManagerComment);
            Assert.Equal(reviewTimestamp, image.LatestReviewAt);
        }

        [Fact]
        public async Task GetTaskImagesAsync_WhenMultipleReviewersRejectedSameImage_ReturnsAllReviewerFeedbacks()
        {
            const string annotatorId = "annotator-1";
            const int projectId = 23;
            var firstReviewAt = DateTime.UtcNow.AddMinutes(-2);
            var secondReviewAt = DateTime.UtcNow.AddMinutes(-1);

            var assignments = new List<Assignment>
            {
                new Assignment
                {
                    Id = 801,
                    ProjectId = projectId,
                    DataItemId = 1801,
                    AnnotatorId = annotatorId,
                    ReviewerId = "reviewer-1",
                    Reviewer = new User { Id = "reviewer-1", FullName = "Reviewer One", Email = "reviewer1@example.com" },
                    Status = TaskStatusConstants.Rejected,
                    SubmittedAt = DateTime.UtcNow.AddMinutes(-10),
                    AssignedDate = DateTime.UtcNow,
                    Project = new Project { Id = projectId, Deadline = DateTime.UtcNow.AddDays(1), MaxTaskDurationHours = 24 },
                    DataItem = new DataItem { Id = 1801, StorageUrl = "image-1801.jpg" },
                    ReviewLogs = new List<ReviewLog>
                    {
                        new ReviewLog
                        {
                            Id = 9001,
                            AssignmentId = 801,
                            ReviewerId = "reviewer-1",
                            Verdict = "Rejected",
                            Comment = "Wrong class",
                            ErrorCategory = "classification",
                            CreatedAt = firstReviewAt
                        }
                    }
                },
                new Assignment
                {
                    Id = 802,
                    ProjectId = projectId,
                    DataItemId = 1801,
                    AnnotatorId = annotatorId,
                    ReviewerId = "reviewer-2",
                    Reviewer = new User { Id = "reviewer-2", FullName = "Reviewer Two", Email = "reviewer2@example.com" },
                    Status = TaskStatusConstants.Rejected,
                    SubmittedAt = DateTime.UtcNow.AddMinutes(-10),
                    AssignedDate = DateTime.UtcNow,
                    Project = new Project { Id = projectId, Deadline = DateTime.UtcNow.AddDays(1), MaxTaskDurationHours = 24 },
                    DataItem = new DataItem { Id = 1801, StorageUrl = "image-1801.jpg" },
                    ReviewLogs = new List<ReviewLog>
                    {
                        new ReviewLog
                        {
                            Id = 9002,
                            AssignmentId = 802,
                            ReviewerId = "reviewer-2",
                            Verdict = "Rejected",
                            Comment = "Bounding box too loose",
                            ErrorCategory = "bbox",
                            CreatedAt = secondReviewAt
                        }
                    }
                }
            };

            _assignmentRepoMock
                .Setup(r => r.GetAssignmentsByAnnotatorAsync(annotatorId, projectId, null))
                .ReturnsAsync(assignments);

            var result = await _taskService.GetTaskImagesAsync(projectId, annotatorId);

            var image = Assert.Single(result);
            Assert.Equal(TaskStatusConstants.Rejected, image.Status);
            Assert.Equal("Bounding box too loose", image.RejectionReason);
            Assert.Equal("bbox", image.ErrorCategory);
            Assert.Equal(secondReviewAt, image.LatestReviewAt);
            Assert.Equal(2, image.ReviewerFeedbacks.Count);
            Assert.Collection(
                image.ReviewerFeedbacks,
                feedback =>
                {
                    Assert.Equal(9002, feedback.ReviewLogId);
                    Assert.Equal("reviewer-2", feedback.ReviewerId);
                    Assert.Equal("Reviewer Two", feedback.ReviewerName);
                    Assert.Equal("Bounding box too loose", feedback.Comment);
                    Assert.Equal("bbox", feedback.ErrorCategories);
                },
                feedback =>
                {
                    Assert.Equal(9001, feedback.ReviewLogId);
                    Assert.Equal("reviewer-1", feedback.ReviewerId);
                    Assert.Equal("Reviewer One", feedback.ReviewerName);
                    Assert.Equal("Wrong class", feedback.Comment);
                    Assert.Equal("classification", feedback.ErrorCategories);
                });
        }

        [Fact]
        public async Task SaveDraftAsync_WhenAssignmentRejected_KeepsRejectedStatus()
        {
            const string annotatorId = "annotator-1";
            const int assignmentId = 21;

            var existingAnnotation = new Annotation
            {
                Id = 300,
                AssignmentId = assignmentId,
                DataJSON = "[{\"label\":\"old\"}]",
                CreatedAt = DateTime.UtcNow.AddMinutes(-5),
                ClassId = 1
            };

            var assignment = new Assignment
            {
                Id = assignmentId,
                AnnotatorId = annotatorId,
                Status = TaskStatusConstants.Rejected,
                Annotations = new List<Annotation> { existingAnnotation }
            };

            Annotation? createdAnnotation = null;

            _assignmentRepoMock
                .Setup(r => r.GetAssignmentWithDetailsAsync(assignmentId))
                .ReturnsAsync(assignment);

            _annotationRepoMock
                .Setup(r => r.AddAsync(It.IsAny<Annotation>()))
                .Callback<Annotation>(annotation => createdAnnotation = annotation)
                .Returns(Task.CompletedTask);

            await _taskService.SaveDraftAsync(annotatorId, new SubmitAnnotationRequest
            {
                AssignmentId = assignmentId,
                DataJSON = "[{\"label\":\"updated\"}]",
                ClassId = 2
            });

            Assert.Equal(TaskStatusConstants.Rejected, assignment.Status);
            Assert.NotNull(createdAnnotation);
            Assert.Equal(assignmentId, createdAnnotation!.AssignmentId);
            Assert.Equal("[{\"label\":\"updated\"}]", createdAnnotation.DataJSON);

            _annotationRepoMock.Verify(r => r.AddAsync(It.IsAny<Annotation>()), Times.Once);
        }
    }
}

