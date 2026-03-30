using Core.Constants;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Entities
{
    public class GlobalUserBanRequest
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string TargetUserId { get; set; } = string.Empty;

        [ForeignKey("TargetUserId")]
        public virtual User TargetUser { get; set; } = null!;

        [Required]
        public string RequestedByAdminId { get; set; } = string.Empty;

        [ForeignKey("RequestedByAdminId")]
        public virtual User RequestedByAdmin { get; set; } = null!;

        [Required]
        public string ManagerId { get; set; } = string.Empty;

        [ForeignKey("ManagerId")]
        public virtual User Manager { get; set; } = null!;

        [Required]
        public string Status { get; set; } = GlobalUserBanRequestStatusConstants.Pending;

        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ResolvedAt { get; set; }

        public string? DecisionNote { get; set; }
    }
}
