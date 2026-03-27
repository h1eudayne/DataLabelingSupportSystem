using System.ComponentModel.DataAnnotations;

namespace Core.Entities
{
    public class RefreshToken
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [Required]
        public string Token { get; set; } = string.Empty;

        public DateTime ExpiresAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? RevokedAt { get; set; }

        public bool IsExpired => DateTime.UtcNow >= ExpiresAt;

        public bool IsActive => !IsExpired && RevokedAt == null;

        public virtual User? User { get; set; }
    }
}
