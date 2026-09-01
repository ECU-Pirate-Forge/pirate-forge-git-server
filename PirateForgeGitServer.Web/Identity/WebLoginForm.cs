namespace PirateForgeGitServer.Web.Identity;

public sealed class WebLoginForm
{
    public string Email { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public bool RememberMe { get; init; }

    public string? ReturnUrl { get; init; }
}

public sealed class WebLogoutForm
{
    public string? ReturnUrl { get; init; }
}
