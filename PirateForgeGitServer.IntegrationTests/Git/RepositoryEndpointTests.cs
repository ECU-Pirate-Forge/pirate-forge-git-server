using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace PirateForgeGitServer.IntegrationTests.Git;

public sealed class RepositoryEndpointTests(ApiFactory factory)
    : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task AdministratorCanCreateABareRepository()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await LoginAsync(client));
        var name = $"template-{Guid.NewGuid():N}";

        using var response = await client.PostAsJsonAsync(
            "/api/repositories",
            new { Name = name, DisplayName = "Course template" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(name, payload.GetProperty("name").GetString());
        Assert.True(
            Directory.Exists(
                Path.Combine(factory.StorageRoot, "repositories", $"{name}.git")));
    }

    [Fact]
    public async Task AdministratorCanListRepositories()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await LoginAsync(client));
        var name = $"listed-{Guid.NewGuid():N}";
        using var createResponse = await client.PostAsJsonAsync(
            "/api/repositories",
            new { Name = name, DisplayName = "Listed repository" });
        createResponse.EnsureSuccessStatusCode();

        var repositories = await client.GetFromJsonAsync<JsonElement>(
            "/api/repositories");

        Assert.Contains(
            repositories.EnumerateArray(),
            repository => repository.GetProperty("name").GetString() == name);
    }

    [Fact]
    public async Task InfoRefsReturnsUploadPackAdvertisementForOwner()
    {
        using var client = factory.CreateClient();
        var bearerToken = await LoginAsync(client);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", bearerToken);
        var name = $"clone-{Guid.NewGuid():N}";
        using var createRepositoryResponse = await client.PostAsJsonAsync(
            "/api/repositories",
            new { Name = name, DisplayName = "Clone test" });
        createRepositoryResponse.EnsureSuccessStatusCode();
        using var createTokenResponse = await client.PostAsJsonAsync(
            "/api/personal-access-tokens",
            new { Name = "Git CLI", ExpiresAt = (DateTimeOffset?)null });
        createTokenResponse.EnsureSuccessStatusCode();
        var createdToken =
            await createTokenResponse.Content.ReadFromJsonAsync<JsonElement>();
        var personalAccessToken = createdToken.GetProperty("token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(
                Encoding.UTF8.GetBytes(
                    $"{ApiFactory.AdministratorEmail}:{personalAccessToken}")));

        using var response = await client.GetAsync(
            $"/{name}.git/info/refs?service=git-upload-pack");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "application/x-git-upload-pack-advertisement",
            response.Content.Headers.ContentType?.MediaType);
        var advertisement = await response.Content.ReadAsByteArrayAsync();
        Assert.StartsWith(
            "001e# service=git-upload-pack\n0000",
            Encoding.ASCII.GetString(advertisement),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task GitEndpointChallengesAnonymousClientsForBasicAuthentication()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            $"/missing-{Guid.NewGuid():N}.git/info/refs?service=git-upload-pack");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(
            "Basic",
            Assert.Single(response.Headers.WwwAuthenticate).Scheme);
    }

    [Fact]
    public async Task OwnerCanListRepositoryBranches()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await LoginAsync(client));
        var name = $"branches-{Guid.NewGuid():N}";
        await CreateRepositoryWithCommitAsync(client, name);

        var branches = await client.GetFromJsonAsync<JsonElement>(
            $"/api/repositories/{name}/branches");

        var branch = Assert.Single(branches.EnumerateArray());
        Assert.Equal("main", branch.GetProperty("name").GetString());
        Assert.Equal(40, branch.GetProperty("commitId").GetString()?.Length);
    }

    [Fact]
    public async Task OwnerCanBrowseRepositoryTree()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await LoginAsync(client));
        var name = $"tree-{Guid.NewGuid():N}";
        await CreateRepositoryWithCommitAsync(client, name);

        var entries = await client.GetFromJsonAsync<JsonElement>(
            $"/api/repositories/{name}/tree?branch=main");

        Assert.Contains(
            entries.EnumerateArray(),
            entry => entry.GetProperty("name").GetString() == "README.md"
                && entry.GetProperty("kind").GetString() == "file");
        Assert.Contains(
            entries.EnumerateArray(),
            entry => entry.GetProperty("name").GetString() == "src"
                && entry.GetProperty("kind").GetString() == "directory");
    }

    [Fact]
    public async Task OwnerCanViewATextFile()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await LoginAsync(client));
        var name = $"file-{Guid.NewGuid():N}";
        await CreateRepositoryWithCommitAsync(client, name);

        var file = await client.GetFromJsonAsync<JsonElement>(
            $"/api/repositories/{name}/file?branch=main&path=src%2FProgram.cs");

        Assert.Equal("src/Program.cs", file.GetProperty("path").GetString());
        Assert.Equal("csharp", file.GetProperty("language").GetString());
        Assert.Equal(
            "Console.WriteLine(\"Ahoy!\");",
            file.GetProperty("content").GetString());
    }

    [Fact]
    public async Task OwnerCanViewCommitHistory()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await LoginAsync(client));
        var name = $"history-{Guid.NewGuid():N}";
        await CreateRepositoryWithCommitAsync(client, name);

        var commits = await client.GetFromJsonAsync<JsonElement>(
            $"/api/repositories/{name}/commits?branch=main");

        var commit = Assert.Single(commits.EnumerateArray());
        Assert.Equal("Add browser fixture", commit.GetProperty("subject").GetString());
        Assert.Equal("Browser Test", commit.GetProperty("authorName").GetString());
        Assert.Equal("browser@example.edu", commit.GetProperty("authorEmail").GetString());
    }

    [Fact]
    public async Task OwnerCanDownloadRepositoryArchive()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await LoginAsync(client));
        var name = $"archive-{Guid.NewGuid():N}";
        await CreateRepositoryWithCommitAsync(client, name);

        using var response = await client.GetAsync(
            $"/api/repositories/{name}/archive?branch=main");

        response.EnsureSuccessStatusCode();
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        Assert.Contains(archive.Entries, entry => entry.FullName == "README.md");
        Assert.Contains(archive.Entries, entry => entry.FullName == "src/Program.cs");
    }

    [Theory]
    [InlineData("tree?branch=main&path=..%2Fsrc")]
    [InlineData("tree?branch=--help")]
    [InlineData("file?branch=main&path=..%2FREADME.md")]
    [InlineData("archive?branch=--format%3Dtar")]
    public async Task RepositoryReadsRejectInvalidRefsAndPaths(string resource)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await LoginAsync(client));
        var name = $"invalid-read-{Guid.NewGuid():N}";
        await CreateRepositoryWithCommitAsync(client, name);

        using var response = await client.GetAsync(
            $"/api/repositories/{name}/{resource}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<string> LoginAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new
            {
                Email = ApiFactory.AdministratorEmail,
                Password = ApiFactory.AdministratorPassword,
            });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        return payload.GetProperty("accessToken").GetString()
            ?? throw new InvalidOperationException("Login did not return an access token.");
    }

    private async Task CreateRepositoryWithCommitAsync(
        HttpClient client,
        string name)
    {
        using var createResponse = await client.PostAsJsonAsync(
            "/api/repositories",
            new { Name = name, DisplayName = "Browser test" });
        createResponse.EnsureSuccessStatusCode();

        var workingDirectory = Path.Combine(
            Path.GetTempPath(),
            $"pirate-forge-browser-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workingDirectory);
        try
        {
            RunGit(workingDirectory, "init", "-b", "main", "--quiet");
            RunGit(workingDirectory, "config", "user.name", "Browser Test");
            RunGit(workingDirectory, "config", "user.email", "browser@example.edu");
            await File.WriteAllTextAsync(
                Path.Combine(workingDirectory, "README.md"),
                "# Browser test");
            Directory.CreateDirectory(Path.Combine(workingDirectory, "src"));
            await File.WriteAllTextAsync(
                Path.Combine(workingDirectory, "src", "Program.cs"),
                "Console.WriteLine(\"Ahoy!\");");
            RunGit(workingDirectory, "add", "README.md", "src/Program.cs");
            RunGit(workingDirectory, "commit", "-m", "Add browser fixture", "--quiet");
            RunGit(
                workingDirectory,
                "push",
                Path.Combine(factory.StorageRoot, "repositories", $"{name}.git"),
                "main",
                "--quiet");
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(
                         workingDirectory,
                         "*",
                         SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    private static void RunGit(string workingDirectory, params string[] arguments)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("Git did not start.");
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(
            process.ExitCode == 0,
            $"git {string.Join(' ', arguments)} failed: {error}");
    }
}
