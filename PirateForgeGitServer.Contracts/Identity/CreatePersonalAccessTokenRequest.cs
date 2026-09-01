namespace PirateForgeGitServer.Contracts.Identity;

public sealed record CreatePersonalAccessTokenRequest(
    string Name,
    DateTimeOffset? ExpiresAt);
