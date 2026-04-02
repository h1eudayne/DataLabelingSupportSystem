using BLL.Interfaces;
using Core.Constants;
using Core.DTOs.Requests;
using Core.DTOs.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
        private readonly IAppNotificationService _notificationService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            IUserService userService,
            IActivityLogService logService,
            IAppNotificationService notificationService,
            ILogger<AuthController> logger)
        {
            _userService = userService;
            _logService = logService;
            _notificationService = notificationService;
            _logger = logger;
        }

        [HttpPost("register")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 409)]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            try
            {
                await _userService.RegisterAsync(request.FullName, request.Email, request.Password, UserRoles.Annotator);
                return Ok(new { Message = "Registration successful." });
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("Email already exists"))
                    return Conflict(new ErrorResponse { Message = "Email is already in use. Please use a different email." });
                return BadRequest(new ErrorResponse { Message = "Registration failed. Please check your information and try again." });
            }
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

                var authenticatedUser = await _userService.GetUserByEmailAsync(request.Email);
                var userId = authenticatedUser?.Id;

                int unreadCount = 0;

                if (!string.IsNullOrEmpty(userId))
                {
                    try
                    {
                        await _logService.LogActionAsync(
                            userId,
                            "Login",
                            "User",
                            userId,
                            "User logged into the system."
                        );

                        unreadCount = await _notificationService.GetUnreadCountAsync(userId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Login side-effects failed for {Email}", request.Email);
                    }
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
            catch (UnauthorizedAccessException)
            {
                return StatusCode(403, new ErrorResponse { Message = "Account is deactivated or banned." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Login failed for {Email}", request.Email);
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
            catch (Exception)
            {
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
            catch (Exception)
            {
                return StatusCode(500, new ErrorResponse { Message = "An error occurred during logout." });
            }
        }

        [HttpPost("forgot-password")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Email))
            {
                return BadRequest(new ErrorResponse { Message = "Email is required." });
            }

            try
            {
                var resultMessage = await _userService.ForgotPasswordAsync(request.Email);
                return Ok(new { Message = resultMessage });
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }
    }
}
