using BLL.Interfaces;
using Core.DTOs.Requests;
using Core.DTOs.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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

        /// <summary>
        /// Create a new label for a project.
        /// </summary>
        /// <remarks>
        /// This API is used to create a new label. ProjectId is required.
        /// </remarks>
        /// <param name="request">Label information to create (Name, color, guideline, checklist...)</param>
        /// <returns>The newly created label information.</returns>
        /// <response code="200">Label created successfully.</response>
        /// <response code="400">Label name already exists in the project or invalid input data.</response>
        [HttpPost]
        [ProducesResponseType(typeof(LabelResponse), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        public async Task<IActionResult> CreateLabel([FromBody] CreateLabelRequest request)
        {
            try
            {
                var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId)) return Unauthorized();

                var result = await _labelService.CreateLabelAsync(userId, request);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }

        /// <summary>
        /// Update an existing label.
        /// </summary>
        /// <remarks>
        /// **IMPORTANT NOTE FOR FE:** If the user changes the Name or Guideline of the label, 
        /// the backend will automatically RESET all in-progress tasks that use this label.  
        /// It is recommended that FE calls the `usage-count` API beforehand to warn users.
        /// </remarks>
        /// <param name="id">ID of the label to update</param>
        /// <param name="request">Updated label information</param>
        /// <returns>The updated label information.</returns>
        [HttpPut("{id}")]
        [ProducesResponseType(typeof(LabelResponse), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        public async Task<IActionResult> UpdateLabel(int id, [FromBody] UpdateLabelRequest request)
        {
            try
            {
                var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId)) return Unauthorized();

                var result = await _labelService.UpdateLabelAsync(userId, id, request);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }

        /// <summary>
        /// Delete a label from the project.
        /// </summary>
        /// <remarks>
        /// **IMPORTANT NOTE FOR FE:** Deleting a label will cause the backend to RESET all tasks using this label.  
        /// It is recommended that FE calls the `usage-count` API first to display a confirmation popup before deletion.
        /// </remarks>
        /// <param name="id">ID of the label to delete</param>
        /// <returns>Deletion success message.</returns>
        [HttpDelete("{id}")]
        [ProducesResponseType(200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        public async Task<IActionResult> DeleteLabel(int id)
        {
            try
            {
                var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId)) return Unauthorized();

                await _labelService.DeleteLabelAsync(userId, id);
                return Ok(new { Message = "Label deleted successfully." });
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }

        /// <summary>
        /// Check how many tasks are currently using a specific label.
        /// </summary>
        /// <remarks>
        /// **For FE:** Call this API when the user clicks Edit/Delete.  
        /// If `UsageCount > 0`, FE must show a warning popup:  
        /// "This label is being used in X tasks. Editing/deleting will reset those tasks. Are you sure?"
        /// </remarks>
        /// <param name="id">ID of the label to check</param>
        /// <returns>The number of tasks using the label and a corresponding message.</returns>
        [HttpGet("{id}/usage-count")]
        [Authorize(Roles = "Manager,Admin")]
        public async Task<IActionResult> GetLabelUsageCount(int id)
        {
            try
            {
                var count = await _labelService.CheckLabelUsageAsync(id);
                return Ok(new
                {
                    LabelId = id,
                    UsageCount = count,
                    Message = count > 0
                        ? $"Warning: This label is currently being used in {count} tasks!"
                        : "This label is not currently in use and can be modified safely."
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }

        /// <summary>
        /// Get all labels of a project.
        /// </summary>
        /// <remarks>
        /// Returns the list of labels to be displayed in the labeling tool or label management screen.
        /// </remarks>
        /// <param name="projectId">Project ID to retrieve labels</param>
        /// <returns>List of labels belonging to the project.</returns>
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