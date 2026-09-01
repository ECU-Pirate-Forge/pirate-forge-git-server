namespace PirateForgeGitServer.Contracts.Identity;

public sealed record PersonalAccessTokenResponse(
    Guid Id,
    string Name,
    string Prefix,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? LastUsedAt);
