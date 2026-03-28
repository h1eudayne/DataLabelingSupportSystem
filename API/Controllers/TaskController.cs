using BLL.Interfaces;
using Core.DTOs.Requests;
using Core.DTOs.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace API.Controllers
{

    [Route("api/tasks")]
    [ApiController]
    [Authorize]
    [Tags("4. Task & Annotation")]
    public class TaskController : ControllerBase
    {
        private readonly ITaskService _taskService;

        public TaskController(ITaskService taskService)
        {
            _taskService = taskService;
        }

        /// <summary>
        /// AssignTeamTasks endpoint.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>An IActionResult representing the operation outcome.</returns>
        [HttpPost("assign-team")]
        [Authorize(Roles = "Manager")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        [ProducesResponseType(typeof(ErrorResponse), 403)]
        public async Task<IActionResult> AssignTeamTasks([FromBody] AssignTeamRequest request)
        {
            var managerId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(managerId)) return Unauthorized();

            try
            {
                await _taskService.AssignTeamAsync(managerId, request);
                return Ok(new { Message = "Tasks successfully distributed to the team." });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new ErrorResponse { Message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }

        /// <summary>
        /// GetTasksByBucket endpoint.
        /// </summary>
        /// <param name="projectId">The projectId.</param>
        /// <param name="bucketId">The bucketId.</param>
        /// <returns>An IActionResult representing the operation outcome.</returns>
        [HttpGet("projects/{projectId}/buckets/{bucketId}")]
        [Authorize(Roles = "Admin,Manager,Annotator")]
        [ProducesResponseType(typeof(object), 200)] 
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> GetTasksByBucket(int projectId, int bucketId)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            try
            {
                var tasks = await _taskService.GetTasksByBucketAsync(projectId, bucketId, userId);
                return Ok(tasks);
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }

        /// <summary>
        /// GetMyProjects endpoint.
        /// </summary>
        /// <returns>An IActionResult representing the operation outcome.</returns>
        [HttpGet("projects")]
        [ProducesResponseType(typeof(List<AssignedProjectResponse>), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> GetMyProjects()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var projects = await _taskService.GetAssignedProjectsAsync(userId);
            return Ok(projects);
        }

        /// <summary>
        /// SubmitMultipleTasks endpoint.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>An IActionResult representing the operation outcome.</returns>
        [HttpPost("submissions/batch")]
        [Authorize(Roles = "Annotator")]
        [ProducesResponseType(typeof(object), 200)] 
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> SubmitMultipleTasks([FromBody] SubmitMultipleTasksRequest request)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            if (request.AssignmentIds == null || !request.AssignmentIds.Any())
                return BadRequest(new ErrorResponse { Message = "Assignment list cannot be empty." });

            try
            {
                var result = await _taskService.SubmitMultipleTasksAsync(userId, request);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }

        /// <summary>
        /// GetProjectImages endpoint.
        /// </summary>
        /// <param name="projectId">The projectId.</param>
        /// <returns>An IActionResult representing the operation outcome.</returns>
        [HttpGet("projects/{projectId}/images")]
        [ProducesResponseType(typeof(List<AssignmentResponse>), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> GetProjectImages(int projectId)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            try
            {
                var images = await _taskService.GetTaskImagesAsync(projectId, userId);
                return Ok(images);
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }

        /// <summary>
        /// JumpToImage endpoint.
        /// </summary>
        /// <param name="projectId">The projectId.</param>
        /// <param name="dataItemId">The dataItemId.</param>
        /// <returns>An IActionResult representing the operation outcome.</returns>
        [HttpGet("projects/{projectId}/items/{dataItemId}")]
        [ProducesResponseType(typeof(AssignmentResponse), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> JumpToImage(int projectId, int dataItemId)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            try
            {
                var result = await _taskService.JumpToDataItemAsync(projectId, dataItemId, userId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }

        /// <summary>
        /// GetSingleAssignment endpoint.
        /// </summary>
        /// <param name="id">The id.</param>
        /// <returns>An IActionResult representing the operation outcome.</returns>
        [HttpGet("assignments/{id}")]
        [ProducesResponseType(typeof(AssignmentResponse), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> GetSingleAssignment(int id)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            try
            {
                var assignment = await _taskService.GetAssignmentByIdAsync(id, userId);
                return Ok(assignment);
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }

        /// <summary>
        /// SaveDraft endpoint.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>An IActionResult representing the operation outcome.</returns>
        [HttpPut("drafts")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> SaveDraft([FromBody] SubmitAnnotationRequest request)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            try
            {
                await _taskService.SaveDraftAsync(userId, request);
                return Ok(new { Message = "Draft saved successfully." });
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }

        /// <summary>
        /// SubmitTask endpoint.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>An IActionResult representing the operation outcome.</returns>
        [HttpPost("submissions")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> SubmitTask([FromBody] SubmitAnnotationRequest request)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            try
            {
                await _taskService.SubmitTaskAsync(userId, request);
                return Ok(new { Message = "Task submitted successfully." });
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }
    }
}