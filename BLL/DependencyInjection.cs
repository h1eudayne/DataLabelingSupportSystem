using BLL.Interfaces;
using BLL.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net.Http;

namespace BLL
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddBusinessLogic(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddScoped<IUserService, UserService>();
            services.AddScoped<IProjectService, ProjectService>();
            services.AddScoped<ITaskService, TaskService>();
            services.AddScoped<IReviewService, ReviewService>();
            services.AddScoped<ILabelService, LabelService>();
            services.AddScoped<IStatisticService, StatisticService>();
            services.AddScoped<IDisputeService, DisputeService>();
            services.AddScoped<IActivityLogService, ActivityLogService>();
            services.AddScoped<IAppNotificationService, AppNotificationService>();
            services.AddSingleton<BackgroundEmailService>();
            services.AddSingleton<IEmailService>(serviceProvider => serviceProvider.GetRequiredService<BackgroundEmailService>());
            services.AddSingleton<IHostedService>(serviceProvider => serviceProvider.GetRequiredService<BackgroundEmailService>());
            services.AddScoped<IWorkflowEmailService, WorkflowEmailService>();

            services.AddHttpClient<IAIService, AIService>(client =>
                {
                    client.Timeout = TimeSpan.FromMinutes(3);
                    client.DefaultRequestHeaders.Add("User-Agent", "DataLabelingSystem/1.0");
                })
                .ConfigurePrimaryHttpMessageHandler(() =>
                {
                    var handler = new HttpClientHandler();
                    if (configuration.GetValue<bool>("AI:AllowUnsafeSslForDevelopment"))
                    {
                        handler.ServerCertificateCustomValidationCallback =
                            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
                    }

                    return handler;
                });

            return services;
        }
    }
}
