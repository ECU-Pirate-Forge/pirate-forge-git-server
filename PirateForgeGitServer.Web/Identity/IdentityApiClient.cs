using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using PirateForgeGitServer.Contracts.Identity;

namespace PirateForgeGitServer.Web.Identity;

public sealed class IdentityApiClient(HttpClient httpClient)
{
    public async Task<WebIdentitySession?> LoginAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        using var loginResponse = await httpClient.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(email, password),
            cancellationToken);
        if (loginResponse.StatusCode is HttpStatusCode.Unauthorized)
        {
            return null;
        }

        loginResponse.EnsureSuccessStatusCode();
        var tokens = await loginResponse.Content.ReadFromJsonAsync<ApiTokenResponse>(
            cancellationToken)
            ?? throw new InvalidOperationException("The identity API returned an empty token response.");

        using var currentUserRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        currentUserRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            tokens.AccessToken);
        using var currentUserResponse = await httpClient.SendAsync(
            currentUserRequest,
            cancellationToken);
        currentUserResponse.EnsureSuccessStatusCode();
        var user = await currentUserResponse.Content.ReadFromJsonAsync<CurrentUserResponse>(
            cancellationToken)
            ?? throw new InvalidOperationException("The identity API returned an empty user response.");

        return new WebIdentitySession(
            user,
            tokens.AccessToken,
            tokens.RefreshToken,
            DateTimeOffset.UtcNow.AddSeconds(tokens.ExpiresIn));
    }

    public async Task<bool> ActivateAsync(
        Guid userId,
        string token,
        string password,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "/api/auth/activate",
            new ActivateAccountRequest(userId, token, password),
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }

    private sealed record ApiTokenResponse(
        string TokenType,
        string AccessToken,
        int ExpiresIn,
        string RefreshToken);
}

public sealed record WebIdentitySession(
    CurrentUserResponse User,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt);
