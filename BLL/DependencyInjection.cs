using BLL.Interfaces;
using BLL.Services;
using DAL;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BLL
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddBusinessLogic(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddDataAccess(configuration);

            services.AddScoped<IUserService, UserService>();
            services.AddScoped<IProjectService, ProjectService>();
            services.AddScoped<ITaskService, TaskService>();
            services.AddScoped<IReviewService, ReviewService>();
            services.AddScoped<ILabelService, LabelService>();
            services.AddScoped<IStatisticService, StatisticService>();
            services.AddScoped<IDisputeService, DisputeService>();
            services.AddScoped<IActivityLogService, ActivityLogService>();
            services.AddScoped<IAppNotificationService, AppNotificationService>();

            return services;
        }

        public static async Task SeedDataAsync(this IServiceProvider serviceProvider, bool isDevelopment)
        {
            await DAL.DependencyInjection.SeedDataAsync(serviceProvider, isDevelopment);
        }
    }
}
