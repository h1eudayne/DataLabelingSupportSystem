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
            var adminSeed = ResolveAdminSeed(configuration, useFallbackDefaults: false);

            if (!adminSeed.Enabled)
            {
                return;
            }

            var existingAdmin = await context.Users.FirstOrDefaultAsync(user => user.Role == UserRoles.Admin);
            var userWithConfiguredEmail = await context.Users.FirstOrDefaultAsync(user => user.Email == adminSeed.Email);

            if (userWithConfiguredEmail == null)
            {
                if (existingAdmin != null)
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
                return;
            }

            if (existingAdmin != null && existingAdmin.Id != userWithConfiguredEmail.Id)
            {
                return;
            }

            var shouldRefreshPassword =
                string.IsNullOrWhiteSpace(userWithConfiguredEmail.PasswordHash) ||
                !string.Equals(userWithConfiguredEmail.Role, UserRoles.Admin, StringComparison.OrdinalIgnoreCase) ||
                !userWithConfiguredEmail.IsActive;

            userWithConfiguredEmail.FullName = adminSeed.FullName;
            userWithConfiguredEmail.Username = BuildUsernameFromEmail(adminSeed.Email);
            userWithConfiguredEmail.Email = adminSeed.Email;
            userWithConfiguredEmail.Role = UserRoles.Admin;
            userWithConfiguredEmail.IsActive = true;
            userWithConfiguredEmail.IsEmailVerified = true;

            if (shouldRefreshPassword)
            {
                userWithConfiguredEmail.PasswordHash = BCrypt.Net.BCrypt.HashPassword(adminSeed.Password);
            }

            await context.SaveChangesAsync();
        }

        private static async Task SeedSampleDataForTesting(ApplicationDbContext context, IConfiguration configuration)
        {
            if (await context.Users.AnyAsync()) return;

            var adminSeed = ResolveAdminSeed(configuration, useFallbackDefaults: true);
            if (!adminSeed.Enabled) return;
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

        private static AdminSeedSettings ResolveAdminSeed(IConfiguration configuration, bool useFallbackDefaults)
        {
            var fullName = (configuration["SeedAdmin:FullName"] ?? string.Empty).Trim();
            var email = (configuration["SeedAdmin:Email"] ?? string.Empty).Trim();
            var password = configuration["SeedAdmin:Password"] ?? string.Empty;
            var configuredEnabled = configuration.GetValue<bool?>("SeedAdmin:Enabled");

            if (useFallbackDefaults)
            {
                fullName = string.IsNullOrWhiteSpace(fullName) ? "System Administrator" : fullName;
                email = string.IsNullOrWhiteSpace(email) ? "admin@system.com" : email;
                password = string.IsNullOrWhiteSpace(password) ? "123456" : password;
            }

            var enabled = configuredEnabled ?? useFallbackDefaults;

            if (!enabled)
            {
                return new AdminSeedSettings(false, string.Empty, string.Empty, string.Empty);
            }

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException(
                    "FATAL: Seed admin is enabled but credentials are missing. " +
                    "Set 'SeedAdmin:Enabled'/'SeedAdmin__Enabled' to true together with " +
                    "'SeedAdmin:Email'/'SeedAdmin__Email' and 'SeedAdmin:Password'/'SeedAdmin__Password'.");
            }

            return new AdminSeedSettings(
                enabled,
                string.IsNullOrWhiteSpace(fullName) ? "System Administrator" : fullName,
                email,
                password);
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
