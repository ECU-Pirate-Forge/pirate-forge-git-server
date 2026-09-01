namespace PirateForgeGitServer.Infrastructure.Identity;

public sealed class BootstrapAdminOptions
{
    public const string SectionName = "PirateForge:BootstrapAdmin";

    public string? Email { get; init; }

    public string? Password { get; init; }

    public string? DisplayName { get; init; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Email)
        && !string.IsNullOrWhiteSpace(Password)
        && !string.IsNullOrWhiteSpace(DisplayName);

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Email)
        && string.IsNullOrWhiteSpace(Password)
        && string.IsNullOrWhiteSpace(DisplayName);
}
