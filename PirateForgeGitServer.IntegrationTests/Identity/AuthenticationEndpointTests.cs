using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace PirateForgeGitServer.IntegrationTests.Identity;

public sealed class AuthenticationEndpointTests(ApiFactory factory)
    : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task MeRequiresAuthentication()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProductionExposesReadinessAndLivenessEndpoints()
    {
        using var productionFactory = new ApiFactory()
            .WithWebHostBuilder(builder =>
                builder.UseEnvironment("Production"));
        using var client = productionFactory.CreateClient();

        using var readiness = await client.GetAsync("/health");
        using var liveness = await client.GetAsync("/alive");

        Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);
        Assert.Equal(HttpStatusCode.OK, liveness.StatusCode);
    }

    [Fact]
    public async Task LoginRejectsRequestsBeyondTheAuthenticationRateLimit()
    {
        using var isolatedFactory = new ApiFactory();
        using var client = isolatedFactory.CreateClient();

        for (var attempt = 0; attempt < 1_000; attempt++)
        {
            using var response = await client.PostAsJsonAsync(
                "/api/auth/login",
                new
                {
                    Email = $"missing-{attempt}@example.edu",
                    Password = "Incorrect-Password-42!"
                });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using var rejected = await client.PostAsJsonAsync(
            "/api/auth/login",
            new
            {
                Email = "missing-final@example.edu",
                Password = "Incorrect-Password-42!"
            });

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    [Fact]
    public async Task LoginReturnsBearerTokenForBootstrapAdministrator()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new
            {
                Email = ApiFactory.AdministratorEmail,
                Password = ApiFactory.AdministratorPassword
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(
            payload.GetProperty("accessToken").GetString()));
        Assert.Equal("Bearer", payload.GetProperty("tokenType").GetString());
    }

    [Fact]
    public async Task AdministratorCanCreateAndActivateAStudentAccount()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await LoginAsync(
                client,
                ApiFactory.AdministratorEmail,
                ApiFactory.AdministratorPassword));

        var email = $"student-{Guid.NewGuid():N}@example.edu";
        var createResponse = await client.PostAsJsonAsync(
            "/api/admin/users",
            new { Email = email, DisplayName = "Test Student", Role = "Student" });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var userId = created.GetProperty("id").GetGuid();

        var tokenResponse = await client.PostAsync(
            $"/api/admin/users/{userId}/activation-token",
            content: null);
        tokenResponse.EnsureSuccessStatusCode();
        var activation = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>();
        var activationToken = activation.GetProperty("token").GetString();

        client.DefaultRequestHeaders.Authorization = null;
        var activateResponse = await client.PostAsJsonAsync(
            "/api/auth/activate",
            new
            {
                UserId = userId,
                Token = activationToken,
                Password = "Student-Password-42!"
            });
        Assert.Equal(HttpStatusCode.NoContent, activateResponse.StatusCode);

        var studentToken = await LoginAsync(
            client,
            email,
            "Student-Password-42!");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", studentToken);
        var currentUser = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");

        Assert.Contains(
            currentUser.GetProperty("roles").EnumerateArray(),
            role => role.GetString() == "Student");
    }

    [Fact]
    public async Task AuthenticatedUserCanCreateListAndRevokeAPersonalAccessToken()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await LoginAsync(
                client,
                ApiFactory.AdministratorEmail,
                ApiFactory.AdministratorPassword));

        var createResponse = await client.PostAsJsonAsync(
            "/api/personal-access-tokens",
            new { Name = "Development laptop", ExpiresAt = (DateTimeOffset?)null });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.StartsWith(
            "pfg_",
            created.GetProperty("token").GetString(),
            StringComparison.Ordinal);
        var tokenId = created.GetProperty("id").GetGuid();

        var tokens = await client.GetFromJsonAsync<JsonElement>(
            "/api/personal-access-tokens");
        var listed = Assert.Single(tokens.EnumerateArray());
        Assert.Equal(tokenId, listed.GetProperty("id").GetGuid());
        Assert.False(listed.TryGetProperty("token", out _));

        var revokeResponse = await client.DeleteAsync(
            $"/api/personal-access-tokens/{tokenId}");
        Assert.Equal(HttpStatusCode.NoContent, revokeResponse.StatusCode);
    }

    [Fact]
    public async Task AdministratorCanDeactivateAnAccountAndPreventLogin()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await LoginAsync(
                client,
                ApiFactory.AdministratorEmail,
                ApiFactory.AdministratorPassword));
        var email = $"deactivated-{Guid.NewGuid():N}@example.edu";
        var createResponse = await client.PostAsJsonAsync(
            "/api/admin/users",
            new { Email = email, DisplayName = "Inactive Student", Role = "Student" });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var userId = created.GetProperty("id").GetGuid();
        var tokenResponse = await client.PostAsync(
            $"/api/admin/users/{userId}/activation-token",
            content: null);
        tokenResponse.EnsureSuccessStatusCode();
        var activation = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = null;
        var activateResponse = await client.PostAsJsonAsync(
            "/api/auth/activate",
            new
            {
                UserId = userId,
                Token = activation.GetProperty("token").GetString(),
                Password = "Student-Password-42!"
            });
        activateResponse.EnsureSuccessStatusCode();
        var studentAccessToken = await LoginAsync(
            client,
            email,
            "Student-Password-42!");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await LoginAsync(
                client,
                ApiFactory.AdministratorEmail,
                ApiFactory.AdministratorPassword));
        var deactivateResponse = await client.PutAsJsonAsync(
            $"/api/admin/users/{userId}/active",
            new { IsActive = false });
        Assert.Equal(HttpStatusCode.NoContent, deactivateResponse.StatusCode);

        client.DefaultRequestHeaders.Authorization = null;
        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { Email = email, Password = "Student-Password-42!" });
        Assert.Equal(HttpStatusCode.Unauthorized, loginResponse.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            studentAccessToken);
        var existingSessionResponse = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Forbidden, existingSessionResponse.StatusCode);
    }

    [Fact]
    public async Task AdministratorCanIssueAResetTokenForAUser()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await LoginAsync(
                client,
                ApiFactory.AdministratorEmail,
                ApiFactory.AdministratorPassword));
        var email = $"reset-{Guid.NewGuid():N}@example.edu";
        var createResponse = await client.PostAsJsonAsync(
            "/api/admin/users",
            new { Email = email, DisplayName = "Reset Student", Role = "Student" });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var userId = created.GetProperty("id").GetGuid();
        var activationTokenResponse = await client.PostAsync(
            $"/api/admin/users/{userId}/activation-token",
            content: null);
        activationTokenResponse.EnsureSuccessStatusCode();
        var activation = await activationTokenResponse.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = null;
        var activateResponse = await client.PostAsJsonAsync(
            "/api/auth/activate",
            new
            {
                UserId = userId,
                Token = activation.GetProperty("token").GetString(),
                Password = "Original-Password-42!"
            });
        activateResponse.EnsureSuccessStatusCode();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await LoginAsync(
                client,
                ApiFactory.AdministratorEmail,
                ApiFactory.AdministratorPassword));
        var resetTokenResponse = await client.PostAsync(
            $"/api/admin/users/{userId}/password-reset-token",
            content: null);
        resetTokenResponse.EnsureSuccessStatusCode();
        var resetToken = await resetTokenResponse.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = null;
        var resetResponse = await client.PostAsJsonAsync(
            "/api/auth/reset-password",
            new
            {
                UserId = userId,
                Token = resetToken.GetProperty("token").GetString(),
                Password = "Replacement-Password-42!"
            });
        Assert.Equal(HttpStatusCode.NoContent, resetResponse.StatusCode);

        var oldPasswordResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { Email = email, Password = "Original-Password-42!" });
        Assert.Equal(HttpStatusCode.Unauthorized, oldPasswordResponse.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(
            await LoginAsync(client, email, "Replacement-Password-42!")));
    }

    [Fact]
    public async Task StudentCannotUseAdministratorEndpoints()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await LoginAsync(
                client,
                ApiFactory.AdministratorEmail,
                ApiFactory.AdministratorPassword));
        var email = $"isolated-{Guid.NewGuid():N}@example.edu";
        var createResponse = await client.PostAsJsonAsync(
            "/api/admin/users",
            new { Email = email, DisplayName = "Isolated Student", Role = "Student" });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var userId = created.GetProperty("id").GetGuid();
        var tokenResponse = await client.PostAsync(
            $"/api/admin/users/{userId}/activation-token",
            content: null);
        tokenResponse.EnsureSuccessStatusCode();
        var activation = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = null;
        var activateResponse = await client.PostAsJsonAsync(
            "/api/auth/activate",
            new
            {
                UserId = userId,
                Token = activation.GetProperty("token").GetString(),
                Password = "Student-Password-42!"
            });
        activateResponse.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await LoginAsync(client, email, "Student-Password-42!"));

        var response = await client.GetAsync("/api/admin/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PersonalAccessTokenAuthenticatesWithHttpBasic()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await LoginAsync(
                client,
                ApiFactory.AdministratorEmail,
                ApiFactory.AdministratorPassword));
        var createResponse = await client.PostAsJsonAsync(
            "/api/personal-access-tokens",
            new { Name = "Git CLI", ExpiresAt = (DateTimeOffset?)null });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = created.GetProperty("token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{ApiFactory.AdministratorEmail}:{token}")));

        using var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<string> LoginAsync(
        HttpClient client,
        string email,
        string password)
    {
        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { Email = email, Password = password });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        return payload.GetProperty("accessToken").GetString()
            ?? throw new InvalidOperationException("Login did not return an access token.");
    }
}
