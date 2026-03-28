namespace Core.Entities
{
    public class Project
    {
        public int Id { get; set; }
        public string ManagerId { get; set; }
        public virtual User Manager { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public DateTime Deadline { get; set; }
        public DateTime CreatedDate { get; set; }
        public string AllowGeometryTypes { get; set; }
        public string AnnotationGuide { get; set; }
        public int MaxTaskDurationHours { get; set; } = 24;
        public string GuidelineVersion { get; set; } = "1.0";
        public bool RequireConsensus { get; set; } = false;
        public string Status { get; set; } = "Draft";
        public int PenaltyUnit { get; set; } = 10;

        
        public int SamplingRate { get; set; } = 100; 

        public virtual ICollection<ReviewChecklistItem> ChecklistItems { get; set; } = new List<ReviewChecklistItem>();

        public virtual ICollection<LabelClass> LabelClasses { get; set; } = new List<LabelClass>();
        public virtual ICollection<DataItem> DataItems { get; set; } = new List<DataItem>();
        
        public virtual ICollection<ProjectFlag> ProjectFlags { get; set; } = new List<ProjectFlag>();
    }
}