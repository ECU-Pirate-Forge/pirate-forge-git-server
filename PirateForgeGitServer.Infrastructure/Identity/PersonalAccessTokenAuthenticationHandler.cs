using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PirateForgeGitServer.Application.Identity;

namespace PirateForgeGitServer.Infrastructure.Identity;

internal sealed class PersonalAccessTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    PersonalAccessTokenService tokenService,
    UserManager<PirateForgeUser> userManager,
    SignInManager<PirateForgeUser> signInManager)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!AuthenticationHeaderValue.TryParse(
                Request.Headers.Authorization,
                out var header)
            || !string.Equals(header.Scheme, "Basic", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(header.Parameter))
        {
            return AuthenticateResult.NoResult();
        }

        string credentials;
        try
        {
            credentials = Encoding.UTF8.GetString(
                Convert.FromBase64String(header.Parameter));
        }
        catch (FormatException)
        {
            return AuthenticateResult.Fail("The Basic authentication value is invalid.");
        }

        var separatorIndex = credentials.IndexOf(':', StringComparison.Ordinal);
        if (separatorIndex <= 0 || separatorIndex == credentials.Length - 1)
        {
            return AuthenticateResult.Fail("A username and personal access token are required.");
        }

        var username = credentials[..separatorIndex];
        var token = credentials[(separatorIndex + 1)..];
        var userId = await tokenService.ValidateAsync(token, Context.RequestAborted);
        if (userId is null)
        {
            return AuthenticateResult.Fail("The personal access token is invalid.");
        }

        var user = await userManager.FindByIdAsync(userId.Value.ToString());
        if (user?.IsActive is not true
            || !string.Equals(
                user.Email,
                username,
                StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.Fail("The personal access token is invalid.");
        }

        var principal = await signInManager.CreateUserPrincipalAsync(user);
        return AuthenticateResult.Success(
            new AuthenticationTicket(principal, Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate =
            "Basic realm=\"Pirate Forge Git Server\", charset=\"UTF-8\"";
        return Task.CompletedTask;
    }
}
