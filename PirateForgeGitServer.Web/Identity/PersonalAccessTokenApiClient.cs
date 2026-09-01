using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using PirateForgeGitServer.Contracts.Identity;

namespace PirateForgeGitServer.Web.Identity;

public sealed class PersonalAccessTokenApiClient(
    HttpClient httpClient,
    IHttpContextAccessor httpContextAccessor)
{
    public async Task<IReadOnlyList<PersonalAccessTokenResponse>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        using var request = await CreateAuthorizedRequestAsync(
            HttpMethod.Get,
            "/api/personal-access-tokens/");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<
                IReadOnlyList<PersonalAccessTokenResponse>>(cancellationToken)
            ?? throw new InvalidOperationException(
                "The identity API returned an empty token list.");
    }

    public async Task<CreatedPersonalAccessTokenResponse?> CreateAsync(
        CreatePersonalAccessTokenRequest request,
        CancellationToken cancellationToken = default)
    {
        using var message = await CreateAuthorizedRequestAsync(
            HttpMethod.Post,
            "/api/personal-access-tokens/");
        message.Content = JsonContent.Create(request);
        using var response = await httpClient.SendAsync(message, cancellationToken);
        if (response.StatusCode is HttpStatusCode.BadRequest)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<
                CreatedPersonalAccessTokenResponse>(cancellationToken)
            ?? throw new InvalidOperationException(
                "The identity API returned an empty created token.");
    }

    public async Task<bool> RevokeAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        using var request = await CreateAuthorizedRequestAsync(
            HttpMethod.Delete,
            $"/api/personal-access-tokens/{id}");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }

    private async Task<HttpRequestMessage> CreateAuthorizedRequestAsync(
        HttpMethod method,
        string requestUri)
    {
        var context = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException(
                "Personal access token API calls require an active HTTP request.");
        var accessToken = await context.GetTokenAsync(
            WebAuthenticationDefaults.AuthenticationScheme,
            "access_token")
            ?? throw new InvalidOperationException(
                "The signed-in session does not contain an API access token.");
        var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken);
        return request;
    }
}
