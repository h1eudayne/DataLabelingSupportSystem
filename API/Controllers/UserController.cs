using BLL.Interfaces;
using Core.DTOs.Requests;
using Core.DTOs.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace API.Controllers
{
    
    
    
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
            if (string.IsNullOrEmpty(adminId)) return Unauthorized();

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

        
        
        
        
        
        
        
        
        
        [HttpGet("all")]
        [Authorize(Roles = "Admin")]
        [ProducesResponseType(typeof(List<UserResponse>), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> GetAllUsersNoPaging()
        {
            var users = await _userService.GetAllUsersNoPagingAsync();
            return Ok(users);
        }

        
        
        
        
        
        
        
        
        
        
        
        [HttpGet]
        [Authorize(Roles = "Admin,Manager")]
        [ProducesResponseType(typeof(PagedResponse<UserResponse>), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        public async Task<IActionResult> GetAllUsers([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        {
            var result = await _userService.GetAllUsersAsync(page, pageSize);
            return Ok(result);
        }

        
        
        
        
        
        
        
        
        
        
        
        
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

        
        
        
        
        
        
        
        
        
        
        
        
        [HttpPut("{id}/change-password")]
        [Authorize(Roles = "Admin")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(ErrorResponse), 400)]
        [ProducesResponseType(typeof(ErrorResponse), 401)]
        [ProducesResponseType(typeof(ErrorResponse), 404)]
        public async Task<IActionResult> AdminChangePassword(string id, [FromBody] AdminChangePasswordRequest request)
        {
            try
            {
                var adminId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(adminId)) return Unauthorized();

                if (string.IsNullOrEmpty(request.NewPassword))
                    return BadRequest(new ErrorResponse { Message = "New password cannot be empty." });

                await _userService.AdminChangeUserPasswordAsync(adminId, id, request.NewPassword);

                return Ok(new { Message = "Password has been successfully changed by Admin." });
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("User not found"))
                    return NotFound(new ErrorResponse { Message = ex.Message });

                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
        }

        
        
        
        
        
        
        
        
        
        
        
        
        
        
        
        
        
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
                var actorId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(actorId)) return Unauthorized();

                await _userService.UpdateUserAsync(id, actorId, request);
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
                var adminId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                await _userService.ToggleUserStatusAsync(id, isActive, adminId);
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