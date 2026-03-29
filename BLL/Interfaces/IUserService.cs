using Core.DTOs.Requests;
using Core.DTOs.Responses;
using Core.Entities;
using Microsoft.AspNetCore.Http;

namespace BLL.Interfaces
{
    public interface IUserService
    {
        Task<User> RegisterAsync(string fullName, string email, string password, string role, string? managerId = null);
        Task<(string? accessToken, string? refreshToken)> LoginAsync(string email, string password);
        Task<(string? accessToken, string? refreshToken)> RefreshTokenAsync(string refreshToken);
        Task RevokeRefreshTokenAsync(string userId);
        Task<User?> GetUserByIdAsync(string id);
        Task<User?> GetUserByEmailAsync(string email);
        Task<bool> IsEmailExistsAsync(string email);
        Task ChangePasswordAsync(string userId, string oldPassword, string newPassword);
        Task UpdateProfileAsync(string userId, UpdateProfileRequest request);
        Task<PagedResponse<UserResponse>> GetAllUsersAsync(int page, int pageSize);
        Task UpdateUserAsync(string userId, string actorId, UpdateUserRequest request);
        Task DeleteUserAsync(string userId);
        Task<string> ForgotPasswordAsync(string email);
        Task AdminChangeUserPasswordAsync(string adminId, string targetUserId, string newPassword);
        Task<List<UserResponse>> GetManagementBoardAsync();
        Task ToggleUserStatusAsync(string userId, bool isActive, string? adminId = null);
        Task UpdateAvatarAsync(string userId, string avatarUrl);
        Task<ImportUserResponse> ImportUsersFromExcelAsync(Stream fileStream, string adminId);
        Task<List<UserResponse>> GetManagedUsersAsync(string managerId);
        Task<List<UserResponse>> GetAllUsersNoPagingAsync();
    }
}
