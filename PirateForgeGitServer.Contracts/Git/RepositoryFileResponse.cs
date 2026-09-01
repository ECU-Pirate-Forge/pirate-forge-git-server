namespace PirateForgeGitServer.Contracts.Git;

public sealed record RepositoryFileResponse(
    string Path,
    string Language,
    string Content,
    long Size);
