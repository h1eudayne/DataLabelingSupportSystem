using DAL;
using DTOs.Constants;
using DTOs.Entities;
using Microsoft.EntityFrameworkCore;

namespace API
{
    public static class DataSeeder
    {
        public static async Task SeedUsersAsync(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // 1. Seed Users (Nếu chưa có)
            if (!await context.Users.AnyAsync())
            {
                var users = new List<User>
                {
                    new User
                    {
                        Id = Guid.NewGuid().ToString(),
                        FullName = "Admin System",
                        Email = "Admin@Gmail.com",
                        Role = UserRoles.Admin,
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword("123456")
                    },
                    new User
                    {
                        Id = "manager-01", // Hard-code ID để dễ link dữ liệu
                        FullName = "Manager Boss",
                        Email = "Manager@Gmail.com",
                        Role = UserRoles.Manager,
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword("123456")
                    },
                    new User
                    {
                        Id = "staff-01", // Hard-code ID để gán task
                        FullName = "Staff Annotator",
                        Email = "Staff@Gmail.com",
                        Role = UserRoles.Annotator,
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword("123456")
                    }
                };

                await context.Users.AddRangeAsync(users);
                await context.SaveChangesAsync();
            }

            // 2. Seed Project & Data (Nếu chưa có Project nào)
            if (!await context.Projects.AnyAsync())
            {
                // Tạo 1 Dự án mẫu
                var project = new Project
                {
                    Name = "Dự án Phân loại Xe cộ (Demo)",
                    Description = "Dự án này dùng để test tính năng Dashboard và Progress.",
                    ManagerId = "manager-01", // Gán cho ông Manager trên
                    PricePerLabel = 5000,
                    TotalBudget = 10000000,
                    Deadline = DateTime.UtcNow.AddDays(7),
                    CreatedDate = DateTime.UtcNow,
                    AllowGeometryTypes = "Rectangle"
                };

                // Thêm Nhãn (Labels)
                project.LabelClasses.Add(new LabelClass { Name = "Car", Color = "#FF0000", GuideLine = "Vẽ khung quanh xe con" });
                project.LabelClasses.Add(new LabelClass { Name = "Truck", Color = "#00FF00", GuideLine = "Vẽ khung quanh xe tải" });
                project.LabelClasses.Add(new LabelClass { Name = "Bike", Color = "#0000FF", GuideLine = "Vẽ khung quanh xe máy" });

                // Thêm DataItems (Ảnh mẫu)
                for (int i = 1; i <= 10; i++)
                {
                    project.DataItems.Add(new DataItem
                    {
                        StorageUrl = $"https://via.placeholder.com/600x400?text=Image_{i}", // Ảnh dummy
                        Status = "New",
                        UploadedDate = DateTime.UtcNow
                    });
                }

                await context.Projects.AddAsync(project);
                await context.SaveChangesAsync();

                // 3. Seed Assignments (Gán việc cho Staff để hiện Dashboard)
                var dataItems = await context.DataItems.Where(d => d.ProjectId == project.Id).ToListAsync();
                var staffId = "staff-01";

                var assignments = new List<Assignment>
                {
                    // 1 Task đang làm
                    new Assignment { ProjectId = project.Id, DataItemId = dataItems[0].Id, AnnotatorId = staffId, Status = "InProgress", AssignedDate = DateTime.UtcNow },
                    // 1 Task đã nộp
                    new Assignment { ProjectId = project.Id, DataItemId = dataItems[1].Id, AnnotatorId = staffId, Status = "Submitted", AssignedDate = DateTime.UtcNow, SubmittedAt = DateTime.UtcNow },
                    // 1 Task bị từ chối (Để hiện Rejected đỏ lòm)
                    new Assignment { ProjectId = project.Id, DataItemId = dataItems[2].Id, AnnotatorId = staffId, Status = "Rejected", AssignedDate = DateTime.UtcNow },
                    // 1 Task đã xong (Approved)
                    new Assignment { ProjectId = project.Id, DataItemId = dataItems[3].Id, AnnotatorId = staffId, Status = "Completed", AssignedDate = DateTime.UtcNow, SubmittedAt = DateTime.UtcNow }
                };

                // Cập nhật trạng thái DataItem tương ứng
                dataItems[0].Status = "Assigned";
                dataItems[1].Status = "Assigned";
                dataItems[2].Status = "Assigned";
                dataItems[3].Status = "Done";

                await context.Assignments.AddRangeAsync(assignments);
                await context.SaveChangesAsync();
            }
        }
    }
}