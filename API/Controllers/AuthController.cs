using BLL.Interfaces;
using Core.Constants;
using Core.DTOs.Requests;
using Core.DTOs.Responses;
using DAL;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace API.Controllers
{
    [Route("api/auth")]
    [ApiController]
    [Tags("1. Authentication")]
    public class AuthController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly IActivityLogService _logService;
        private readonly ApplicationDbContext _context;

        public AuthController(IUserService userService, IActivityLogService logService, ApplicationDbContext context)
        {
            _userService = userService;
            _logService = logService;
            _context = context;
        }

        [HttpPost("login")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        [ProducesResponseType(typeof(ErrorResponse), 403)]
        [ProducesResponseType(typeof(ErrorResponse), 500)]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            try
            {
                var (accessToken, refreshToken) = await _userService.LoginAsync(request.Email, request.Password);
                if (accessToken == null || refreshToken == null)
                    return Unauthorized(new ErrorResponse { Message = "Invalid email or password." });

                var handler = new JwtSecurityTokenHandler();
                var jwtToken = handler.ReadJwtToken(accessToken);
                var userId = jwtToken.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;

                if (!string.IsNullOrEmpty(userId))
                {
                    await _logService.LogActionAsync(
                        userId,
                        "Login",
                        "User",
                        userId,
                        "User logged into the system."
                    );
                }

                int unreadCount = 0;
                if (!string.IsNullOrEmpty(userId))
                {
                    unreadCount = _context.AppNotifications
                        .Count(n => n.UserId == userId && !n.IsRead);
                }

                return Ok(new
                {
                    Message = "Login successful.",
                    accessToken = accessToken,
                    refreshToken = refreshToken,
                    tokenType = "Bearer",
                    expiresIn = 1800,
                    unreadNotifications = unreadCount
                });
            }
            catch (ArgumentException)
            {
                return StatusCode(403, new ErrorResponse { Message = "Account is deactivated or banned." });
            }
            catch (InvalidOperationException ex)
            {
                return StatusCode(400, new ErrorResponse { Message = ex.Message });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Login Error] {ex}");
                return StatusCode(500, new ErrorResponse { Message = "An error occurred during login. Please try again later." });
            }
        }

        [HttpPost("refresh-token")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
        {
            try
            {
                var (accessToken, refreshToken) = await _userService.RefreshTokenAsync(request.RefreshToken);
                if (accessToken == null || refreshToken == null)
                    return Unauthorized(new ErrorResponse { Message = "Invalid or expired refresh token." });

                return Ok(new
                {
                    Message = "Token refreshed successfully.",
                    accessToken = accessToken,
                    refreshToken = refreshToken,
                    tokenType = "Bearer",
                    expiresIn = 1800
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RefreshToken Error] {ex}");
                return StatusCode(500, new ErrorResponse { Message = "An error occurred during token refresh. Please try again later." });
            }
        }

        [HttpPost("logout")]
        [Authorize]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> Logout()
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                if (!string.IsNullOrEmpty(userId))
                {
                    await _userService.RevokeRefreshTokenAsync(userId);

                    await _logService.LogActionAsync(
                        userId,
                        "Logout",
                        "User",
                        userId,
                        "User logged out of the system. All refresh tokens revoked."
                    );
                }

                return Ok(new { Message = "Logout successful. All tokens have been invalidated." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Logout Error] {ex}");
                return StatusCode(500, new ErrorResponse { Message = "An error occurred during logout." });
            }
        }

        [HttpPost("forgot-password")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
        {
            try
            {
                var newPassword = await _userService.ForgotPasswordAsync(request.Email);
                return Ok(new
                {
                    Message = "A new password has been generated and sent to your email. Please check your inbox and use it to login.",
                    NewPassword = newPassword
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }
    }
}
