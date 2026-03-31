using Core.Interfaces;
using DAL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;

namespace DAL
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddDataAccess(this IServiceCollection services, IConfiguration configuration)
        {
            var connectionString = DatabaseConnectionStringResolver.GetRequiredConnectionString(configuration);
            var configuredServerVersion = configuration["Database:ServerVersion"];
            var serverVersion = !string.IsNullOrWhiteSpace(configuredServerVersion)
                ? ServerVersion.Parse(configuredServerVersion)
                : ServerVersion.AutoDetect(connectionString);

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseMySql(
                    connectionString,
                    serverVersion,
                    mySqlOptions =>
                    {
                        mySqlOptions.CommandTimeout(120);
                        mySqlOptions.EnableRetryOnFailure(
                            maxRetryCount: 5,
                            maxRetryDelay: TimeSpan.FromSeconds(30),
                            errorNumbersToAdd: null);
                    }));

            services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
            services.AddScoped<IUserRepository, UserRepository>();
            services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
            services.AddScoped<IProjectRepository, ProjectRepository>();
            services.AddScoped<IAssignmentRepository, AssignmentRepository>();
            services.AddScoped<ILabelRepository, LabelRepository>();
            services.AddScoped<IDisputeRepository, DisputeRepository>();
            services.AddScoped<IActivityLogRepository, ActivityLogRepository>();

            return services;
        }

        public static async Task SeedDataAsync(this IServiceProvider serviceProvider, bool isDevelopment)
        {
            await DataSeeder.SeedData(serviceProvider, isDevelopment);
        }
    }
}

