using BLL.Interfaces;
using BLL.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BLL
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddBusinessLogic(this IServiceCollection services, IConfiguration configuration)
        {
            global::DAL.DependencyInjection.AddDataAccess(services, configuration);

            services.AddScoped<IUserService, UserService>();
            services.AddScoped<IProjectService, ProjectService>();
            services.AddScoped<ITaskService, TaskService>();
            services.AddScoped<IReviewService, ReviewService>();
            services.AddScoped<ILabelService, LabelService>();
            services.AddScoped<IStatisticService, StatisticService>();
            services.AddScoped<IDisputeService, DisputeService>();
            services.AddScoped<IActivityLogService, ActivityLogService>();
            services.AddScoped<IAppNotificationService, AppNotificationService>();
            services.AddSingleton<IEmailService, BackgroundEmailService>();
            services.AddScoped<IWorkflowEmailService, WorkflowEmailService>();

            return services;
        }

        public static async Task SeedDataAsync(this IServiceProvider serviceProvider, bool isDevelopment)
        {
            await global::DAL.DependencyInjection.SeedDataAsync(serviceProvider, isDevelopment);
        }

        public static async Task InitializeInfrastructureAsync(this IServiceProvider serviceProvider, bool isDevelopment)
        {
            using var scope = serviceProvider.CreateScope();
            var services = scope.ServiceProvider;
            var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
            var configuration = services.GetRequiredService<IConfiguration>();

            try
            {
                var context = services.GetRequiredService<global::DAL.ApplicationDbContext>();
                var applyMigrationsOnStartup = configuration.GetValue("Database:ApplyMigrationsOnStartup", true);
                var ensureCreatedOnStartup = configuration.GetValue("Database:EnsureCreatedOnStartup", true);
                var connection = context.Database.GetDbConnection();

                logger.LogInformation(
                    "Database connection target resolved. Provider={Provider}; Server={Server}; Database={Database}",
                    context.Database.ProviderName,
                    connection.DataSource,
                    connection.Database);

                if (applyMigrationsOnStartup)
                {
                    logger.LogInformation("Applying database migrations on startup.");
                    await context.Database.MigrateAsync();
                }
                else if (ensureCreatedOnStartup)
                {
                    logger.LogInformation("Ensuring database schema exists from the current EF model.");
                    await context.Database.EnsureCreatedAsync();
                }
                else
                {
                    logger.LogInformation("Skipping automatic database schema initialization on startup.");
                }

                await global::DAL.AssignmentSchemaUpdater.EnsureAssignmentIndexesAsync(context, logger);
                await global::DAL.DependencyInjection.SeedDataAsync(services, isDevelopment);
                logger.LogInformation("Infrastructure initialization completed successfully.");
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "Infrastructure initialization failed during startup.");
                throw;
            }
        }
    }
}
