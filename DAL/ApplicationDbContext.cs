using Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace DAL
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<Project> Projects { get; set; }
        public DbSet<LabelClass> LabelClasses { get; set; }
        public DbSet<DataItem> DataItems { get; set; }
        public DbSet<Assignment> Assignments { get; set; }
        public DbSet<Annotation> Annotations { get; set; }
        public DbSet<ReviewLog> ReviewLogs { get; set; }
        public DbSet<UserProjectStat> UserProjectStats { get; set; }
        public DbSet<Dispute> Disputes { get; set; }
        public DbSet<ActivityLog> ActivityLogs { get; set; }
        public DbSet<ReviewChecklistItem> ReviewChecklistItems { get; set; }
        public DbSet<AppNotification> AppNotifications { get; set; }
        public DbSet<GlobalUserBanRequest> GlobalUserBanRequests { get; set; }
        public DbSet<RefreshToken> RefreshTokens { get; set; }

        public DbSet<ProjectFlag> ProjectFlags { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Project>()
                .HasOne(p => p.Manager)
                .WithMany(u => u.ManagedProjects)
                .HasForeignKey(p => p.ManagerId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Assignment>()
                .HasOne(a => a.Annotator)
                .WithMany(u => u.Assignments)
                .HasForeignKey(a => a.AnnotatorId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ReviewLog>()
                .HasOne(r => r.Reviewer)
                .WithMany(u => u.ReviewsGiven)
                .HasForeignKey(r => r.ReviewerId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<UserProjectStat>()
                .HasOne(s => s.User)
                .WithMany(u => u.ProjectStats)
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Assignment>()
                .HasOne(a => a.Project)
                .WithMany()
                .HasForeignKey(a => a.ProjectId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<Assignment>()
                .HasIndex(a => new { a.DataItemId, a.AnnotatorId, a.ReviewerId })
                .IsUnique()
                .HasDatabaseName("IX_Assignment_DataItem_Annotator_Reviewer");

            modelBuilder.Entity<Assignment>()
                .HasIndex(a => a.Status)
                .HasDatabaseName("IX_Assignment_Status");

            modelBuilder.Entity<Assignment>()
                .HasIndex(a => new { a.ProjectId, a.AnnotatorId, a.Status })
                .HasDatabaseName("IX_Assignment_Project_Annotator_Status");

            modelBuilder.Entity<RefreshToken>()
                .HasOne(rt => rt.User)
                .WithMany()
                .HasForeignKey(rt => rt.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<RefreshToken>()
                .HasIndex(rt => rt.Token)
                .IsUnique()
                .HasDatabaseName("IX_RefreshToken_Token");

            modelBuilder.Entity<RefreshToken>()
                .HasIndex(rt => rt.UserId)
                .HasDatabaseName("IX_RefreshToken_UserId");

            modelBuilder.Entity<GlobalUserBanRequest>()
                .HasOne(r => r.TargetUser)
                .WithMany()
                .HasForeignKey(r => r.TargetUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<GlobalUserBanRequest>()
                .HasOne(r => r.RequestedByAdmin)
                .WithMany()
                .HasForeignKey(r => r.RequestedByAdminId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<GlobalUserBanRequest>()
                .HasOne(r => r.Manager)
                .WithMany()
                .HasForeignKey(r => r.ManagerId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ProjectFlag>()
                .HasOne(pf => pf.Project)
                .WithMany(p => p.ProjectFlags)
                .HasForeignKey(pf => pf.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
