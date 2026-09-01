using System.Security.Claims;
using PirateForgeGitServer.Contracts.Identity;

namespace PirateForgeGitServer.Web.Identity;

public static class WebSessionFactory
{
    public static ClaimsPrincipal CreatePrincipal(CurrentUserResponse user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.DisplayName),
        };
        claims.AddRange(user.Roles.Select(role => new Claim(ClaimTypes.Role, role)));

        return new ClaimsPrincipal(
            new ClaimsIdentity(claims, WebAuthenticationDefaults.AuthenticationScheme));
    }

    public static string NormalizeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrEmpty(returnUrl)
            || !returnUrl.StartsWith('/')
            || returnUrl.StartsWith("//", StringComparison.Ordinal)
            || returnUrl.StartsWith("/\\", StringComparison.Ordinal))
        {
            return "/";
        }

        return returnUrl;
    }
}

public static class WebAuthenticationDefaults
{
    public const string AuthenticationScheme = "PirateForge.WebCookie";
}
