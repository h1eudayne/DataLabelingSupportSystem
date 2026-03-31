using BLL.Interfaces;
using BLL.Services;
using Core.DTOs.Requests;
using Core.Entities;
using Core.Interfaces;
using Moq;
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

        private readonly LabelService _labelService;

        public LabelServiceTests()
        {
            _labelRepoMock = new Mock<ILabelRepository>();
            _assignmentRepoMock = new Mock<IAssignmentRepository>();
            _logServiceMock = new Mock<IActivityLogService>();
            _annotationRepoMock = new Mock<IRepository<Annotation>>();
            _projectRepoMock = new Mock<IProjectRepository>();

            _labelService = new LabelService(
                _labelRepoMock.Object,
                _assignmentRepoMock.Object,
                _logServiceMock.Object,
                _annotationRepoMock.Object,
                _projectRepoMock.Object
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

            await Assert.ThrowsAsync<Exception>(() =>
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
        public async Task UpdateLabelAsync_WithCriticalChange_IncrementsVersionAndResetsTasks()
        {
            var label = new LabelClass
            {
                Id = 1,
                ProjectId = 1,
                Name = "OldName",
                GuideLine = "Old guideline",
                Version = "1.0"
            };

            var request = new UpdateLabelRequest
            {
                Name = "NewName",
                Color = "#FF0000",
                GuideLine = "New guideline"
            };

            _labelRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(label);
            _labelRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
            _assignmentRepoMock.Setup(r => r.CountActiveTasksAsync(1)).ReturnsAsync(5);
            _assignmentRepoMock.Setup(r => r.ResetAssignmentsByProjectAsync(1, It.IsAny<string>())).Returns(Task.CompletedTask);

            var result = await _labelService.UpdateLabelAsync("user-1", 1, request);

            Assert.Equal("NewName", label.Name);
            Assert.Equal("1.1", label.Version);
            _assignmentRepoMock.Verify(r => r.ResetAssignmentsByProjectAsync(1, It.IsAny<string>()), Times.Once);
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
        }

        [Fact]
        public async Task UpdateLabelAsync_LabelNotFound_ThrowsException()
        {
            _labelRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ReturnsAsync((LabelClass?)null);

            await Assert.ThrowsAsync<Exception>(() =>
                _labelService.UpdateLabelAsync("user-1", 999, new UpdateLabelRequest { Name = "Test" }));
        }

        #endregion

        #region DeleteLabelAsync Tests

        [Fact]
        public async Task DeleteLabelAsync_WithActiveTasks_ResetsTasksAndDeletes()
        {
            var label = new LabelClass
            {
                Id = 1,
                ProjectId = 1,
                Name = "Cat"
            };

            _labelRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(label);
            _assignmentRepoMock.Setup(r => r.CountActiveTasksAsync(1)).ReturnsAsync(10);
            _assignmentRepoMock.Setup(r => r.ResetAssignmentsByProjectAsync(1, It.IsAny<string>())).Returns(Task.CompletedTask);
            _labelRepoMock.Setup(r => r.Delete(It.IsAny<LabelClass>())).Verifiable();
            _labelRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            await _labelService.DeleteLabelAsync("user-1", 1);

            _assignmentRepoMock.Verify(r => r.ResetAssignmentsByProjectAsync(1, It.IsAny<string>()), Times.Once);
            _labelRepoMock.Verify(r => r.Delete(It.IsAny<LabelClass>()), Times.Once);
            _logServiceMock.Verify(l => l.LogActionAsync(It.IsAny<string>(), "DeleteLabel", "LabelClass", "1", It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task DeleteLabelAsync_NoActiveTasks_DeletesWithoutReset()
        {
            var label = new LabelClass
            {
                Id = 1,
                ProjectId = 1,
                Name = "UnusedLabel"
            };

            _labelRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(label);
            _assignmentRepoMock.Setup(r => r.CountActiveTasksAsync(1)).ReturnsAsync(0);
            _labelRepoMock.Setup(r => r.Delete(It.IsAny<LabelClass>())).Verifiable();
            _labelRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            await _labelService.DeleteLabelAsync("user-1", 1);

            _assignmentRepoMock.Verify(r => r.ResetAssignmentsByProjectAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
            _labelRepoMock.Verify(r => r.Delete(It.IsAny<LabelClass>()), Times.Once);
        }

        [Fact]
        public async Task DeleteLabelAsync_LabelNotFound_ThrowsException()
        {
            _labelRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ReturnsAsync((LabelClass?)null);

            await Assert.ThrowsAsync<Exception>(() =>
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
            Assert.Contains("Warning", result.WarningMessage);
        }

        [Fact]
        public async Task CheckLabelUsageAsync_LabelNotInUse_ReturnsNoWarning()
        {
            _annotationRepoMock.Setup(r => r.FindAsync(a => a.ClassId == 1)).ReturnsAsync(new List<Annotation>());
            _labelRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new LabelClass { Id = 1, Name = "Unused" });

            var result = await _labelService.CheckLabelUsageAsync(1);

            Assert.Equal(0, result.UsageCount);
            Assert.False(result.RequiresConfirmation);
            Assert.Contains("not currently in use", result.WarningMessage);
        }

        #endregion
    }
}

