namespace PirateForgeGitServer.Contracts.Git;

public sealed record RepositoryTreeEntryResponse(
    string Name,
    string Path,
    string Kind,
    long? Size);
