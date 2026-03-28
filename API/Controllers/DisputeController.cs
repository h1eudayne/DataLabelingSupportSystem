using BLL.Interfaces;
using Core.DTOs.Requests;
using Core.DTOs.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace API.Controllers
{
    [Route("api/disputes")]
    [ApiController]
    [Authorize]
    [Tags("6. Dispute & Logs")]
    public class DisputeController : ControllerBase
    {
        private readonly IDisputeService _disputeService;

        public DisputeController(IDisputeService disputeService)
        {
            _disputeService = disputeService;
        }

        [HttpPost]
        [Authorize(Roles = "Annotator")]
        [ProducesResponseType(200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> CreateDispute([FromBody] CreateDisputeRequest request)
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId)) return Unauthorized();

                var result = await _disputeService.CreateDisputeAsync(userId, request);
                return Ok(new { Message = "Dispute submitted successfully.", DisputeId = result.Id });
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

        [HttpPost("resolve")]
        [Authorize(Roles = "Manager")]
        [ProducesResponseType(200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> ResolveDispute([FromBody] ResolveDisputeRequest request)
        {
            try
            {
                var managerId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(managerId)) return Unauthorized();

                await _disputeService.ResolveDisputeAsync(managerId, request);
                return Ok(new { Message = "Dispute resolved." });
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }

        [HttpGet]
        [ProducesResponseType(200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> GetDisputes([FromQuery] int projectId)
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
                var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "";

                var disputes = await _disputeService.GetDisputesAsync(projectId, userId, role);
                return Ok(disputes);
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }
    }
}
