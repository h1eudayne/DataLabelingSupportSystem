using Microsoft.Extensions.Logging;

namespace DAL
{
    public static class AssignmentSchemaUpdater
    {
        public static async Task EnsureAssignmentIndexesAsync(ApplicationDbContext context, ILogger logger)
        {
            if (!string.Equals(context.Database.ProviderName, "Pomelo.EntityFrameworkCore.MySql", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                logger.LogInformation("Skipping legacy assignment index updater because MySQL schema is created from the current EF model.");
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Unable to update Assignment indexes automatically.");
            }
        }
    }
}
