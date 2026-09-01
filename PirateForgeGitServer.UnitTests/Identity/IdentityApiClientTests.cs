using System.Net;
using System.Net.Http.Headers;
using System.Text;
using PirateForgeGitServer.Web.Identity;

namespace PirateForgeGitServer.UnitTests.Identity;

public sealed class IdentityApiClientTests
{
    [Fact]
    public async Task LoginAsyncReturnsAuthenticatedUserAndTokens()
    {
        var handler = new SequenceHandler(
            request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("/api/auth/login", request.RequestUri?.AbsolutePath);
                return JsonResponse(
                    """
                    {
                      "tokenType": "Bearer",
                      "accessToken": "access-token",
                      "expiresIn": 3600,
                      "refreshToken": "refresh-token"
                    }
                    """);
            },
            request =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal("/api/auth/me", request.RequestUri?.AbsolutePath);
                Assert.Equal(
                    new AuthenticationHeaderValue("Bearer", "access-token"),
                    request.Headers.Authorization);
                return JsonResponse(
                    """
                    {
                      "id": "76ce1da1-134a-4fa8-a7a8-c99fab63cbad",
                      "email": "captain@example.edu",
                      "displayName": "Captain Flint",
                      "roles": ["Instructor"]
                    }
                    """);
            });
        var client = CreateClient(handler);
        var beforeLogin = DateTimeOffset.UtcNow;

        var session = await client.LoginAsync(
            "captain@example.edu",
            "correct horse battery staple");

        Assert.NotNull(session);
        Assert.Equal("access-token", session.AccessToken);
        Assert.Equal("refresh-token", session.RefreshToken);
        Assert.Equal("captain@example.edu", session.User.Email);
        Assert.Equal(["Instructor"], session.User.Roles);
        Assert.InRange(
            session.ExpiresAt,
            beforeLogin.AddMinutes(59),
            beforeLogin.AddMinutes(61));
    }

    [Fact]
    public async Task LoginAsyncReturnsNullForInvalidCredentials()
    {
        var client = CreateClient(
            new SequenceHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)));

        var session = await client.LoginAsync(
            "captain@example.edu",
            "wrong password");

        Assert.Null(session);
    }

    private static IdentityApiClient CreateClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.test"),
        });

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private sealed class SequenceHandler(
        params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
        : HttpMessageHandler
    {
        private int index;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.True(index < responses.Length, "Received more HTTP requests than expected.");
            return Task.FromResult(responses[index++](request));
        }
    }
}
