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

            _taskService = new TaskService(
                _assignmentRepoMock.Object,
                _dataItemRepoMock.Object,
                _annotationRepoMock.Object,
                _statisticServiceMock.Object,
                _userRepoMock.Object,
                _projectRepoMock.Object,
                _notificationMock.Object,
                _logServiceMock.Object
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
    }
}
