namespace PirateForgeGitServer.Contracts.Identity;

public sealed record ResetPasswordRequest(
    Guid UserId,
    string Token,
    string Password);
