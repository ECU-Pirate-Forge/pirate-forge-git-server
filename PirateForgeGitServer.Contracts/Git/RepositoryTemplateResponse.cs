namespace PirateForgeGitServer.Contracts.Git;

public sealed record RepositoryTemplateResponse(
    Guid Id,
    string Name,
    string DisplayName,
    Guid OwnerUserId,
    DateTimeOffset CreatedAt,
    string Purpose);
