using Core.Constants;
using Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DAL
{
    public static class DataSeeder
    {
        public static async Task SeedData(IServiceProvider serviceProvider, bool isDevelopment)
        {
            using (var scope = serviceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

                if (isDevelopment)
                {
                    await SeedSampleDataForTesting(context, configuration);
                }
                else
                {
                    await SeedDefaultAdminAsync(context, configuration);
                }
            }
        }

        private static async Task SeedDefaultAdminAsync(ApplicationDbContext context, IConfiguration configuration)
        {
            var adminSeed = ResolveAdminSeed(configuration);

            if (!adminSeed.Enabled || await context.Users.AnyAsync(user => user.Role == UserRoles.Admin))
            {
                return;
            }

            var admin = new User
            {
                FullName = adminSeed.FullName,
                Username = BuildUsernameFromEmail(adminSeed.Email),
                Email = adminSeed.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(adminSeed.Password),
                Role = UserRoles.Admin,
                IsActive = true,
                IsEmailVerified = true
            };

            await context.Users.AddAsync(admin);
            await context.SaveChangesAsync();
        }

        private static async Task SeedSampleDataForTesting(ApplicationDbContext context, IConfiguration configuration)
        {
            if (await context.Users.AnyAsync()) return;

            var adminSeed = ResolveAdminSeed(configuration);
            var adminId = "440816a8-8954-4557-a462-196471ce8b02";
            var managerId = "49179920-d356-46f9-bb64-64da9f6ef4ee";
            var annotatorId = "5c023639-82ed-448e-9415-fc2e86b2d325";
            var reviewerId = "59420220-7179-4029-92e8-54a7c7acb39d";

            var users = new List<User>
            {
                new User
                {
                    Id = adminId,
                    FullName = adminSeed.FullName,
                    Username = BuildUsernameFromEmail(adminSeed.Email),
                    Email = adminSeed.Email,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(adminSeed.Password),
                    Role = UserRoles.Admin,
                    IsActive = true,
                    IsEmailVerified = true
                },
                new User
                {
                    Id = managerId,
                    FullName = "Manager User",
                    Username = "manager",
                    Email = "manager@system.com",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("123456"),
                    Role = UserRoles.Manager,
                    IsActive = true,
                    IsEmailVerified = true
                },
                new User
                {
                    Id = annotatorId,
                    FullName = "Annotator User",
                    Username = "annotator",
                    Email = "annotator@system.com",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("123456"),
                    Role = UserRoles.Annotator,
                    IsActive = true,
                    IsEmailVerified = true
                },
                new User
                {
                    Id = reviewerId,
                    FullName = "Reviewer User",
                    Username = "reviewer",
                    Email = "reviewer@system.com",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("123456"),
                    Role = UserRoles.Reviewer,
                    IsActive = true,
                    IsEmailVerified = true
                }
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

        private static AdminSeedSettings ResolveAdminSeed(IConfiguration configuration)
        {
            var enabled = configuration.GetValue("SeedAdmin:Enabled", true);
            var fullName = (configuration["SeedAdmin:FullName"] ?? "System Administrator").Trim();
            var email = (configuration["SeedAdmin:Email"] ?? "ducnguyen230705@gmail.com").Trim();
            var password = configuration["SeedAdmin:Password"] ?? "123456";

            return new AdminSeedSettings(
                enabled,
                string.IsNullOrWhiteSpace(fullName) ? "System Administrator" : fullName,
                string.IsNullOrWhiteSpace(email) ? "ducnguyen230705@gmail.com" : email,
                string.IsNullOrWhiteSpace(password) ? "123456" : password);
        }

        private static string BuildUsernameFromEmail(string email)
        {
            var localPart = email.Split('@', 2, StringSplitOptions.TrimEntries)[0];
            var sanitized = new string(localPart
                .Where(character => char.IsLetterOrDigit(character) || character is '.' or '_' or '-')
                .ToArray());

            if (string.IsNullOrWhiteSpace(sanitized))
            {
                sanitized = "admin";
            }

            return sanitized.Length <= 50 ? sanitized : sanitized[..50];
        }

        private sealed record AdminSeedSettings(bool Enabled, string FullName, string Email, string Password);
    }
}
