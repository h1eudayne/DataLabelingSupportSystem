using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Core.DTOs.Requests
{
    public class RegisterRequest
    {
        [Required]
        [JsonPropertyName("fullName")]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [Required]
        [MinLength(6)]
        [JsonPropertyName("password")]
        public string Password { get; set; } = string.Empty;

        [JsonPropertyName("role")]
        public string Role { get; set; } = "Annotator";

        [JsonPropertyName("managerId")]
        public string? ManagerId { get; set; }
    }

    public class LoginRequest
    {
        [Required]
        [EmailAddress]
        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [Required]
        [JsonPropertyName("password")]
        public string Password { get; set; } = string.Empty;
    }

    public class UpdatePaymentRequest
    {
        [Required]
        [JsonPropertyName("bankName")]
        public string BankName { get; set; } = string.Empty;

        [Required]
        [JsonPropertyName("bankAccountNumber")]
        public string BankAccountNumber { get; set; } = string.Empty;

        [Required]
        [JsonPropertyName("taxCode")]
        public string TaxCode { get; set; } = string.Empty;
    }

    public class UpdateUserRequest
    {
        [JsonPropertyName("fullName")]
        public string? FullName { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("password")]
        public string? Password { get; set; }

        [JsonPropertyName("managerId")]
        public string? ManagerId { get; set; }
    }

    public class ChangePasswordRequest
    {
        [Required]
        [JsonPropertyName("oldPassword")]
        public string OldPassword { get; set; } = string.Empty;

        [Required]
        [MinLength(6)]
        [JsonPropertyName("newPassword")]
        public string NewPassword { get; set; } = string.Empty;
    }

    public class UpdateProfileRequest
    {
        [JsonPropertyName("fullName")]
        public string? FullName { get; set; }

        [JsonPropertyName("avatarUrl")]
        public string? AvatarUrl { get; set; }
    }

    public class ForgotPasswordRequest
    {
        [JsonPropertyName("email")]
        public string Email { get; set; } = null!;
    }

    public class AdminChangePasswordRequest
    {
        [JsonPropertyName("newPassword")]
        public string NewPassword { get; set; } = null!;
    }

    public class RefreshTokenRequest
    {
        [Required]
        [JsonPropertyName("refreshToken")]
        public string RefreshToken { get; set; } = string.Empty;
    }
}
