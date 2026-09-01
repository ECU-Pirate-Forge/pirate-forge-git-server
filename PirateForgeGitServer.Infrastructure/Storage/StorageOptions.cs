namespace PirateForgeGitServer.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "PirateForge:Storage";

    public required string RootPath { get; init; }
}
