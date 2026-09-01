using Microsoft.Extensions.Options;

namespace PirateForgeGitServer.Infrastructure.Storage;

public sealed class StoragePaths(IOptions<StorageOptions> options)
{
    public string RootPath { get; } = Path.GetFullPath(options.Value.RootPath);

    public string DatabaseDirectory => Path.Combine(RootPath, "database");

    public string DatabasePath => Path.Combine(DatabaseDirectory, "pirate-forge.db");

    public string RepositoriesPath => Path.Combine(RootPath, "repositories");

    public string DownloadsPath => Path.Combine(RootPath, "downloads");

    public string SubmissionArchivesPath => Path.Combine(RootPath, "submission-archives");

    public string DataProtectionKeysPath => Path.Combine(RootPath, "data-protection-keys");

    public IReadOnlyList<string> Directories =>
    [
        RootPath,
        DatabaseDirectory,
        RepositoriesPath,
        DownloadsPath,
        SubmissionArchivesPath,
        DataProtectionKeysPath
    ];

    public void EnsureCreated()
    {
        foreach (var directory in Directories)
        {
            Directory.CreateDirectory(directory);
        }
    }
}
