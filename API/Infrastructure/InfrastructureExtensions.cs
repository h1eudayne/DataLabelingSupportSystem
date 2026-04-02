using System.Data;
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
                var applyMigrationsOnStartup = configuration.GetValue<bool?>("Database:ApplyMigrationsOnStartup") ?? isDevelopment;
                var ensureCreatedOnStartup = configuration.GetValue<bool?>("Database:EnsureCreatedOnStartup") ?? false;
                var connection = context.Database.GetDbConnection();

                logger.LogInformation(
                    "Database connection target resolved. Provider={Provider}; Server={Server}; Database={Database}",
                    context.Database.ProviderName,
                    connection.DataSource,
                    connection.Database);
                logger.LogInformation(
                    "Database startup behavior resolved. ApplyMigrations={ApplyMigrations}; EnsureCreated={EnsureCreated}; IsDevelopment={IsDevelopment}",
                    applyMigrationsOnStartup,
                    ensureCreatedOnStartup,
                    isDevelopment);

                if (applyMigrationsOnStartup)
                {
                    await GuardAgainstAutomaticMigrationOnUntrackedSchemaAsync(context, logger);
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

        private static async Task GuardAgainstAutomaticMigrationOnUntrackedSchemaAsync(
            global::DAL.ApplicationDbContext context,
            ILogger logger)
        {
            if (!string.Equals(context.Database.ProviderName, "Pomelo.EntityFrameworkCore.MySql", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var connection = context.Database.GetDbConnection();
            var shouldCloseConnection = connection.State != ConnectionState.Open;

            if (shouldCloseConnection)
            {
                await connection.OpenAsync();
            }

            try
            {
                var existingTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                await using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT table_name
                    FROM information_schema.tables
                    WHERE table_schema = DATABASE()
                      AND table_type = 'BASE TABLE';
                    """;

                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    if (!reader.IsDBNull(0))
                    {
                        existingTables.Add(reader.GetString(0));
                    }
                }

                if (existingTables.Count == 0 || existingTables.Contains("__EFMigrationsHistory"))
                {
                    return;
                }

                var overlappingTables = context.Model.GetEntityTypes()
                    .Select(entityType => entityType.GetTableName())
                    .Where(tableName => !string.IsNullOrWhiteSpace(tableName))
                    .Cast<string>()
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Where(existingTables.Contains)
                    .OrderBy(tableName => tableName, StringComparer.OrdinalIgnoreCase)
                    .Take(5)
                    .ToArray();

                if (overlappingTables.Length == 0)
                {
                    return;
                }

                logger.LogCritical(
                    "Automatic EF migrations were blocked because database {Database} already contains application tables but has no __EFMigrationsHistory table. Overlapping tables: {Tables}",
                    connection.Database,
                    string.Join(", ", overlappingTables));

                throw new InvalidOperationException(
                    $"Automatic EF Core migrations were blocked for database '{connection.Database}' because it already contains application tables " +
                    $"({string.Join(", ", overlappingTables)}) but has no '__EFMigrationsHistory' table. " +
                    "This schema was likely created outside EF migrations. Disable 'Database:ApplyMigrationsOnStartup' for this environment " +
                    "or baseline the database before enabling automatic migrations.");
            }
            finally
            {
                if (shouldCloseConnection)
                {
                    await connection.CloseAsync();
                }
            }
        }
    }
}
