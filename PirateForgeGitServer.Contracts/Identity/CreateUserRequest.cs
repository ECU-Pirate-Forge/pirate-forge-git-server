namespace PirateForgeGitServer.Contracts.Identity;

public sealed record CreateUserRequest(
    string Email,
    string DisplayName,
    string Role);
