namespace PirateForgeGitServer.Contracts.Identity;

public sealed record AccountTokenResponse(
    Guid UserId,
    string Email,
    string Token);
