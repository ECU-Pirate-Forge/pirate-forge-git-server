namespace PirateForgeGitServer.Contracts.Git;

public sealed record RepositoryResponse(
    Guid Id,
    string Name,
    string DisplayName,
    Guid OwnerUserId,
    DateTimeOffset CreatedAt);
