using System.ComponentModel.DataAnnotations;

namespace Core.Entities
{
    public class ProjectFlag
    {
        [Key]
        public int Id { get; set; }

        public int ProjectId { get; set; }
        public virtual Project? Project { get; set; }

        [Required]
        [MaxLength(100)]
        public string FlagType { get; set; } = string.Empty;

        [MaxLength(500)]
        public string Description { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public bool IsActive { get; set; } = true;
    }
}
