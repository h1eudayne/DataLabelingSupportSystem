using BLL.Interfaces;
using BLL.Services;
using Core.Constants;
using Core.DTOs.Requests;
using Core.Entities;
using Core.Interfaces;
using Moq;
using System.Text.Json;
using Xunit;

namespace BLL.Tests
{
    public class LabelServiceTests
    {
        private readonly Mock<ILabelRepository> _labelRepoMock;
        private readonly Mock<IAssignmentRepository> _assignmentRepoMock;
        private readonly Mock<IActivityLogService> _logServiceMock;
        private readonly Mock<IRepository<Annotation>> _annotationRepoMock;
        private readonly Mock<IProjectRepository> _projectRepoMock;
        private readonly Mock<IAppNotificationService> _notificationMock;

        private readonly LabelService _labelService;

        public LabelServiceTests()
        {
            _labelRepoMock = new Mock<ILabelRepository>();
            _assignmentRepoMock = new Mock<IAssignmentRepository>();
            _logServiceMock = new Mock<IActivityLogService>();
            _annotationRepoMock = new Mock<IRepository<Annotation>>();
            _projectRepoMock = new Mock<IProjectRepository>();
            _notificationMock = new Mock<IAppNotificationService>();

            _labelRepoMock.As<IRepository<LabelClass>>()
                .Setup(r => r.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
                .Returns<Func<Task>, CancellationToken>((operation, _) => operation());

            _labelService = new LabelService(
                _labelRepoMock.Object,
                _assignmentRepoMock.Object,
                _logServiceMock.Object,
                _annotationRepoMock.Object,
                _projectRepoMock.Object,
                _notificationMock.Object
            );
        }

        #region CreateLabelAsync Tests

        [Fact]
        public async Task CreateLabelAsync_WithValidData_CreatesLabel()
        {
            var request = new CreateLabelRequest
            {
                ProjectId = 1,
                Name = "Cat",
                Color = "#FF0000",
                GuideLine = "Images of cats",
                IsDefault = true
            };

            _labelRepoMock.Setup(r => r.ExistsInProjectAsync(request.ProjectId, request.Name)).ReturnsAsync(false);
            _projectRepoMock.Setup(r => r.GetByIdAsync(request.ProjectId)).ReturnsAsync(new Project { Id = 1, GuidelineVersion = "1.0" });
            _labelRepoMock.Setup(r => r.AddAsync(It.IsAny<LabelClass>())).Returns(Task.CompletedTask);
            _labelRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            var result = await _labelService.CreateLabelAsync("user-1", request);

            Assert.NotNull(result);
            Assert.Equal("Cat", result.Name);
            Assert.Equal("#FF0000", result.Color);
            Assert.True(result.IsDefault);
            _labelRepoMock.Verify(r => r.AddAsync(It.IsAny<LabelClass>()), Times.Once);
            _logServiceMock.Verify(l => l.LogActionAsync(It.IsAny<string>(), "CreateLabel", "LabelClass", It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task CreateLabelAsync_DuplicateName_ThrowsException()
        {
            var request = new CreateLabelRequest
            {
                ProjectId = 1,
                Name = "Cat"
            };

            _labelRepoMock.Setup(r => r.ExistsInProjectAsync(1, "Cat")).ReturnsAsync(true);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _labelService.CreateLabelAsync("user-1", request));
        }

        [Fact]
        public async Task CreateLabelAsync_WithChecklist_SerializesChecklist()
        {
            var request = new CreateLabelRequest
            {
                ProjectId = 1,
                Name = "Cat",
                Color = "#FF0000",
                Checklist = new List<string> { "Has ears", "Has whiskers" }
            };

            _labelRepoMock.Setup(r => r.ExistsInProjectAsync(1, "Cat")).ReturnsAsync(false);
            _projectRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new Project { Id = 1 });
            _labelRepoMock.Setup(r => r.AddAsync(It.IsAny<LabelClass>())).Callback<LabelClass>(l =>
            {
                Assert.Equal("[\"Has ears\",\"Has whiskers\"]", l.DefaultChecklist);
            }).Returns(Task.CompletedTask);
            _labelRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            await _labelService.CreateLabelAsync("user-1", request);

            _labelRepoMock.Verify(r => r.AddAsync(It.IsAny<LabelClass>()), Times.Once);
        }

        #endregion

        #region GetLabelsByProjectIdAsync Tests

        [Fact]
        public async Task GetLabelsByProjectIdAsync_ReturnsLabels()
        {
            var labels = new List<LabelClass>
            {
                new LabelClass { Id = 1, Name = "Cat", Color = "#FF0000", DefaultChecklist = "[]" },
                new LabelClass { Id = 2, Name = "Dog", Color = "#00FF00", DefaultChecklist = "[\"Has tail\"]" }
            };

            _labelRepoMock.Setup(r => r.FindAsync(l => l.ProjectId == 1)).ReturnsAsync(labels);

            var result = await _labelService.GetLabelsByProjectIdAsync(1);

            Assert.Equal(2, result.Count);
            Assert.Contains(result, l => l.Name == "Cat");
            Assert.Contains(result, l => l.Name == "Dog");
        }

        [Fact]
        public async Task GetLabelsByProjectIdAsync_EmptyProject_ReturnsEmptyList()
        {
            _labelRepoMock.Setup(r => r.FindAsync(l => l.ProjectId == 999)).ReturnsAsync(new List<LabelClass>());

            var result = await _labelService.GetLabelsByProjectIdAsync(999);

            Assert.Empty(result);
        }

        #endregion

        #region UpdateLabelAsync Tests

        [Fact]
        public async Task UpdateLabelAsync_WithCriticalChange_ReopensCompletedProjectAndOnlyResetsAffectedLabelTasks()
        {
            var label = new LabelClass
            {
                Id = 1,
                ProjectId = 1,
                Name = "OldName",
                GuideLine = "Old guideline",
                Version = "1.0",
                DefaultChecklist = "[]"
            };

            var request = new UpdateLabelRequest
            {
                Name = "NewName",
                Color = "#FF0000",
                GuideLine = "New guideline"
            };

            var project = new Project
            {
                Id = 1,
                Status = ProjectStatusConstants.Completed
            };

            var affectedDataItem = new DataItem
            {
                Id = 11,
                ProjectId = 1,
                StorageUrl = "affected.jpg",
                Status = TaskStatusConstants.Approved
            };

            var unaffectedDataItem = new DataItem
            {
                Id = 12,
                ProjectId = 1,
                StorageUrl = "safe.jpg",
                Status = TaskStatusConstants.Approved
            };

            string affectedDataJson = JsonSerializer.Serialize(new
            {
                annotations = new object[]
                {
                    new { id = "ann-1", labelId = 1, labelName = "OldName", x = 10, y = 20, width = 30, height = 40, type = "BBOX" },
                    new { id = "ann-2", labelId = 99, labelName = "KeepMe", x = 50, y = 60, width = 20, height = 25, type = "BBOX" }
                },
                __checklist = new { },
                __defaultFlags = Array.Empty<object>()
            });

            string unaffectedDataJson = JsonSerializer.Serialize(new
            {
                annotations = new object[]
                {
                    new { id = "ann-safe", labelId = 99, labelName = "Safe", x = 1, y = 1, width = 5, height = 5, type = "BBOX" }
                },
                __checklist = new { },
                __defaultFlags = Array.Empty<object>()
            });

            var affectedAssignments = new List<Assignment>
            {
                new Assignment
                {
                    Id = 101,
                    ProjectId = 1,
                    DataItemId = 11,
                    AnnotatorId = "annotator-1",
                    ReviewerId = "reviewer-1",
                    Status = TaskStatusConstants.Approved,
                    DataItem = affectedDataItem,
                    Annotations = new List<Annotation>
                    {
                        new Annotation
                        {
                            AssignmentId = 101,
                            DataJSON = affectedDataJson,
                            CreatedAt = DateTime.UtcNow.AddMinutes(-1),
                            ClassId = 1
                        }
                    }
                },
                new Assignment
                {
                    Id = 102,
                    ProjectId = 1,
                    DataItemId = 11,
                    AnnotatorId = "annotator-1",
                    ReviewerId = "reviewer-2",
                    Status = TaskStatusConstants.Approved,
                    DataItem = affectedDataItem,
                    Annotations = new List<Annotation>
                    {
                        new Annotation
                        {
                            AssignmentId = 102,
                            DataJSON = affectedDataJson,
                            CreatedAt = DateTime.UtcNow,
                            ClassId = 1
                        }
                    }
                },
            };

            var unaffectedAssignment = new Assignment
            {
                Id = 201,
                ProjectId = 1,
                DataItemId = 12,
                AnnotatorId = "annotator-2",
                ReviewerId = "reviewer-3",
                Status = TaskStatusConstants.Approved,
                DataItem = unaffectedDataItem,
                Annotations = new List<Annotation>
                {
                    new Annotation
                    {
                        AssignmentId = 201,
                        DataJSON = unaffectedDataJson,
                        CreatedAt = DateTime.UtcNow,
                        ClassId = 99
                    }
                }
            };

            var createdAnnotations = new List<Annotation>();

            _labelRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(label);
            _labelRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _projectRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(project);
            _projectRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _assignmentRepoMock.Setup(r => r.GetAssignmentsByProjectWithDetailsAsync(1))
                .ReturnsAsync(affectedAssignments.Append(unaffectedAssignment).ToList());
            _assignmentRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _annotationRepoMock.Setup(r => r.AddAsync(It.IsAny<Annotation>()))
                .Callback<Annotation>(annotation => createdAnnotations.Add(annotation))
                .Returns(Task.CompletedTask);
            _annotationRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            var result = await _labelService.UpdateLabelAsync("user-1", 1, request);

            Assert.Equal("NewName", label.Name);
            Assert.Equal("1.1", label.Version);
            Assert.Equal(ProjectStatusConstants.Active, project.Status);
            Assert.All(affectedAssignments, assignment =>
            {
                Assert.Equal(TaskStatusConstants.Rejected, assignment.Status);
                Assert.False(string.IsNullOrWhiteSpace(assignment.ManagerComment));
            });
            Assert.Equal(TaskStatusConstants.Approved, unaffectedAssignment.Status);
            Assert.Equal(TaskStatusConstants.Assigned, affectedDataItem.Status);
            Assert.Equal(TaskStatusConstants.Approved, unaffectedDataItem.Status);
            Assert.Equal(2, createdAnnotations.Count);
            _notificationMock.Verify(
                n => n.SendNotificationAsync(
                    "annotator-1",
                    It.Is<string>(message =>
                        message.Contains("OldName") &&
                        message.Contains("NewName") &&
                        message.Contains("project")),
                    "Warning",
                    null,
                    null,
                    null,
                    null),
                Times.Once);

            var relabelPayload = JsonDocument.Parse(createdAnnotations[0].DataJSON).RootElement;
            Assert.True(relabelPayload.TryGetProperty("__lockedAnnotations", out var lockedAnnotations));
            Assert.Single(lockedAnnotations.EnumerateArray());
            Assert.Equal(99, lockedAnnotations.EnumerateArray().First().GetProperty("labelId").GetInt32());
            Assert.True(relabelPayload.TryGetProperty("__relabelLabelIds", out var restrictedLabelIds));
            Assert.Single(restrictedLabelIds.EnumerateArray());
            Assert.Equal(1, restrictedLabelIds.EnumerateArray().First().GetInt32());
            Assert.Equal(0, relabelPayload.GetProperty("annotations").GetArrayLength());
        }

        [Fact]
        public async Task UpdateLabelAsync_NonCriticalChange_DoesNotResetTasks()
        {
            var label = new LabelClass
            {
                Id = 1,
                ProjectId = 1,
                Name = "Cat",
                GuideLine = "Same guideline",
                Color = "#FF0000",
                Version = "1.0"
            };

            var request = new UpdateLabelRequest
            {
                Name = "Cat",
                GuideLine = "Same guideline",
                Color = "#00FF00"
            };

            _labelRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(label);
            _labelRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            var result = await _labelService.UpdateLabelAsync("user-1", 1, request);

            Assert.Equal("#00FF00", label.Color);
            Assert.Equal("1.0", label.Version);
            _assignmentRepoMock.Verify(r => r.ResetAssignmentsByProjectAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
            _assignmentRepoMock.Verify(r => r.GetAssignmentsByProjectWithDetailsAsync(It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task UpdateLabelAsync_ReopensTaskGroupWhenAnotherReviewerCopyStillUsesEditedLabel()
        {
            var label = new LabelClass
            {
                Id = 10,
                ProjectId = 1,
                Name = "Vehicle",
                GuideLine = "Old guideline",
                Version = "1.0",
                DefaultChecklist = "[]"
            };

            var request = new UpdateLabelRequest
            {
                Name = "Vehicle v2",
                Color = "#8b5cf6",
                GuideLine = "Updated guideline"
            };

            string unaffectedLatestDataJson = JsonSerializer.Serialize(new
            {
                annotations = new object[]
                {
                    new { id = "ann-safe", labelId = 99, labelName = "Keep", x = 1, y = 1, width = 5, height = 5, type = "BBOX" }
                },
                __checklist = new { },
                __defaultFlags = Array.Empty<object>()
            });

            string editedLabelDataJson = JsonSerializer.Serialize(new
            {
                annotations = new object[]
                {
                    new { id = "ann-target", labelId = 10, labelName = "Vehicle", x = 10, y = 10, width = 20, height = 20, type = "BBOX" },
                    new { id = "ann-keep", labelId = 99, labelName = "Keep", x = 30, y = 30, width = 15, height = 15, type = "BBOX" }
                },
                __checklist = new { },
                __defaultFlags = Array.Empty<object>()
            });

            var sharedDataItem = new DataItem
            {
                Id = 101,
                ProjectId = 1,
                StorageUrl = "car.jpg",
                Status = TaskStatusConstants.Approved
            };

            var firstReviewerAssignment = new Assignment
            {
                Id = 1001,
                ProjectId = 1,
                DataItemId = 101,
                AnnotatorId = "annotator-1",
                ReviewerId = "reviewer-1",
                Status = TaskStatusConstants.Approved,
                DataItem = sharedDataItem,
                Annotations = new List<Annotation>
                {
                    new Annotation
                    {
                        AssignmentId = 1001,
                        DataJSON = editedLabelDataJson,
                        CreatedAt = DateTime.UtcNow.AddMinutes(-5),
                        ClassId = 10
                    },
                    new Annotation
                    {
                        AssignmentId = 1001,
                        DataJSON = unaffectedLatestDataJson,
                        CreatedAt = DateTime.UtcNow,
                        ClassId = 99
                    }
                }
            };

            var secondReviewerAssignment = new Assignment
            {
                Id = 1002,
                ProjectId = 1,
                DataItemId = 101,
                AnnotatorId = "annotator-1",
                ReviewerId = "reviewer-2",
                Status = TaskStatusConstants.Approved,
                DataItem = sharedDataItem,
                Annotations = new List<Annotation>
                {
                    new Annotation
                    {
                        AssignmentId = 1002,
                        DataJSON = editedLabelDataJson,
                        CreatedAt = DateTime.UtcNow.AddMinutes(-1),
                        ClassId = 10
                    }
                }
            };

            var createdAnnotations = new List<Annotation>();
            var project = new Project
            {
                Id = 1,
                Name = "Relabel Project",
                Status = ProjectStatusConstants.Completed
            };

            _labelRepoMock.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(label);
            _labelRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _projectRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(project);
            _projectRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _assignmentRepoMock.Setup(r => r.GetAssignmentsByProjectWithDetailsAsync(1))
                .ReturnsAsync(new List<Assignment> { firstReviewerAssignment, secondReviewerAssignment });
            _assignmentRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _annotationRepoMock.Setup(r => r.AddAsync(It.IsAny<Annotation>()))
                .Callback<Annotation>(annotation => createdAnnotations.Add(annotation))
                .Returns(Task.CompletedTask);
            _annotationRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            await _labelService.UpdateLabelAsync("manager-1", 10, request);

            Assert.All(new[] { firstReviewerAssignment, secondReviewerAssignment }, assignment =>
            {
                Assert.Equal(TaskStatusConstants.Rejected, assignment.Status);
                Assert.False(string.IsNullOrWhiteSpace(assignment.ManagerComment));
            });
            Assert.Equal(TaskStatusConstants.Assigned, sharedDataItem.Status);
            Assert.Equal(ProjectStatusConstants.Active, project.Status);
            Assert.Equal(2, createdAnnotations.Count);
            var payload = JsonDocument.Parse(createdAnnotations[0].DataJSON).RootElement;
            Assert.True(payload.TryGetProperty("__lockedAnnotations", out var relabelLockedAnnotations));
            Assert.Single(relabelLockedAnnotations.EnumerateArray());
            Assert.Equal(99, relabelLockedAnnotations.EnumerateArray().First().GetProperty("labelId").GetInt32());
        }

        [Fact]
        public async Task UpdateLabelAsync_UsesTransactionAndReturnsUpdatedLabel()
        {
            var label = new LabelClass
            {
                Id = 1,
                ProjectId = 1,
                Name = "OldName",
                GuideLine = "Old guideline",
                Version = "1.0",
                DefaultChecklist = "[]"
            };

            var request = new UpdateLabelRequest
            {
                Name = "NewName",
                Color = "#123456",
                GuideLine = "New guideline"
            };

            _labelRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(label);
            _labelRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _assignmentRepoMock.Setup(r => r.GetAssignmentsByProjectWithDetailsAsync(1)).ReturnsAsync(new List<Assignment>());

            var result = await _labelService.UpdateLabelAsync("user-1", 1, request);

            Assert.Equal("NewName", result.Name);
            Assert.Equal("#123456", result.Color);
            Assert.Equal("1.1", label.Version);
            _labelRepoMock.As<IRepository<LabelClass>>().Verify(
                r => r.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task UpdateLabelAsync_LabelNotFound_ThrowsException()
        {
            _labelRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ReturnsAsync((LabelClass?)null);

            await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                _labelService.UpdateLabelAsync("user-1", 999, new UpdateLabelRequest { Name = "Test" }));
        }

        #endregion

        #region DeleteLabelAsync Tests

        [Fact]
        public async Task DeleteLabelAsync_WhenLabelHasBeenUsed_ThrowsAndDoesNotDelete()
        {
            var label = new LabelClass
            {
                Id = 1,
                ProjectId = 1,
                Name = "Cat"
            };
            var annotations = new List<Annotation>
            {
                new Annotation { Id = 1, ClassId = 1 },
                new Annotation { Id = 2, ClassId = 1 }
            };

            _labelRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(label);
            _annotationRepoMock.Setup(r => r.FindAsync(a => a.ClassId == 1)).ReturnsAsync(annotations);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _labelService.DeleteLabelAsync("user-1", 1));

            Assert.Contains("before any annotator starts drawing", ex.Message);
            _labelRepoMock.Verify(r => r.Delete(It.IsAny<LabelClass>()), Times.Never);
            _labelRepoMock.Verify(r => r.SaveChangesAsync(), Times.Never);
            _logServiceMock.Verify(l => l.LogActionAsync(It.IsAny<string>(), "DeleteLabel", "LabelClass", "1", It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task DeleteLabelAsync_WhenLabelHasNotBeenUsed_DeletesSuccessfully()
        {
            var label = new LabelClass
            {
                Id = 1,
                ProjectId = 1,
                Name = "UnusedLabel"
            };

            _labelRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(label);
            _annotationRepoMock.Setup(r => r.FindAsync(a => a.ClassId == 1)).ReturnsAsync(new List<Annotation>());
            _labelRepoMock.Setup(r => r.Delete(It.IsAny<LabelClass>())).Verifiable();
            _labelRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            await _labelService.DeleteLabelAsync("user-1", 1);

            _labelRepoMock.Verify(r => r.Delete(It.IsAny<LabelClass>()), Times.Once);
            _logServiceMock.Verify(l => l.LogActionAsync(It.IsAny<string>(), "DeleteLabel", "LabelClass", "1", It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task DeleteLabelAsync_LabelNotFound_ThrowsException()
        {
            _labelRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ReturnsAsync((LabelClass?)null);

            await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                _labelService.DeleteLabelAsync("user-1", 999));
        }

        #endregion

        #region CheckLabelUsageAsync Tests

        [Fact]
        public async Task CheckLabelUsageAsync_LabelInUse_ReturnsUsageInfo()
        {
            var annotations = new List<Annotation>
            {
                new Annotation { Id = 1, ClassId = 1 },
                new Annotation { Id = 2, ClassId = 1 },
                new Annotation { Id = 3, ClassId = 1 }
            };
            var label = new LabelClass { Id = 1, Name = "Cat" };

            _annotationRepoMock.Setup(r => r.FindAsync(a => a.ClassId == 1)).ReturnsAsync(annotations);
            _labelRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(label);

            var result = await _labelService.CheckLabelUsageAsync(1);

            Assert.Equal(1, result.LabelId);
            Assert.Equal("Cat", result.LabelName);
            Assert.Equal(3, result.UsageCount);
            Assert.True(result.RequiresConfirmation);
            Assert.Contains("cannot be deleted", result.WarningMessage);
        }

        [Fact]
        public async Task CheckLabelUsageAsync_LabelNotInUse_ReturnsNoWarning()
        {
            _annotationRepoMock.Setup(r => r.FindAsync(a => a.ClassId == 1)).ReturnsAsync(new List<Annotation>());
            _labelRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new LabelClass { Id = 1, Name = "Unused" });

            var result = await _labelService.CheckLabelUsageAsync(1);

            Assert.Equal(0, result.UsageCount);
            Assert.False(result.RequiresConfirmation);
            Assert.Contains("deleted safely", result.WarningMessage);
        }

        #endregion
    }
}

