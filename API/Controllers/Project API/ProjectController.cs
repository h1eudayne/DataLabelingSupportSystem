using BLL.Interfaces;
using Core.DTOs.Requests;
using Core.DTOs.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace API.Controllers
{
    /// <summary>
    /// Controller for managing the lifecycle and core details of annotation projects.
    /// </summary>
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
        [HttpPost("assign-reviewers")]
        [Authorize(Roles = "Manager,Admin")]
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
        /// Retrieves all projects for a specific user by their ID.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="userId">The target user's ID.</param>
        /// <returns>A list of projects based on the target user's role.</returns>
        /// <response code="200">Projects retrieved successfully.</response>
        /// <response code="400">Failed to retrieve projects.</response>
        /// <response code="401">User is not authenticated or not authorized.</response>
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
        /// Creates a new annotation project.
        /// </summary>
        /// <remarks>
        /// Accessible by Managers and Admins. The user creating the project is automatically assigned as its Manager.
        /// </remarks>
        /// <param name="request">Payload containing project details like name, description, and deadlines.</param>
        /// <returns>The newly created project's basic information.</returns>
        /// <response code="200">Project created successfully.</response>
        /// <response code="400">Creation failed due to validation errors.</response>
        /// <response code="401">User is not authenticated or not authorized.</response>
        [HttpPost]
        [Authorize(Roles = "Manager,Admin")]
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
                // Log the full exception details
                Console.WriteLine($"[ERROR] CreateProject failed: {ex}");
                Console.WriteLine($"[ERROR] Inner Exception: {ex.InnerException?.Message}");
                Console.WriteLine($"[ERROR] Stack Trace: {ex.StackTrace}");
                
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }
        /// <summary>
        /// Removes a user from the project and revokes their assigned, uncompleted tasks.
        /// </summary>
        [HttpDelete("{projectId}/users/{userId}")]
        [Authorize(Roles = "Manager,Admin")]
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
        /// Updates an existing project's details.
        /// </summary>
        /// <remarks>
        /// Accessible by Managers and Admins.
        /// </remarks>
        /// <param name="id">The ID of the project to update.</param>
        /// <param name="request">Payload containing the updated project information.</param>
        /// <returns>A confirmation message.</returns>
        /// <response code="200">Project updated successfully.</response>
        /// <response code="400">Update failed (e.g., project not found or validation error).</response>
        /// <response code="401">User is not authenticated or not authorized.</response>
        [HttpPut("{id}")]
        [Authorize(Roles = "Manager,Admin")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> UpdateProject(int id, [FromBody] UpdateProjectRequest request)
        {
            try
            {
                await _projectService.UpdateProjectAsync(id, request);
                return Ok(new { Message = "Project updated successfully." });
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }

        /// <summary>
        /// Retrieves detailed information for a specific project.
        /// </summary>
        /// <remarks>
        /// Accessible by all roles involved in the project.
        /// </remarks>
        /// <param name="id">The ID of the project to retrieve.</param>
        /// <returns>Detailed project data.</returns>
        /// <response code="200">Project retrieved successfully.</response>
        /// <response code="400">Invalid request.</response>
        /// <response code="404">Project not found.</response>
        [HttpGet("{id}")]
        [Authorize(Roles = "Manager,Admin,Annotator,Reviewer")]
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
        /// Retrieves all projects managed by a specific manager.
        /// </summary>
        /// <remarks>
        /// Accessible by Managers and Admins.
        /// </remarks>
        /// <param name="managerId">The target manager's user ID.</param>
        /// <returns>A list of projects managed by the specified user.</returns>
        /// <response code="200">Projects retrieved successfully.</response>
        /// <response code="400">Failed to retrieve projects.</response>
        /// <response code="401">User is not authenticated or not authorized.</response>
        [HttpGet("managers/{managerId}")]
        [Authorize(Roles = "Manager,Admin")]
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
        /// Deletes a project from the system.
        /// </summary>
        /// <remarks>
        /// Accessible by Managers and Admins. This operation may be irreversible depending on the underlying service implementation.
        /// </remarks>
        /// <param name="id">The ID of the project to delete.</param>
        /// <returns>A confirmation message.</returns>
        /// <response code="200">Project deleted successfully.</response>
        /// <response code="400">Deletion failed (e.g., project has active assignments).</response>
        /// <response code="401">User is not authenticated or not authorized.</response>
        [HttpDelete("{id}")]
        [Authorize(Roles = "Manager,Admin")]
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
        /// Retrieves a comprehensive list of all projects in the system.
        /// </summary>
        /// <remarks>
        /// Accessible only by Admins. This endpoint provides an aggregated view of all projects including their status, progress, and member count across all managers.
        /// </remarks>
        /// <returns>A list of project summaries for the entire system.</returns>
        /// <response code="200">Projects retrieved successfully.</response>
        /// <response code="400">Failed to retrieve projects.</response>
        /// <response code="401">User is not authenticated or not authorized.</response>
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
    }
}