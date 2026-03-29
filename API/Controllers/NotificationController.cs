using BLL.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace API.Controllers
{
    [Route("api/notifications")]
    [ApiController]
    [Authorize]
    [Tags("5. Notifications")]
    public class NotificationController : ControllerBase
    {
        private readonly IAppNotificationService _notificationService;
        public NotificationController(IAppNotificationService notificationService)
        {
            _notificationService = notificationService;
        }
        [HttpGet]
        [ProducesResponseType(200)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> GetMyNotifications()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();
            try
            {
                var notifications = await _notificationService.GetMyNotificationsAsync(userId);
                return Ok(notifications);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
        [HttpPut("{id}/read")]
        [ProducesResponseType(200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> MarkAsRead(int id)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();
            try
            {
                await _notificationService.MarkAsReadAsync(id, userId);
                return Ok(new { message = "Marked as read successfully." });
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("not found")) return NotFound(new { message = ex.Message });
                return BadRequest(new { message = ex.Message });
            }
        }
        [HttpPut("read-all")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> MarkAllAsRead()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();
            try
            {
                await _notificationService.MarkAllAsReadAsync(userId);
                return Ok(new { message = "All notifications marked as read." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}