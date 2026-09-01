namespace PirateForgeGitServer.Contracts.Identity;

public sealed record UserResponse(
    Guid Id,
    string Email,
    string DisplayName,
    bool IsActive,
    bool IsActivated,
    IReadOnlyList<string> Roles);
