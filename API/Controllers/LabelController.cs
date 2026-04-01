using BLL.Interfaces;
using Core.DTOs.Requests;
using Core.DTOs.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Core.Constants;

namespace API.Controllers
{
    [Route("api/labels")]
    [ApiController]
    [Authorize]
    [Tags("4. Task & Annotation")]
    public class LabelController : ControllerBase
    {
        private readonly ILabelService _labelService;

        public LabelController(ILabelService labelService)
        {
            _labelService = labelService;
        }

        [HttpPost]
        [ProducesResponseType(typeof(LabelResponse), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 403)]
        public async Task<IActionResult> CreateLabel([FromBody] CreateLabelRequest request)
        {
            try
            {
                var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId)) return Unauthorized();

                var result = await _labelService.CreateLabelAsync(userId, request);
                return Ok(result);
            }
            catch (InvalidOperationException)
            {
                return BadRequest(new ErrorResponse { Message = "Label name already exists in this project." });
            }
            catch (Exception)
            {
                return BadRequest(new ErrorResponse { Message = "Unable to create the label right now. Please try again." });
            }
        }

        [HttpPut("{id}")]
        [ProducesResponseType(typeof(LabelResponse), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 403)]
        public async Task<IActionResult> UpdateLabel(int id, [FromBody] UpdateLabelRequest request)
        {
            try
            {
                var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId)) return Unauthorized();

                var result = await _labelService.UpdateLabelAsync(userId, id, request);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
            catch (Exception)
            {
                return BadRequest(new ErrorResponse { Message = "Unable to update the label right now. Please try again." });
            }
        }

        [HttpDelete("{id}")]
        [ProducesResponseType(200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 403)]
        public async Task<IActionResult> DeleteLabel(int id)
        {
            try
            {
                var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId)) return Unauthorized();

                await _labelService.DeleteLabelAsync(userId, id);
                return Ok(new { Message = "Label deleted successfully." });
            }
            catch (KeyNotFoundException ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
            catch (Exception)
            {
                return BadRequest(new ErrorResponse { Message = "Unable to delete the label right now. Please try again." });
            }
        }

        [HttpGet("{id}/usage-count")]
        [Authorize(Roles = "Manager,Admin")]
        public async Task<IActionResult> GetLabelUsageCount(int id)
        {
            try
            {
                var usageInfo = await _labelService.CheckLabelUsageAsync(id);
                return Ok(new
                {
                    LabelId = id,
                    UsageCount = usageInfo.UsageCount,
                    Message = usageInfo.WarningMessage
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }

        [HttpGet]
        [ProducesResponseType(typeof(List<LabelResponse>), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        public async Task<IActionResult> GetLabels([FromQuery] int projectId)
        {
            try
            {
                if (projectId <= 0) return BadRequest(new ErrorResponse { Message = "ProjectId is required." });

                var result = await _labelService.GetLabelsByProjectIdAsync(projectId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }
    }
}
