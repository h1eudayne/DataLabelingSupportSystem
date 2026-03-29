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
                ReviewerIds = null
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
            Assert.Equal(TaskStatusConstants.Rejected, secondImage.Status);
            Assert.Equal("Need fix", secondImage.RejectionReason);
            Assert.Equal("bbox", secondImage.ErrorCategory);
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

            var result = await _taskService.GetAssignedProjectsAsync(annotatorId);

            var project = Assert.Single(result);
            Assert.Equal(2, project.TotalImages);
            Assert.Equal(1, project.CompletedImages);
            Assert.Equal("InProgress", project.Status);
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
    }
}
