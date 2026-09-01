namespace PirateForgeGitServer.Contracts.Identity;

public sealed record CreatedPersonalAccessTokenResponse(
    Guid Id,
    string Name,
    string Prefix,
    string Token,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt);
