using Microsoft.Extensions.Options;
using PirateForgeGitServer.Infrastructure.Storage;

namespace PirateForgeGitServer.UnitTests.Infrastructure;

public sealed class StoragePathsTests : IDisposable
{
    private readonly string _rootPath =
        Path.Combine(Path.GetTempPath(), $"pirate-forge-tests-{Guid.NewGuid():N}");

    [Fact]
    public void EnsureCreatedCreatesEveryStorageDirectory()
    {
        var paths = new StoragePaths(
            Options.Create(new StorageOptions { RootPath = _rootPath }));

        paths.EnsureCreated();

        Assert.All(
            paths.Directories,
            directory => Assert.True(Directory.Exists(directory)));
        Assert.StartsWith(paths.RootPath, paths.DatabasePath, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }
}
