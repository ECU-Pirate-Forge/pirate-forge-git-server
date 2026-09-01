using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PirateForgeGitServer.Infrastructure.Persistence;

internal sealed class SqliteHealthCheck(
    IDbContextFactory<PirateForgeDbContext> contextFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var dbContext =
                await contextFactory.CreateDbContextAsync(cancellationToken);
            await dbContext.Database.OpenConnectionAsync(cancellationToken);

            await using var command = dbContext.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT 1;";
            await command.ExecuteScalarAsync(cancellationToken);

            return HealthCheckResult.Healthy();
        }
        catch (DbException exception)
        {
            return HealthCheckResult.Unhealthy(
                "The SQLite database is unavailable.",
                exception);
        }
        catch (InvalidOperationException exception)
        {
            return HealthCheckResult.Unhealthy(
                "The SQLite database is not configured correctly.",
                exception);
        }
    }
}
