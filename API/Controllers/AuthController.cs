using BLL.Interfaces;
using Core.Constants;
using Core.DTOs.Requests;
using Core.DTOs.Responses;
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

        public AuthController(IUserService userService, IActivityLogService logService)
        {
            _userService = userService;
            _logService = logService;
        }

        /// <summary>
        /// Registers a new user account.
        /// </summary>
        /// <remarks>
        /// This endpoint creates a new user with a specific role
        /// (e.g., Annotator, Reviewer, Manager, Admin).
        /// </remarks>
        /// <param name="request">
        /// The registration request containing:
        /// - Full name
        /// - Email address
        /// - Password
        /// - Role
        /// </param>
        /// <returns>
        /// A confirmation message and the newly created user's unique identifier.
        /// </returns>
        /// <response code="200">User registered successfully.</response>
        /// <response code="400">
        /// Registration failed due to validation errors
        /// (e.g., email already exists or invalid role).
        /// </response>
        /// <response code="409">Email is already in use.</response>
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

        /// <summary>
        /// Authenticates a user and issues a JWT access token.
        /// </summary>
        /// <remarks>
        /// The returned JWT token must be included in the
        /// <c>Authorization</c> header as:
        /// <br />
        /// <c>Authorization: Bearer {token}</c>
        /// </remarks>
        /// <param name="request">
        /// The login request containing the user's email and password.
        /// </param>
        /// <returns>
        /// A JWT access token along with token metadata.
        /// </returns>
        /// <response code="200">Login successful and token issued.</response>
        /// <response code="400">Login request is invalid.</response>
        /// <response code="401">Authentication failed due to invalid credentials.</response>
        /// <response code="403">Account is deactivated or banned.</response>
        /// <response code="500">Internal server error.</response>
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
                var token = await _userService.LoginAsync(request.Email, request.Password);
                if (token == null)
                    return Unauthorized(new ErrorResponse { Message = "Invalid email or password." });

                var handler = new JwtSecurityTokenHandler();
                var jwtToken = handler.ReadJwtToken(token);
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

                return Ok(new
                {
                    Message = "Login successful.",
                    AccessToken = token,
                    TokenType = "Bearer"
                });
            }
            catch (ArgumentException)
            {
                return StatusCode(403, new ErrorResponse { Message = "Account is deactivated or banned." });
            }
            catch (Exception)
            {
                return StatusCode(500, new ErrorResponse { Message = "An error occurred during login. Please try again later." });
            }
        }

        /// <summary>
        /// Logs out the current user (Records action in Activity Log).
        /// </summary>
        /// <remarks>
        /// **IMPORTANT FOR FRONTEND:** /// Because this system uses JWT (which is stateless), calling this API will **NOT** invalidate the token on the server side.
        /// <br/>
        /// This API exists purely to record a "Logout" event in the database for auditing purposes. 
        /// <br/>
        /// **After receiving a 200 OK from this endpoint, the frontend MUST delete the token from LocalStorage/Cookies to actually log the user out.**
        /// </remarks>
        /// <returns>
        /// A message confirming the logout action was logged.
        /// </returns>
        /// <response code="200">Logout logged successfully. Frontend must now clear the token.</response>
        /// <response code="401">Unauthorized. Valid JWT token is missing in the header.</response>
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
                    await _logService.LogActionAsync(
                        userId,
                        "Logout",
                        "User",
                        userId,
                        "User logged out of the system."
                    );
                }

                return Ok(new { Message = "Logout successful. Please clear the token on the client side." });
            }
            catch (Exception)
            {
                return StatusCode(500, new ErrorResponse { Message = "An error occurred during logout." });
            }
        }
        /// <summary>
        /// Reset password (used for the Forgot Password feature on the login screen).
        /// </summary>
        /// <remarks>
        /// The system will generate a new random password.  
        /// </remarks>
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
                    Message = "Password has been reset successfully.",
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