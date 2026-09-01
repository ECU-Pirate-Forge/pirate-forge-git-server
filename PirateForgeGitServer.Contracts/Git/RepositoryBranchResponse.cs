namespace PirateForgeGitServer.Contracts.Git;

public sealed record RepositoryBranchResponse(
    string Name,
    string CommitId,
    DateTimeOffset CommittedAt);
