using Microsoft.Extensions.Hosting;

namespace PirateForgeGitServer.Infrastructure.Storage;

internal sealed class StorageInitializer(StoragePaths paths) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        paths.EnsureCreated();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
