using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DAL
{
    public static class AssignmentSchemaUpdater
    {
        public static async Task EnsureAssignmentIndexesAsync(ApplicationDbContext context, ILogger logger)
        {
            if (!context.Database.IsSqlServer())
            {
                return;
            }

            const string sql = """
                IF OBJECT_ID(N'[dbo].[Assignments]', N'U') IS NULL
                    RETURN;

                IF EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_Assignment_DataItem_Annotator'
                      AND object_id = OBJECT_ID(N'[dbo].[Assignments]')
                )
                BEGIN
                    DROP INDEX [IX_Assignment_DataItem_Annotator] ON [dbo].[Assignments];
                END

                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_Assignment_DataItem_Annotator_Reviewer'
                      AND object_id = OBJECT_ID(N'[dbo].[Assignments]')
                )
                BEGIN
                    CREATE UNIQUE INDEX [IX_Assignment_DataItem_Annotator_Reviewer]
                    ON [dbo].[Assignments] ([DataItemId], [AnnotatorId], [ReviewerId]);
                END
                """;

            try
            {
                await context.Database.ExecuteSqlRawAsync(sql);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Unable to update Assignment indexes automatically.");
            }
        }
    }
}
