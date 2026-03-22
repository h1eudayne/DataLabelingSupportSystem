using DAL;
using Core.Constants;
using Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace API
{
    public static class DataSeeder
    {
        public static async Task SeedData(IServiceProvider serviceProvider, bool isDevelopment)
        {
            using (var scope = serviceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                if (isDevelopment)
                {
                    await SeedSampleDataForTesting(context);
                }
            }
        }

        private static async Task SeedSampleDataForTesting(ApplicationDbContext context)
        {
            if (await context.Users.AnyAsync()) return;

            var adminId = "440816a8-8954-4557-a462-196471ce8b02";
            var managerId = "49179920-d356-46f9-bb64-64da9f6ef4ee";
            var annotatorId = "5c023639-82ed-448e-9415-fc2e86b2d325";
            var reviewerId = "59420220-7179-4029-92e8-54a7c7acb39d";

            var defaultPasswordHash = BCrypt.Net.BCrypt.HashPassword("123456");

            var users = new List<User>
            {
                new User { Id = adminId, FullName = "Admin User", Email = "admin@system.com", PasswordHash = BCrypt.Net.BCrypt.HashPassword("123456"), Role = UserRoles.Admin, IsActive = true },
                new User { Id = managerId, FullName = "Manager User", Email = "manager@system.com", PasswordHash = BCrypt.Net.BCrypt.HashPassword("123456"), Role = UserRoles.Manager, IsActive = true },
                new User { Id = annotatorId, FullName = "Annotator User", Email = "annotator@system.com", PasswordHash = BCrypt.Net.BCrypt.HashPassword("123456"), Role = UserRoles.Annotator, IsActive = true },
                new User { Id = reviewerId, FullName = "Reviewer User", Email = "reviewer@system.com", PasswordHash = BCrypt.Net.BCrypt.HashPassword("123456"), Role = UserRoles.Reviewer, IsActive = true }
            };

            await context.Users.AddRangeAsync(users);
            await context.SaveChangesAsync();

            var project = new Project
            {
                Name = "Dự án Thử nghiệm (Dev Only)",
                Description = "Dữ liệu này chỉ xuất hiện trong môi trường Development.",
                ManagerId = managerId,
                Deadline = DateTime.UtcNow.AddDays(30),
                CreatedDate = DateTime.UtcNow,
                AllowGeometryTypes = "Rectangle",
                AnnotationGuide = "Đây là hướng dẫn gán nhãn mẫu cho dự án thử nghiệm.",
                PenaltyUnit = 10,
                LabelClasses = new List<LabelClass>
                {
                    new LabelClass { Name = "Object", Color = "#FF0000", GuideLine = "Vẽ khung bao quanh vật thể." }
                }
            };

            await context.Projects.AddAsync(project);
            await context.SaveChangesAsync();
        }
    }
}