using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Entities
{
    public class LabelClass
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(7)]
        public string Color { get; set; } = "#000000";

        public string? GuideLine { get; set; }
        public string? ExampleImageUrl { get; set; }
        public string? DefaultChecklist { get; set; }

        public bool IsDefault { get; set; } = false;

        public int ProjectId { get; set; }

        [ForeignKey("ProjectId")]
        public virtual Project? Project { get; set; }

        [MaxLength(20)]
        public string Version { get; set; } = "1.0";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public virtual ICollection<Annotation> Annotations { get; set; } = new List<Annotation>();
    }
}
