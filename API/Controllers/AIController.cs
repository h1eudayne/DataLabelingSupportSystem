using BLL.Interfaces;
using Core.DTOs.Requests;
using Core.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace API.Controllers
{
    /// <summary>
    /// AI-powered object detection endpoints using GECO2 few-shot counting model.
    /// Enables annotators to use AI-assisted labeling by providing 1-3 exemplar
    /// bounding boxes, after which the AI detects all similar objects in the image.
    /// </summary>
    [Route("api/ai")]
    [ApiController]
    [Authorize]
    public class AIController : ControllerBase
    {
        private readonly IAIService _aiService;

        /// <summary>
        /// Initializes the AI controller with the AI detection service.
        /// </summary>
        public AIController(IAIService aiService)
        {
            _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        }

        /// <summary>
        /// Detects objects in an image using few-shot AI detection (GECO2).
        /// The user provides an image URL and 1-3 exemplar bounding boxes;
        /// the AI locates all similar objects and returns the count + annotated image.
        /// </summary>
        /// <param name="request">Detection request with image URL and exemplar boxes.</param>
        /// <returns>Detection result with object count and annotated image URL.</returns>
        /// <response code="200">Detection completed successfully.</response>
        /// <response code="400">Invalid request parameters.</response>
        /// <response code="500">AI service error (HuggingFace Space unavailable).</response>
        [HttpPost("detect")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        [ProducesResponseType(StatusCodes.Status504GatewayTimeout)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DetectObjects([FromBody] AIDetectRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { message = "Invalid request.", errors = ModelState });

            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(userId))
                return Unauthorized();

            try
            {
                var result = await _aiService.DetectObjectsAsync(userId, request);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (AIServiceUnavailableException ex)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new { message = ex.Message });
            }
            catch (TimeoutException ex)
            {
                return StatusCode(StatusCodes.Status504GatewayTimeout,
                    new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = $"AI detection failed: {ex.Message}" });
            }
        }

        /// <summary>
        /// Checks whether the AI detection service (GECO2 HuggingFace Space) is currently available.
        /// Useful for showing service status in the UI before the user attempts detection.
        /// </summary>
        /// <returns>Service availability status.</returns>
        /// <response code="200">Service status retrieved.</response>
        [HttpGet("status")]
        [AllowAnonymous]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetServiceStatus()
        {
            var isAvailable = await _aiService.IsServiceAvailableAsync();
            return Ok(new
            {
                service = "GECO2 (Few-shot Object Detection)",
                provider = "HuggingFace Spaces",
                available = isAvailable,
                message = isAvailable
                    ? "AI service is ready for inference."
                    : "AI service is currently loading (cold start). Please wait 30-60 seconds.",
                note = isAvailable
                    ? "Health check only. Use /api/ai/detect or the AI Preview button to run detection."
                    : "Health check only. First request may take 30-60 seconds due to model loading."
            });
        }
    }
}
