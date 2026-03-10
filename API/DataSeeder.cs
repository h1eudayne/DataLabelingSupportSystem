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

                // Chỉ chạy khi môi trường là Development
                if (isDevelopment)
                {
                    await SeedSampleDataForTesting(context);
                }
            }
        }

        private static async Task SeedSampleDataForTesting(ApplicationDbContext context)
        {
            // Nếu trong DB đã có dữ liệu thì thôi không seed nữa để tránh lỗi trùng lặp
            if (await context.Users.AnyAsync()) return;

            var adminId = Guid.NewGuid().ToString();
            var managerId = Guid.NewGuid().ToString();
            var reviewerId = Guid.NewGuid().ToString();
            var defaultPassword = BCrypt.Net.BCrypt.HashPassword("123456");

            // 1. Tạo dàn Sếp và Quản lý
            var users = new List<User>
            {
                new User { Id = adminId, FullName = "Admin Tổng", Email = "admin@system.com", PasswordHash = defaultPassword, Role = UserRoles.Admin, IsActive = true },
                new User { Id = managerId, FullName = "Quản lý Hà", Email = "manager@system.com", PasswordHash = defaultPassword, Role = UserRoles.Manager, IsActive = true },
                new User { Id = reviewerId, FullName = "Reviewer Tuấn", Email = "reviewer@system.com", PasswordHash = defaultPassword, Role = UserRoles.Reviewer, IsActive = true }
            };

            // 2. Tạo 20 Annotator cho FE test phân trang
            var annotatorIds = new List<string>();
            for (int i = 1; i <= 20; i++)
            {
                var annId = Guid.NewGuid().ToString();
                annotatorIds.Add(annId);
                users.Add(new User
                {
                    Id = annId,
                    FullName = $"Nhân viên số {i}",
                    Email = $"annotator{i}@system.com",
                    PasswordHash = defaultPassword,
                    Role = UserRoles.Annotator,
                    IsActive = true,
                    ManagerId = managerId // Gán thẳng đệ tử cho bà Hà
                });
            }

            await context.Users.AddRangeAsync(users);
            await context.SaveChangesAsync();

            // 3. Tạo Dự án mẫu
            var project = new Project
            {
                Name = "Dự án Nhận diện Biển báo (Demo)",
                Description = "Dự án chứa data mẫu để FE làm UI.",
                ManagerId = managerId,
                PricePerLabel = 1000,
                TotalBudget = 5000000,
                StartDate = DateTime.UtcNow,
                EndDate = DateTime.UtcNow.AddDays(30),
                Deadline = DateTime.UtcNow.AddDays(30),
                CreatedDate = DateTime.UtcNow,
                AllowGeometryTypes = "Rectangle",
                AnnotationGuide = "Vui lòng vẽ khung bao quanh các biển báo giao thông.",
                PenaltyUnit = 10,
                LabelClasses = new List<LabelClass>
                {
                    new LabelClass { Name = "Biển Cấm", Color = "#FF0000", GuideLine = "Vòng tròn đỏ" },
                    new LabelClass { Name = "Biển Chỉ dẫn", Color = "#0000FF", GuideLine = "Nền xanh, hình vuông/chữ nhật" }
                }
            };

            await context.Projects.AddAsync(project);
            await context.SaveChangesAsync();

            // 4. Tạo sẵn 15 cái ảnh (DataItems) + Assignments để FE test hiển thị Task
            var dataItems = new List<DataItem>();
            for (int i = 1; i <= 15; i++)
            {
                dataItems.Add(new DataItem
                {
                    ProjectId = project.Id,
                    StorageUrl = $"https://picsum.photos/800/600?random={i}", // Lấy URL ảnh ngẫu nhiên trên mạng
                    BucketId = 1,
                    Status = TaskStatusConstants.Assigned,
                    UploadedDate = DateTime.UtcNow
                });
            }
            await context.DataItems.AddRangeAsync(dataItems);
            await context.SaveChangesAsync();

            var assignments = new List<Assignment>();
            foreach (var item in dataItems)
            {
                assignments.Add(new Assignment
                {
                    ProjectId = project.Id,
                    DataItemId = item.Id,
                    AnnotatorId = annotatorIds[0], // Gán tất cả 15 ảnh cho "Nhân viên số 1" để ông FE lấy acc này test
                    ReviewerId = reviewerId,
                    Status = TaskStatusConstants.Assigned,
                    AssignedDate = DateTime.UtcNow
                });
            }
            await context.Assignments.AddRangeAsync(assignments);
            await context.SaveChangesAsync();
        }
    }
}