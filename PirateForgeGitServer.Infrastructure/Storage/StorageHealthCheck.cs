using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PirateForgeGitServer.Infrastructure.Storage;

internal sealed class StorageHealthCheck(StoragePaths paths) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            foreach (var directory in paths.Directories)
            {
                if (!Directory.Exists(directory))
                {
                    return Task.FromResult(
                        HealthCheckResult.Unhealthy($"Storage directory does not exist: {directory}"));
                }
            }

            var probePath = Path.Combine(paths.RootPath, $".health-{Guid.NewGuid():N}");
            using (File.Create(probePath, 1, FileOptions.DeleteOnClose))
            {
            }

            return Task.FromResult(HealthCheckResult.Healthy());
        }
        catch (IOException exception)
        {
            return Task.FromResult(
                HealthCheckResult.Unhealthy("Storage is not writable.", exception));
        }
        catch (UnauthorizedAccessException exception)
        {
            return Task.FromResult(
                HealthCheckResult.Unhealthy("Storage access was denied.", exception));
        }
    }
}
