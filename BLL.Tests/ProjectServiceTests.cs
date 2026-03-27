using BLL.Interfaces;
using BLL.Services;
using Core.Constants;
using Core.DTOs.Requests;
using Core.DTOs.Responses;
using Core.Entities;
using DAL.Interfaces;
using Moq;
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
        private readonly Mock<IAppNotificationService> _notificationMock;

        private readonly ProjectService _projectService;

        public ProjectServiceTests()
        {
            _projectRepoMock = new Mock<IProjectRepository>();
            _userRepoMock = new Mock<IUserRepository>();
            _statsRepoMock = new Mock<IRepository<UserProjectStat>>();
            _assignmentRepoMock = new Mock<IAssignmentRepository>();
            _activityLogRepoMock = new Mock<IActivityLogRepository>();
            _flagRepoMock = new Mock<IRepository<ProjectFlag>>();
            _notificationMock = new Mock<IAppNotificationService>();

            _projectService = new ProjectService(
                _projectRepoMock.Object,
                _userRepoMock.Object,
                _statsRepoMock.Object,
                _assignmentRepoMock.Object,
                _activityLogRepoMock.Object,
                _flagRepoMock.Object,
                _notificationMock.Object
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
            Assert.Equal(1, result.ProcessedItems);
            Assert.Single(result.Labels);
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

        #region CompleteProjectAsync Tests

        [Fact]
        public async Task CompleteProjectAsync_AllTasksApproved_CompletesProject()
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
                            new Assignment { Id = 1, Status = TaskStatusConstants.Approved }
                        }
                    }
                }
            };

            _projectRepoMock.Setup(r => r.GetProjectWithDetailsAsync(1)).ReturnsAsync(project);
            _projectRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _activityLogRepoMock.Setup(r => r.AddAsync(It.IsAny<ActivityLog>())).Returns(Task.CompletedTask);
            _activityLogRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            await _projectService.CompleteProjectAsync(1, "manager-1");

            Assert.Equal(ProjectStatusConstants.Completed, project.Status);
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

            // BUG-FIX: Items are now added via AddDataItemsAsync to DbContext directly
            _projectRepoMock.Verify(r => r.AddDataItemsAsync(It.Is<List<DataItem>>(items =>
                items.Count == 3 &&
                items.All(d => d.Status == TaskStatusConstants.New) &&
                items.All(d => d.ProjectId == 1)
            )), Times.Once);
        }

        [Fact]
        public async Task ImportDataItemsAsync_BUGFIX_AddingOneItem_ShouldReturnCountOfOne()
        {
            // This test verifies the bug fix: previously, adding 1 item would return 0
            // because items were added to navigation property instead of DbContext directly
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

            // Add exactly 1 item
            var urls = new List<string> { "single_image.jpg" };

            await _projectService.ImportDataItemsAsync(1, urls);

            // Verify exactly 1 item was passed to AddDataItemsAsync
            Assert.NotNull(capturedItems);
            Assert.Single(capturedItems);
            Assert.Equal("single_image.jpg", capturedItems[0].StorageUrl);
            Assert.Equal(TaskStatusConstants.New, capturedItems[0].Status);
            Assert.Equal(1, capturedItems[0].BucketId); // First item should be in bucket 1
        }

        [Fact]
        public async Task ImportDataItemsAsync_BUGFIX_BucketIdCalculation_ShouldBeCorrect()
        {
            // Test bucket ID calculation for multiple items
            var project = new Project
            {
                Id = 1,
                Name = "Test Project",
                ManagerId = "manager-1",
                DataItems = new List<DataItem>()
            };

            List<DataItem>? capturedItems = null;
            _projectRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(project);
            _projectRepoMock.Setup(r => r.GetProjectDataItemsCountAsync(1)).ReturnsAsync(0); // Starting count is 0
            _projectRepoMock.Setup(r => r.AddDataItemsAsync(It.IsAny<List<DataItem>>()))
                .Callback<List<DataItem>>(items => capturedItems = items)
                .Returns(Task.CompletedTask);
            _projectRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _activityLogRepoMock.Setup(r => r.AddAsync(It.IsAny<ActivityLog>())).Returns(Task.CompletedTask);
            _activityLogRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            // Add 55 items (spans across 2 buckets with 50 items each)
            var urls = Enumerable.Range(1, 55).Select(i => $"image_{i}.jpg").ToList();

            await _projectService.ImportDataItemsAsync(1, urls);

            Assert.NotNull(capturedItems);
            Assert.Equal(55, capturedItems.Count);

            // First 50 items should be in bucket 1
            for (int i = 0; i < 50; i++)
            {
                Assert.Equal(1, capturedItems[i].BucketId);
            }

            // Items 51-55 should be in bucket 2
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
            var users = new List<User>
            {
                new User { Id = "u1", ManagerId = "manager-1" },
                new User { Id = "u2", ManagerId = "manager-1" },
                new User { Id = "u3", ManagerId = "other" }
            };

            _projectRepoMock.Setup(r => r.GetProjectsByManagerIdAsync("manager-1")).ReturnsAsync(projects);
            _userRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(users);

            var result = await _projectService.GetManagerStatsAsync("manager-1");

            Assert.Equal(1, result.TotalProjects);
            Assert.Equal(2, result.TotalMembers);
            Assert.Equal(2, result.TotalDataItems);
        }

        #endregion
    }
}
