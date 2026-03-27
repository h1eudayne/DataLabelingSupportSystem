using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DAL;
using System.Security.Claims;

namespace API.Controllers
{
    [Route("api/notifications")]
    [ApiController]
    [Authorize]
    [Tags("5. Notifications")]
    public class NotificationController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public NotificationController(ApplicationDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Retrieves the latest notifications for the currently authenticated user.
        /// </summary>
        /// <remarks>
        /// Fetches up to 50 of the most recent notifications ordered by creation date.
        /// </remarks>
        /// <returns>A list of the user's recent notifications.</returns>
        /// <response code="200">Successfully retrieved the notifications.</response>
        /// <response code="401">User is not authenticated.</response>
        [HttpGet]
        [ProducesResponseType(200)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> GetMyNotifications()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var notifications = await _context.AppNotifications
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedAt)
                .Take(50)
                .Select(n => new
                {
                    n.Id,
                    n.Message,
                    n.Type,
                    n.IsRead,
                    n.CreatedAt
                })
                .ToListAsync();

            return Ok(notifications);
        }

        /// <summary>
        /// Marks a specific notification as read.
        /// </summary>
        /// <param name="id">The ID of the notification to mark as read.</param>
        /// <returns>A success message indicating the notification was updated.</returns>
        /// <response code="200">Notification marked as read successfully.</response>
        /// <response code="404">Notification not found or does not belong to the user.</response>
        [HttpPut("{id}/read")]
        [ProducesResponseType(200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> MarkAsRead(int id)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            var notif = await _context.AppNotifications
                .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);

            if (notif == null) return NotFound(new { message = "Notification not found." });

            notif.IsRead = true;
            await _context.SaveChangesAsync();

            return Ok(new { message = "Marked as read successfully." });
        }

        /// <summary>
        /// Marks all unread notifications for the current user as read.
        /// </summary>
        /// <returns>A success message indicating all notifications were updated.</returns>
        /// <response code="200">All unread notifications marked as read.</response>
        [HttpPut("read-all")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> MarkAllAsRead()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            var unreadNotifs = await _context.AppNotifications
                .Where(n => n.UserId == userId && !n.IsRead)
                .ToListAsync();

            foreach (var notif in unreadNotifs)
            {
                notif.IsRead = true;
            }

            await _context.SaveChangesAsync();

            return Ok(new { message = "All notifications marked as read." });
        }
    }
}