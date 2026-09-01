using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using PirateForgeGitServer.Contracts.Git;
using PirateForgeGitServer.Web.Identity;

namespace PirateForgeGitServer.Web.Repositories;

public sealed class RepositoryApiClient(
    HttpClient httpClient,
    IHttpContextAccessor httpContextAccessor)
{
    public Task<IReadOnlyList<RepositoryResponse>> ListAsync(
        CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<RepositoryResponse>>(
            "/api/repositories",
            cancellationToken);

    public Task<IReadOnlyList<RepositoryBranchResponse>> ListBranchesAsync(
        string repositoryName,
        CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<RepositoryBranchResponse>>(
            $"/api/repositories/{Uri.EscapeDataString(repositoryName)}/branches",
            cancellationToken);

    public Task<IReadOnlyList<RepositoryTreeEntryResponse>> ListTreeAsync(
        string repositoryName,
        string branch,
        string? path,
        CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<RepositoryTreeEntryResponse>>(
            BuildRepositoryUrl(repositoryName, "tree", branch, path),
            cancellationToken);

    public Task<RepositoryFileResponse> ReadFileAsync(
        string repositoryName,
        string branch,
        string path,
        CancellationToken cancellationToken = default) =>
        GetAsync<RepositoryFileResponse>(
            BuildRepositoryUrl(repositoryName, "file", branch, path),
            cancellationToken);

    public Task<IReadOnlyList<RepositoryCommitResponse>> ListCommitsAsync(
        string repositoryName,
        string branch,
        CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<RepositoryCommitResponse>>(
            BuildRepositoryUrl(repositoryName, "commits", branch, null),
            cancellationToken);

    public async Task<byte[]> DownloadArchiveAsync(
        string repositoryName,
        string branch,
        CancellationToken cancellationToken = default)
    {
        using var request = await CreateRequestAsync(
            BuildRepositoryUrl(repositoryName, "archive", branch, null));
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private async Task<T> GetAsync<T>(
        string requestUri,
        CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(requestUri);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
            ?? throw new InvalidOperationException(
                "The repository API returned an empty response.");
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(string requestUri)
    {
        var context = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException(
                "Repository API calls require an active HTTP request.");
        var accessToken = await context.GetTokenAsync(
            WebAuthenticationDefaults.AuthenticationScheme,
            "access_token")
            ?? throw new InvalidOperationException(
                "The signed-in session does not contain an API access token.");
        var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken);
        return request;
    }

    private static string BuildRepositoryUrl(
        string repositoryName,
        string resource,
        string branch,
        string? path)
    {
        var url = $"/api/repositories/{Uri.EscapeDataString(repositoryName)}/{resource}"
            + $"?branch={Uri.EscapeDataString(branch)}";
        return string.IsNullOrEmpty(path)
            ? url
            : $"{url}&path={Uri.EscapeDataString(path)}";
    }
}
