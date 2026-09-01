using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PirateForgeGitServer.Web.Components;
using PirateForgeGitServer.Web.Identity;

namespace PirateForgeGitServer.IntegrationTests.Identity;

public sealed class PersonalAccessTokenWebTests
{
    [Fact]
    public async Task UserCanCreateAndViewPersonalAccessTokenOnce()
    {
        var handler = new PersonalAccessTokenApiHandler();
        using var factory = CreateFactory(handler);
        using var client = CreateClient(factory);
        await LoginAsync(client);

        using var page = await client.GetAsync("/account/tokens");
        page.EnsureSuccessStatusCode();
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("Personal access tokens", html, StringComparison.Ordinal);
        Assert.Contains("Existing Git token", html, StringComparison.Ordinal);
        Assert.Contains("Active", html, StringComparison.Ordinal);
        Assert.Contains("Revoked Git token", html, StringComparison.Ordinal);
        Assert.Contains("Revoked", html, StringComparison.Ordinal);
        Assert.Contains("Expired Git token", html, StringComparison.Ordinal);
        Assert.Contains("Expired", html, StringComparison.Ordinal);
        Assert.Contains(
            "Revoke token Existing Git token",
            html,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            $"/account/tokens/{PersonalAccessTokenApiHandler.RevokedTokenId}/revoke",
            html,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            $"/account/tokens/{PersonalAccessTokenApiHandler.ExpiredTokenId}/revoke",
            html,
            StringComparison.Ordinal);

        using var createResponse = await client.PostAsync(
            "/account/tokens/create",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["Name"] = "Laptop",
                    ["__RequestVerificationToken"] = GetAntiforgeryToken(html),
                }));

        Assert.Equal(HttpStatusCode.Redirect, createResponse.StatusCode);
        Assert.Equal(
            "/account/tokens?created=true",
            createResponse.Headers.Location?.OriginalString);

        using var createdPage = await client.GetAsync("/account/tokens?created=true");
        var createdHtml = await createdPage.Content.ReadAsStringAsync();
        Assert.Contains("pf_pat_created_secret", createdHtml, StringComparison.Ordinal);
        Assert.Contains(
            "Copy this token now. It will not be shown again.",
            createdHtml,
            StringComparison.Ordinal);

        using var refreshedPage = await client.GetAsync("/account/tokens");
        Assert.DoesNotContain(
            "pf_pat_created_secret",
            await refreshedPage.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task UserCanRevokePersonalAccessToken()
    {
        var handler = new PersonalAccessTokenApiHandler();
        using var factory = CreateFactory(handler);
        using var client = CreateClient(factory);
        await LoginAsync(client);
        using var page = await client.GetAsync("/account/tokens");
        var html = await page.Content.ReadAsStringAsync();

        using var response = await client.PostAsync(
            $"/account/tokens/{PersonalAccessTokenApiHandler.ExistingTokenId}/revoke",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = GetAntiforgeryToken(html),
                }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(
            "/account/tokens?revoked=true",
            response.Headers.Location?.OriginalString);
        Assert.True(handler.TokenWasRevoked);
    }

    private static WebApplicationFactory<App> CreateFactory(
        PersonalAccessTokenApiHandler handler) =>
        new WebApplicationFactory<App>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["PirateForge:Storage:RootPath"] = Path.Combine(
                                Path.GetTempPath(),
                                "pirate-forge-token-web-tests",
                                Guid.NewGuid().ToString("N")),
                        }));
                builder.ConfigureTestServices(services =>
                {
                    services.AddHttpClient<IdentityApiClient>(
                            client => client.BaseAddress =
                                new Uri("https://api.example.test"))
                        .ConfigurePrimaryHttpMessageHandler(() => handler);
                    services.AddHttpClient<PersonalAccessTokenApiClient>(
                            client => client.BaseAddress =
                                new Uri("https://api.example.test"))
                        .ConfigurePrimaryHttpMessageHandler(() => handler);
                });
            });

    private static HttpClient CreateClient(WebApplicationFactory<App> factory) =>
        factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost"),
                HandleCookies = true,
            });

    private static async Task LoginAsync(HttpClient client)
    {
        using var loginPage = await client.GetAsync("/login");
        using var response = await client.PostAsync(
            "/account/login",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["Email"] = "student@example.edu",
                    ["Password"] = "correct horse battery staple",
                    ["ReturnUrl"] = "/account/tokens",
                    ["__RequestVerificationToken"] = GetAntiforgeryToken(
                        await loginPage.Content.ReadAsStringAsync()),
                }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private static string GetAntiforgeryToken(string html) =>
        Regex.Match(
            html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")
        .Groups[1]
        .Value;

    private sealed class PersonalAccessTokenApiHandler : HttpMessageHandler
    {
        public static readonly Guid ExistingTokenId =
            Guid.Parse("11111111-1111-1111-1111-111111111111");
        public static readonly Guid RevokedTokenId =
            Guid.Parse("33333333-3333-3333-3333-333333333333");
        public static readonly Guid ExpiredTokenId =
            Guid.Parse("44444444-4444-4444-4444-444444444444");

        public bool TokenWasRevoked { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath;
            var response = (request.Method.Method, path) switch
            {
                ("POST", "/api/auth/login") => Json(
                    """
                    {
                      "tokenType": "Bearer",
                      "accessToken": "access-token",
                      "expiresIn": 3600,
                      "refreshToken": "refresh-token"
                    }
                    """),
                ("GET", "/api/auth/me") => Json(
                    """
                    {
                      "id": "76ce1da1-134a-4fa8-a7a8-c99fab63cbad",
                      "email": "student@example.edu",
                      "displayName": "Anne Bonny",
                      "roles": ["Student"]
                    }
                    """),
                ("GET", "/api/personal-access-tokens/") => Json(
                    $$"""
                    [
                    {
                      "id": "{{ExistingTokenId}}",
                      "name": "Existing Git token",
                      "prefix": "pf_pat_existing",
                      "createdAt": "2026-08-01T12:00:00Z",
                      "expiresAt": null,
                      "revokedAt": null,
                      "lastUsedAt": null
                    },
                    {
                      "id": "{{RevokedTokenId}}",
                      "name": "Revoked Git token",
                      "prefix": "pf_pat_revoked",
                      "createdAt": "2026-08-01T12:00:00Z",
                      "expiresAt": null,
                      "revokedAt": "2026-08-02T12:00:00Z",
                      "lastUsedAt": null
                    },
                    {
                      "id": "{{ExpiredTokenId}}",
                      "name": "Expired Git token",
                      "prefix": "pf_pat_expired",
                      "createdAt": "2026-01-01T12:00:00Z",
                      "expiresAt": "2026-02-01T12:00:00Z",
                      "revokedAt": null,
                      "lastUsedAt": null
                    }
                    ]
                    """),
                ("POST", "/api/personal-access-tokens/") => Json(
                    """
                    {
                      "id": "22222222-2222-2222-2222-222222222222",
                      "name": "Laptop",
                      "prefix": "pf_pat_created",
                      "token": "pf_pat_created_secret",
                      "createdAt": "2026-08-09T12:00:00Z",
                      "expiresAt": null
                    }
                    """,
                    HttpStatusCode.Created),
                ("DELETE", var deletePath)
                    when deletePath ==
                        $"/api/personal-access-tokens/{ExistingTokenId}" =>
                    Revoke(),
                _ => throw new InvalidOperationException(
                    $"Unexpected identity API request: {request.Method} {path}"),
            };
            return Task.FromResult(response);
        }

        private HttpResponseMessage Revoke()
        {
            TokenWasRevoked = true;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        private static HttpResponseMessage Json(
            string value,
            HttpStatusCode statusCode = HttpStatusCode.OK) =>
            new(statusCode)
            {
                Content = new StringContent(value, Encoding.UTF8, "application/json"),
            };
    }
}
