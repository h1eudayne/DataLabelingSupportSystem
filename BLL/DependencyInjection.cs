using BLL.Interfaces;
using BLL.Services;
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

            try
            {
                var context = services.GetRequiredService<global::DAL.ApplicationDbContext>();
                await global::DAL.AssignmentSchemaUpdater.EnsureAssignmentIndexesAsync(context, logger);
                await global::DAL.DependencyInjection.SeedDataAsync(services, isDevelopment);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred while seeding the initial data.");
            }
        }
    }
}
