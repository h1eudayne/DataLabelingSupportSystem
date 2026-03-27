using BLL.Interfaces;
using Core.DTOs.Requests;
using Core.DTOs.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace API.Controllers
{
    
    
    
    
    [Route("api/reviews")]
    [ApiController]
    [Authorize]
    [Tags("5. Review & QA")]
    public class ReviewController : ControllerBase
    {
        private readonly IReviewService _reviewService;
        private readonly IStatisticService _statisticService;

        public ReviewController(IReviewService reviewService, IStatisticService statisticService)
        {
            _reviewService = reviewService;
            _statisticService = statisticService;
        }

        
        
        

        
        
        
        
        
        
        
        
        
        
        [HttpGet("projects")]
        [Authorize(Roles = "Admin,Manager,Reviewer")]
        [ProducesResponseType(typeof(IEnumerable<AssignedProjectResponse>), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> GetReviewerProjects()
        {
            var reviewerId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(reviewerId)) return Unauthorized();

            try
            {
                var projects = await _reviewService.GetReviewerProjectsAsync(reviewerId);
                return Ok(projects);
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }

        
        
        
        
        
        
        
        
        
        
        
        [HttpGet("projects/{projectId}/tasks")]
        [Authorize(Roles = "Admin,Manager,Reviewer,Annotator")]
        [ProducesResponseType(typeof(IEnumerable<TaskResponse>), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> GetTasksForReview(int projectId)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            try
            {
                var tasks = await _reviewService.GetTasksForReviewAsync(projectId, userId);
                return Ok(tasks);
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }

        
        
        

        
        
        
        
        
        
        
        
        
        
        
        [HttpPost]
        [Authorize(Roles = "Admin,Manager,Reviewer")]
        [ProducesResponseType(typeof(object), 200)] 
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> ReviewTask([FromBody] ReviewRequest request)
        {
            var reviewerId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(reviewerId)) return Unauthorized();

            try
            {
                await _reviewService.ReviewAssignmentAsync(reviewerId, request);
                return Ok(new
                {
                    Message = request.IsApproved ? "Approved" : "Rejected"
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }

        
        
        

        
        
        
        
        
        
        
        
        
        
        
        
        [HttpPost("audits")]
        [Authorize(Roles = "Manager")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> AuditReview([FromBody] AuditReviewRequest request)
        {
            var managerId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(managerId)) return Unauthorized();

            try
            {
                await _reviewService.AuditReviewAsync(managerId, request);
                return Ok(new { Message = "Audit submitted successfully." });
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }

        
        
        

        
        
        
        
        
        
        
        [HttpGet("stats")]
        [Authorize(Roles = "Admin,Manager,Reviewer")]
        [ProducesResponseType(typeof(ReviewerStatsResponse), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> GetReviewerStats()
        {
            var reviewerId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(reviewerId)) return Unauthorized();

            try
            {
                var stats = await _statisticService.GetReviewerStatsAsync(reviewerId);
                return Ok(stats);
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }

        
        
        

        
        
        
        
        
        
        
        
        
        [HttpGet("projects/{projectId}/queue")]
        [Authorize(Roles = "Admin,Manager,Reviewer")]
        [ProducesResponseType(typeof(ReviewQueueResponse), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> GetReviewQueueGroupedByAnnotator(int projectId)
        {
            var reviewerId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(reviewerId)) return Unauthorized();

            try
            {
                var queue = await _reviewService.GetReviewQueueGroupedByAnnotatorAsync(projectId, reviewerId);
                return Ok(queue);
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }

        
        
        

        
        
        
        
        
        
        
        
        [HttpPost("deduct-overdue-reliability")]
        [Authorize(Roles = "Manager,Admin")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> DeductOverdueReliabilityScores()
        {
            try
            {
                await _statisticService.DeductReliabilityScoreForOverdueTasksAsync();
                return Ok(new { Message = "Reliability scores have been deducted for overdue tasks." });
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }

        
        
        

        
        
        
        
        
        
        
        
        
        [HttpGet("projects/{projectId}/batch-status")]
        [Authorize(Roles = "Admin,Manager,Reviewer")]
        [ProducesResponseType(typeof(BatchCompletionStatusResponse), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> GetBatchCompletionStatus(int projectId)
        {
            var reviewerId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(reviewerId)) return Unauthorized();

            try
            {
                var status = await _reviewService.GetBatchCompletionStatusAsync(projectId, reviewerId);
                return Ok(status);
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }
    }
}