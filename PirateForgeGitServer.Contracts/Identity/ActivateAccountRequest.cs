namespace PirateForgeGitServer.Contracts.Identity;

public sealed record ActivateAccountRequest(
    Guid UserId,
    string Token,
    string Password);
