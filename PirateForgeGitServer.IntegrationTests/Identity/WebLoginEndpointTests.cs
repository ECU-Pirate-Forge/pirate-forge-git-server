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

public sealed class WebLoginEndpointTests
{
    [Fact]
    public async Task LoginCreatesCookieAndRedirectsToLocalPage()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);
        using var loginPage = await client.GetAsync("/login?returnUrl=%2Fcourses");
        loginPage.EnsureSuccessStatusCode();
        var antiforgeryToken = GetAntiforgeryToken(
            await loginPage.Content.ReadAsStringAsync());
        Assert.NotEmpty(antiforgeryToken);

        using var response = await client.PostAsync(
            "/account/login",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["Email"] = "captain@example.edu",
                    ["Password"] = "correct horse battery staple",
                    ["ReturnUrl"] = "/courses",
                    ["__RequestVerificationToken"] = antiforgeryToken,
                }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/courses", response.Headers.Location?.OriginalString);
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(
                "__Host-PirateForge.Session=",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoginRejectsRequestsBeyondTheWebRateLimit()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);

        for (var attempt = 0; attempt < 20; attempt++)
        {
            using var response = await PostLoginAsync(client);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }

        using var rejected = await PostLoginAsync(client);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    private static WebApplicationFactory<App> CreateFactory() =>
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
                                "pirate-forge-tests",
                                Guid.NewGuid().ToString("N")),
                        }));
                builder.ConfigureTestServices(services =>
                    services.AddHttpClient<IdentityApiClient>(
                            client => client.BaseAddress =
                                new Uri("https://api.example.test"))
                        .ConfigurePrimaryHttpMessageHandler(
                            () => new IdentityApiHandler()));
            });

    private static HttpClient CreateClient(WebApplicationFactory<App> factory) =>
        factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost"),
                HandleCookies = true,
            });

    private static string GetAntiforgeryToken(string html) =>
        Regex.Match(
            html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")
        .Groups[1]
        .Value;

    private static async Task<HttpResponseMessage> PostLoginAsync(
        HttpClient client)
    {
        using var loginPage = await client.GetAsync("/login");
        var antiforgeryToken = GetAntiforgeryToken(
            await loginPage.Content.ReadAsStringAsync());
        return await client.PostAsync(
            "/account/login",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["Email"] = "captain@example.edu",
                    ["Password"] = "correct horse battery staple",
                    ["ReturnUrl"] = "/",
                    ["__RequestVerificationToken"] = antiforgeryToken,
                }));
    }

    private sealed class IdentityApiHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var json = request.RequestUri?.AbsolutePath switch
            {
                "/api/auth/login" =>
                    """
                    {
                      "tokenType": "Bearer",
                      "accessToken": "access-token",
                      "expiresIn": 3600,
                      "refreshToken": "refresh-token"
                    }
                    """,
                "/api/auth/me" =>
                    """
                    {
                      "id": "76ce1da1-134a-4fa8-a7a8-c99fab63cbad",
                      "email": "captain@example.edu",
                      "displayName": "Captain Flint",
                      "roles": ["Instructor"]
                    }
                    """,
                _ => throw new InvalidOperationException(
                    $"Unexpected identity API request: {request.RequestUri}"),
            };

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                });
        }
    }
}
