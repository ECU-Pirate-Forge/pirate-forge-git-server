using Microsoft.AspNetCore.DataProtection;

namespace PirateForgeGitServer.Web.Identity;

public sealed partial class PersonalAccessTokenFlashStore(
    IDataProtectionProvider provider,
    ILogger<PersonalAccessTokenFlashStore> logger)
{
    private const string CookieName =
        "__Host-PirateForge.NewPersonalAccessToken";
    private readonly IDataProtector protector = provider.CreateProtector(
        "PirateForgeGitServer.Web.PersonalAccessTokenFlash.v1");

    public void Store(HttpResponse response, string token)
    {
        response.Cookies.Append(
            CookieName,
            protector.Protect(token),
            new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                MaxAge = TimeSpan.FromMinutes(5),
                Path = "/",
                SameSite = SameSiteMode.Strict,
                Secure = true,
            });
    }

    public string? Take(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(CookieName, out var protectedToken))
        {
            return null;
        }

        context.Response.Cookies.Delete(
            CookieName,
            new CookieOptions
            {
                Path = "/",
                SameSite = SameSiteMode.Strict,
                Secure = true,
            });

        try
        {
            return protector.Unprotect(protectedToken);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            LogInvalidFlashCookie(logger);
            return null;
        }
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Discarded an invalid personal access token flash cookie.")]
    private static partial void LogInvalidFlashCookie(
        ILogger<PersonalAccessTokenFlashStore> logger);
}
