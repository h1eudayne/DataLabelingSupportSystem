using BLL.Interfaces;
using Core.DTOs.Requests;
using Core.DTOs.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace API.Controllers
{
    /// <summary>
    /// Controller for managing user profiles and user administration.
    /// </summary>
    [Route("api/users")]
    [ApiController]
    [Authorize]
    [Tags("2. User Management")]
    public class UserController : ControllerBase
    {
        private readonly IUserService _userService;

        public UserController(IUserService userService)
        {
            _userService = userService;
        }

        // ======================================================
        // CURRENT USER (SELF-SERVICE)
        // ======================================================

        /// <summary>
        /// Retrieves the profile of the currently logged-in user.
        /// </summary>
        /// <remarks>
        /// Returns basic user information such as ID, Full Name, Email, Role, and Avatar URL.
        /// </remarks>
        /// <returns>User profile data.</returns>
        /// <response code="200">Profile retrieved successfully.</response>
        /// <response code="401">User is not authenticated.</response>
        /// <response code="404">User not found.</response>
        [HttpGet("me")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> GetMyProfile()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userService.GetUserByIdAsync(userId);
            if (user == null) return NotFound(new ErrorResponse { Message = "User not found." });

            return Ok(new
            {
                user.Id,
                user.FullName,
                user.Email,
                user.Role,
                user.AvatarUrl
            });
        }

        /// <summary>
        /// Updates the profile information of the current user.
        /// </summary>
        /// <remarks>
        /// Allows updating the full name and avatar URL.
        /// </remarks>
        /// <param name="request">Profile update payload containing new name and/or avatar.</param>
        /// <returns>A success message.</returns>
        /// <response code="200">Profile updated successfully.</response>
        /// <response code="400">Update failed due to invalid data.</response>
        /// <response code="401">User is not authenticated.</response>
        [HttpPut("me")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            try
            {
                await _userService.UpdateProfileAsync(userId, request);
                return Ok(new { Message = "Profile updated successfully." });
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }

        /// <summary>
        /// Uploads an avatar image for the current user.
        /// </summary>
        /// <remarks>
        /// Saves the image to the server and updates the user's AvatarUrl.
        /// </remarks>
        /// <param name="file">The image file to upload.</param>
        /// <returns>A success message and the new Avatar URL.</returns>
        /// <response code="200">Avatar uploaded successfully.</response>
        /// <response code="400">No file selected or invalid file format.</response>
        /// <response code="401">User is not authenticated.</response>
        /// <response code="500">Internal server error during file upload.</response>
        [HttpPost("me/avatar")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        [ProducesResponseType(typeof(ErrorResponse), 500)]
        public async Task<IActionResult> UploadAvatar(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new ErrorResponse { Message = "Please select an image file." });

            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            try
            {
                var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "avatars");
                if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

                var fileExtension = Path.GetExtension(file.FileName);
                var uniqueFileName = $"{Guid.NewGuid()}{fileExtension}";
                var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(fileStream);
                }

                var avatarUrl = $"/avatars/{uniqueFileName}";
                await _userService.UpdateAvatarAsync(userId, avatarUrl);

                return Ok(new { Message = "Avatar uploaded successfully.", AvatarUrl = avatarUrl });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new ErrorResponse { Message = "Server error: " + ex.Message });
            }
        }

        // ======================================================
        // ACCOUNT SECURITY
        // ======================================================

        /// <summary>
        /// Changes the password of the current user.
        /// </summary>
        /// <param name="request">Payload containing the old and new passwords.</param>
        /// <returns>A success message.</returns>
        /// <response code="200">Password changed successfully.</response>
        /// <response code="400">Password change failed (e.g., incorrect old password).</response>
        /// <response code="401">User is not authenticated.</response>
        [HttpPut("me/password")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            try
            {
                await _userService.ChangePasswordAsync(
                    userId,
                    request.OldPassword,
                    request.NewPassword
                );
                return Ok(new { Message = "Password changed successfully." });
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }

        // ======================================================
        // ADMIN / MANAGER MANAGEMENT
        // ======================================================

        /// <summary>
        /// Imports users from an uploaded Excel file.
        /// </summary>
        /// <remarks>
        /// Admin only. Reads an .xlsx file and batch-creates user accounts (Annotators and Reviewers).
        /// </remarks>
        /// <param name="file">The Excel file (.xlsx) containing user data.</param>
        /// <returns>A summary of successful and failed imports.</returns>
        /// <response code="200">Users imported successfully.</response>
        /// <response code="400">Invalid file format or missing file.</response>
        /// <response code="401">User is not authorized.</response>
        [HttpPost("import")]
        [Authorize(Roles = "Admin")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> ImportUsers(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new ErrorResponse { Message = "Please attach an Excel file." });

            if (!file.FileName.EndsWith(".xlsx"))
                return BadRequest(new ErrorResponse { Message = "The system only supports Excel files (.xlsx)." });

            var adminId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            try
            {
                var result = await _userService.ImportUsersFromExcelAsync(file, adminId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }

        /// <summary>
        /// Retrieves users managed by the current Manager.
        /// </summary>
        /// <remarks>
        /// Returns only users whose ManagerId matches the current user's ID.
        /// Used by Managers to populate assignment dropdowns with relevant team members only.
        /// </remarks>
        /// <returns>A list of managed users.</returns>
        /// <response code="200">Managed users retrieved successfully.</response>
        /// <response code="401">User is not authenticated or not a Manager.</response>
        [HttpGet("managed")]
        [Authorize(Roles = "Manager")]
        [ProducesResponseType(typeof(List<UserResponse>), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> GetManagedUsers()
        {
            var managerId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(managerId)) return Unauthorized();

            var users = await _userService.GetManagedUsersAsync(managerId);
            return Ok(users);
        }

        /// <summary>
        /// Retrieves all users without pagination (Admin only).
        /// </summary>
        /// <remarks>
        /// This is used for extracting a full list of personnel in the system.
        /// </remarks>
        /// <returns>A complete list of all users.</returns>
        /// <response code="200">All users retrieved successfully.</response>
        /// <response code="401">User is not authorized.</response>
        [HttpGet("all")]
        [Authorize(Roles = "Admin")]
        [ProducesResponseType(typeof(List<UserResponse>), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> GetAllUsersNoPaging()
        {
            var users = await _userService.GetAllUsersNoPagingAsync();
            return Ok(users);
        }

        /// <summary>
        /// Retrieves a paginated list of all users.
        /// </summary>
        /// <remarks>
        /// Accessible by Admin and Manager roles. Includes statistics like total projects for each user.
        /// </remarks>
        /// <param name="page">The page number (default is 1).</param>
        /// <param name="pageSize">The number of items per page (default is 10).</param>
        /// <returns>A paginated list of users.</returns>
        /// <response code="200">Users retrieved successfully.</response>
        /// <response code="401">User is not authorized.</response>
        [HttpGet]
        [Authorize(Roles = "Admin,Manager")]
        [ProducesResponseType(typeof(PagedResponse<UserResponse>), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> GetAllUsers([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        {
            var result = await _userService.GetAllUsersAsync(page, pageSize);
            return Ok(result);
        }

        /// <summary>
        /// Creates a new user account manually.
        /// </summary>
        /// <remarks>
        /// Admin only.
        /// </remarks>
        /// <param name="request">User registration data.</param>
        /// <returns>A success message and the newly created User ID.</returns>
        /// <response code="200">User created successfully.</response>
        /// <response code="400">User creation failed (e.g., invalid data).</response>
        /// <response code="401">User is not authorized.</response>
        /// <response code="409">Email already exists.</response>
        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        [ProducesResponseType(typeof(ErrorResponse), 409)]
        public async Task<IActionResult> CreateUser([FromBody] RegisterRequest request)
        {
            try
            {
                var user = await _userService.RegisterAsync(
                    request.FullName,
                    request.Email,
                    request.Password,
                    request.Role,
                    request.ManagerId
                );

                return Ok(new
                {
                    Message = "User created successfully.",
                    UserId = user.Id
                });
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("Email already exists"))
                    return Conflict(new ErrorResponse { Message = ex.Message });

                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }

        /// <summary>
        /// Updates an existing user's details.
        /// </summary>
        /// <remarks>
        /// Admin only. Allows changing name, email, role, and manager assignment.
        /// </remarks>
        /// <param name="id">The target user ID.</param>
        /// <param name="request">Update payload.</param>
        /// <returns>A success message.</returns>
        /// <response code="200">User updated successfully.</response>
        /// <response code="400">Update failed due to validation rules or pending tasks.</response>
        /// <response code="401">User is not authorized.</response>
        /// <response code="404">User not found.</response>
        /// <response code="409">Email already exists.</response>
        [HttpPut("{id}")]
        [Authorize(Roles = "Admin")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        [ProducesResponseType(typeof(ErrorResponse), 409)]
        public async Task<IActionResult> UpdateUser(string id, [FromBody] UpdateUserRequest request)
        {
            try
            {
                await _userService.UpdateUserAsync(id, request);
                return Ok(new { Message = "User updated successfully." });
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("User not found"))
                    return NotFound(new ErrorResponse { Message = ex.Message });
                if (ex.Message.Contains("Email already exists"))
                    return Conflict(new ErrorResponse { Message = ex.Message });

                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }

        /// <summary>
        /// Activates or deactivates a user account.
        /// </summary>
        /// <remarks>
        /// Admin only. A user with pending tasks cannot be deactivated.
        /// </remarks>
        /// <param name="id">The target user ID.</param>
        /// <param name="isActive">The desired account status boolean.</param>
        /// <returns>A success message indicating the new status.</returns>
        /// <response code="200">User status updated successfully.</response>
        /// <response code="400">Operation failed (e.g., user has pending tasks).</response>
        /// <response code="401">User is not authorized.</response>
        /// <response code="404">User not found.</response>
        [HttpPatch("{id}/status")]
        [Authorize(Roles = "Admin")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> ToggleUserStatus(string id, [FromQuery] bool isActive)
        {
            try
            {
                await _userService.ToggleUserStatusAsync(id, isActive);
                var status = isActive ? "activated" : "deactivated";
                return Ok(new { Message = $"User has been {status} successfully." });
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("User not found"))
                    return NotFound(new ErrorResponse { Message = ex.Message });
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }

        /// <summary>
        /// Deletes (deactivates) a user account.
        /// </summary>
        /// <remarks>
        /// Admin only. This is a soft delete that sets IsActive to false.
        /// </remarks>
        /// <param name="id">The target user ID.</param>
        /// <returns>A success message.</returns>
        /// <response code="200">User deleted successfully.</response>
        /// <response code="400">Deletion failed (e.g., user has unfinished tasks).</response>
        /// <response code="401">User is not authorized.</response>
        /// <response code="404">User not found.</response>
        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> DeleteUser(string id)
        {
            try
            {
                await _userService.DeleteUserAsync(id);
                return Ok(new { Message = "User has been deactivated." });
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("User not found"))
                    return NotFound(new ErrorResponse { Message = ex.Message });
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }

        /// <summary>
        /// Retrieves the Management Board (Admins and Managers).
        /// </summary>
        /// <remarks>
        /// Returns a list of users who have the role of 'Admin' or 'Manager', ordered by role.
        /// Used for dropdowns when assigning Managers to Projects or Users.
        /// </remarks>
        /// <returns>A list of management personnel.</returns>
        /// <response code="200">Management board retrieved successfully.</response>
        /// <response code="401">User is not authorized.</response>
        [HttpGet("management-board")]
        [Authorize(Roles = "Admin,Manager")]
        [ProducesResponseType(typeof(List<UserResponse>), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> GetManagementBoard()
        {
            var users = await _userService.GetManagementBoardAsync();
            return Ok(users);
        }
    }
}