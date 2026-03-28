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

        /// <summary>
        /// CreateDispute endpoint.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>An IActionResult representing the operation outcome.</returns>
        [HttpPost]
        [Authorize(Roles = "Annotator")]
        [ProducesResponseType(200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> CreateDispute([FromBody] CreateDisputeRequest request)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            await _disputeService.CreateDisputeAsync(userId, request);
            return Ok(new { Message = "Dispute submitted successfully." });
        }

        /// <summary>
        /// ResolveDispute endpoint.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>An IActionResult representing the operation outcome.</returns>
        [HttpPost("resolve")]
        [Authorize(Roles = "Manager")]
        [ProducesResponseType(200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> ResolveDispute([FromBody] ResolveDisputeRequest request)
        {
            var managerId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(managerId)) return Unauthorized();

            await _disputeService.ResolveDisputeAsync(managerId, request);
            return Ok(new { Message = "Dispute resolved." });
        }

        /// <summary>
        /// GetDisputes endpoint.
        /// </summary>
        /// <param name="projectId">The projectId.</param>
        /// <returns>An IActionResult representing the operation outcome.</returns>
        [HttpGet]
        [ProducesResponseType(200)] 
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> GetDisputes([FromQuery] int projectId)
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var role = User.FindFirst(ClaimTypes.Role)?.Value;

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