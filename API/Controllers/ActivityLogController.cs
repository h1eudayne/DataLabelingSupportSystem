using BLL.Interfaces;
using Core.DTOs.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers
{
    [Route("api/logs")]
    [ApiController]
    [Authorize]
    [Tags("6. Dispute & Logs")]
    public class ActivityLogController : ControllerBase
    {
        private readonly IActivityLogService _logService;

        public ActivityLogController(IActivityLogService logService)
        {
            _logService = logService;
        }

        [HttpGet("system")]
        [Authorize(Roles = "Admin")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> GetSystemLogs()
        {
            try
            {
                var logs = await _logService.GetSystemLogsAsync();
                return Ok(logs);
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }

        [HttpGet("projects/{projectId}")]
        [Authorize(Roles = "Admin,Manager")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> GetProjectLogs(int projectId)
        {
            try
            {
                var logs = await _logService.GetProjectLogsAsync(projectId);
                return Ok(logs);
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }
    }
}
