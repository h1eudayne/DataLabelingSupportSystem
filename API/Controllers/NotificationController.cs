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
        /// GetMyNotifications endpoint.
        /// </summary>
        /// <returns>An IActionResult representing the operation outcome.</returns>
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
        /// MarkAsRead endpoint.
        /// </summary>
        /// <param name="id">The id.</param>
        /// <returns>An IActionResult representing the operation outcome.</returns>
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
        /// MarkAllAsRead endpoint.
        /// </summary>
        /// <returns>An IActionResult representing the operation outcome.</returns>
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