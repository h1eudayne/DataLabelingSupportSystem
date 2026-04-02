using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace API.Infrastructure
{
    public static class InfrastructureExtensions
    {
        public static IServiceCollection AddApplicationInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            global::DAL.DatabaseConnectionStringResolver.GetRequiredConnectionString(configuration);
            global::DAL.DependencyInjection.AddDataAccess(services, configuration);
            return services;
        }

        public static async Task InitializeApplicationInfrastructureAsync(this IServiceProvider serviceProvider, bool isDevelopment)
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

        public static IEndpointRouteBuilder MapApplicationHealthEndpoints(this IEndpointRouteBuilder endpoints, bool isDevelopment)
        {
            endpoints.MapGet("/health", async (global::DAL.ApplicationDbContext db, CancellationToken cancellationToken) =>
            {
                try
                {
                    var canConnect = await db.Database.CanConnectAsync(cancellationToken);
                    return canConnect
                        ? Results.Ok(new { status = "ok", database = "reachable" })
                        : Results.Problem(
                            title: "Database unavailable",
                            statusCode: StatusCodes.Status503ServiceUnavailable);
                }
                catch (Exception ex)
                {
                    return Results.Problem(
                        title: "Database unavailable",
                        detail: isDevelopment ? ex.Message : null,
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }
            }).AllowAnonymous();

            return endpoints;
        }
    }
}
