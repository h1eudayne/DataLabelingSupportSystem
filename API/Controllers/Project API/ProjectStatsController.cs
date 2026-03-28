using BLL.Interfaces;
using Core.DTOs.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers
{

    [Route("api/projects")]
    [ApiController]
    [Authorize]
    [Tags("3. Project Management")]
    public class ProjectStatsController : ControllerBase
    {
        private readonly IProjectService _projectService;

        public ProjectStatsController(IProjectService projectService)
        {
            _projectService = projectService;
        }

        /// <summary>
        /// GetProjectStatistics endpoint.
        /// </summary>
        /// <param name="projectId">The projectId.</param>
        /// <returns>An IActionResult representing the operation outcome.</returns>
        [HttpGet("{projectId}/statistics")]
        [Authorize(Roles = "Admin,Manager,Reviewer,Annotator")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> GetProjectStatistics(int projectId)
        {
            try
            {
                var stats = await _projectService.GetProjectStatisticsAsync(projectId);
                return Ok(stats);
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }

        /// <summary>
        /// GetManagerStats endpoint.
        /// </summary>
        /// <param name="managerId">The managerId.</param>
        /// <returns>An IActionResult representing the operation outcome.</returns>
        [HttpGet("managers/{managerId}/statistics")]
        [Authorize(Roles = "Admin,Manager")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> GetManagerStats(string managerId)
        {
            try
            {
                var stats = await _projectService.GetManagerStatsAsync(managerId);
                return Ok(stats);
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }

        /// <summary>
        /// ToggleUserLock endpoint.
        /// </summary>
        /// <param name="projectId">The projectId.</param>
        /// <param name="userId">The userId.</param>
        /// <param name="lockStatus">The lockStatus.</param>
        /// <returns>An IActionResult representing the operation outcome.</returns>
        [HttpPost("{projectId}/users/{userId}/toggle-lock")]
        [Authorize(Roles = "Manager")]
        public async Task<IActionResult> ToggleUserLock(int projectId, string userId, [FromQuery] bool lockStatus)
        {
            try
            {
                var managerId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(managerId)) return Unauthorized();

                await _projectService.ToggleUserLockAsync(projectId, userId, lockStatus, managerId);

                string action = lockStatus ? "locked" : "unlocked";
                return Ok(new { Message = $"Successfully {action} this user." });
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }
    }
}