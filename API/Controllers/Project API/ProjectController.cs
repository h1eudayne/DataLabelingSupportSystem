using BLL.Interfaces;
using Core.DTOs.Requests;
using Core.DTOs.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace API.Controllers
{

    [Route("api/projects")]
    [ApiController]
    [Authorize]
    [Tags("3. Project Management")]
    public class ProjectController : ControllerBase
    {
        private readonly IProjectService _projectService;

        public ProjectController(IProjectService projectService)
        {
            _projectService = projectService;
        }
        /// <summary>
        /// AssignReviewers endpoint.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>An IActionResult representing the operation outcome.</returns>
        [HttpPost("assign-reviewers")]
        [Authorize(Roles = "Manager")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        public async Task<IActionResult> AssignReviewers([FromBody] AssignReviewersRequest request)
        {
            try
            {
                await _projectService.AssignReviewersAsync(request);
                return Ok(new { Message = "Reviewers assigned successfully." });
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }

        /// <summary>
        /// GetUserProjectsForAdmin endpoint.
        /// </summary>
        /// <param name="userId">The userId.</param>
        /// <returns>An IActionResult representing the operation outcome.</returns>
        [HttpGet("user/{userId}")]
        [Authorize(Roles = "Admin")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        public async Task<IActionResult> GetUserProjectsForAdmin(string userId)
        {
            try
            {
                var projects = await _projectService.GetUserProjectsByUserIdAsync(userId);
                return Ok(projects);
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }

        /// <summary>
        /// CreateProject endpoint.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>An IActionResult representing the operation outcome.</returns>
        [HttpPost]
        [Authorize(Roles = "Manager")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> CreateProject([FromBody] CreateProjectRequest request)
        {
            var managerId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(managerId)) return Unauthorized();

            try
            {
                if (string.IsNullOrWhiteSpace(request.Name))
                {
                    return BadRequest(new ErrorResponse { Message = "Project name is required." });
                }

                if (request.Name.Length > 255)
                {
                    return BadRequest(new ErrorResponse { Message = "Project name is too long (max 255 characters)." });
                }

                if (request.LabelClasses == null || !request.LabelClasses.Any())
                {
                    return BadRequest(new ErrorResponse { Message = "At least one label class is required." });
                }

                foreach (var label in request.LabelClasses)
                {
                    if (string.IsNullOrWhiteSpace(label.Name))
                    {
                        return BadRequest(new ErrorResponse { Message = "Label name cannot be empty." });
                    }
                    if (label.Name.Length > 100)
                    {
                        return BadRequest(new ErrorResponse { Message = $"Label name '{label.Name}' is too long (max 100 characters)." });
                    }
                }

                Console.WriteLine($"[DEBUG] CreateProject - ManagerId: {managerId}");
                Console.WriteLine($"[DEBUG] CreateProject - Request: {System.Text.Json.JsonSerializer.Serialize(request)}");

                var result = await _projectService.CreateProjectAsync(managerId, request);
                return Ok(result);
            }
            catch (Exception ex)
            {

                Console.WriteLine($"[ERROR] CreateProject failed: {ex}");
                Console.WriteLine($"[ERROR] Inner Exception: {ex.InnerException?.Message}");
                Console.WriteLine($"[ERROR] Stack Trace: {ex.StackTrace}");

                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }

        /// <summary>
        /// RemoveUserFromProject endpoint.
        /// </summary>
        /// <param name="projectId">The projectId.</param>
        /// <param name="userId">The userId.</param>
        /// <returns>An IActionResult representing the operation outcome.</returns>
        [HttpDelete("{projectId}/users/{userId}")]
        [Authorize(Roles = "Manager")]
        public async Task<IActionResult> RemoveUserFromProject(int projectId, string userId)
        {
            try
            {
                await _projectService.RemoveUserFromProjectAsync(projectId, userId);
                return Ok(new { Message = "Successfully removed the user from the project and revoked pending tasks." });
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }

        /// <summary>
        /// UpdateProject endpoint.
        /// </summary>
        /// <param name="id">The id.</param>
        /// <param name="request">The request.</param>
        /// <returns>An IActionResult representing the operation outcome.</returns>
        [HttpPut("{id}")]
        [Authorize(Roles = "Manager")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> UpdateProject(int id, [FromBody] UpdateProjectRequest request)
        {
            var actingUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(actingUserId)) return Unauthorized();
            try
            {
                await _projectService.UpdateProjectAsync(id, request, actingUserId);
                return Ok(new { Message = "Project updated successfully." });
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }

        /// <summary>
        /// GetProjectDetails endpoint.
        /// </summary>
        /// <param name="id">The id.</param>
        /// <returns>An IActionResult representing the operation outcome.</returns>
        [HttpGet("{id}")]
        [Authorize(Roles = "Admin,Manager,Reviewer,Annotator")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> GetProjectDetails(int id)
        {
            try
            {
                var project = await _projectService.GetProjectDetailsAsync(id);
                if (project == null) return NotFound(new ErrorResponse { Message = "Project not found." });
                return Ok(project);
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }

        /// <summary>
        /// GetAllProjects endpoint.
        /// </summary>
        /// <returns>An IActionResult representing the operation outcome.</returns>
        [HttpGet]
        [Authorize(Roles = "Admin,Manager,Reviewer,Annotator")]
        [ProducesResponseType(typeof(List<ProjectSummaryResponse>), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> GetAllProjects()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            try
            {
                List<object> projects;
                switch (userRole)
                {
                    case "Admin":
                        projects = (await _projectService.GetAllProjectsForAdminAsync()).Cast<object>().ToList();
                        break;
                    case "Manager":
                        projects = (await _projectService.GetProjectsByManagerAsync(userId)).Cast<object>().ToList();
                        break;
                    case "Reviewer":
                    case "Annotator":
                        projects = (await _projectService.GetAssignedProjectsForUserAsync(userId)).Cast<object>().ToList();
                        break;
                    default:
                        return Forbid();
                }
                return Ok(projects);
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }

        /// <summary>
        /// GetProjectsByManager endpoint.
        /// </summary>
        /// <param name="managerId">The managerId.</param>
        /// <returns>An IActionResult representing the operation outcome.</returns>
        [HttpGet("managers/{managerId}")]
        [Authorize(Roles = "Admin,Manager")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> GetProjectsByManager(string managerId)
        {
            try
            {
                var projects = await _projectService.GetProjectsByManagerAsync(managerId);
                return Ok(projects);
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }

        /// <summary>
        /// DeleteProject endpoint.
        /// </summary>
        /// <param name="id">The id.</param>
        /// <returns>An IActionResult representing the operation outcome.</returns>
        [HttpDelete("{id}")]
        [Authorize(Roles = "Manager")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> DeleteProject(int id)
        {
            try
            {
                await _projectService.DeleteProjectAsync(id);
                return Ok(new { Message = "Project deleted successfully." });
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }

        /// <summary>
        /// GetAllProjectsForAdmin endpoint.
        /// </summary>
        /// <returns>An IActionResult representing the operation outcome.</returns>
        [HttpGet("all")]
        [Authorize(Roles = "Admin")]
        [ProducesResponseType(typeof(List<ProjectSummaryResponse>), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> GetAllProjectsForAdmin()
        {
            try
            {
                var projects = await _projectService.GetAllProjectsForAdminAsync();
                return Ok(projects);
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }

        /// <summary>
        /// CompleteProject endpoint.
        /// </summary>
        /// <param name="projectId">The projectId.</param>
        /// <returns>An IActionResult representing the operation outcome.</returns>
        [HttpPost("{projectId}/complete")]
        [Authorize(Roles = "Manager")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> CompleteProject(int projectId)
        {
            var managerId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(managerId)) return Unauthorized();

            try
            {
                await _projectService.CompleteProjectAsync(projectId, managerId);
                return Ok(new { Message = "Project completed successfully." });
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }

        /// <summary>
        /// ArchiveProject endpoint.
        /// </summary>
        /// <param name="projectId">The projectId.</param>
        /// <returns>An IActionResult representing the operation outcome.</returns>
        [HttpPost("{projectId}/archive")]
        [Authorize(Roles = "Manager")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> ArchiveProject(int projectId)
        {
            var managerId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(managerId)) return Unauthorized();

            try
            {
                await _projectService.ArchiveProjectAsync(projectId, managerId);
                return Ok(new { Message = "Project archived successfully." });
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }
    }
}