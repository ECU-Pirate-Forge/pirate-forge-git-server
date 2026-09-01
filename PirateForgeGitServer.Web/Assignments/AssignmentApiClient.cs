using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using PirateForgeGitServer.Contracts.Courses;
using PirateForgeGitServer.Contracts.Git;
using PirateForgeGitServer.Web.Identity;

namespace PirateForgeGitServer.Web.Assignments;

public sealed class AssignmentApiClient(
    HttpClient httpClient,
    IHttpContextAccessor httpContextAccessor)
{
    public Task<IReadOnlyList<RepositoryTemplateResponse>> ListTemplatesAsync(
        CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<RepositoryTemplateResponse>>(
            "/api/repository-templates",
            cancellationToken);

    public async Task<RepositoryTemplateResponse?> CreateTemplateAsync(
        CreateRepositoryRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            "/api/repository-templates",
            JsonContent.Create(request),
            cancellationToken);
        if (response.StatusCode is HttpStatusCode.BadRequest
            or HttpStatusCode.Conflict)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RepositoryTemplateResponse>(
            cancellationToken)
            ?? throw new InvalidOperationException(
                "The template API returned an empty response.");
    }

    public Task<IReadOnlyList<AssignmentResponse>> ListAssignmentsAsync(
        Guid courseId,
        CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<AssignmentResponse>>(
            $"/api/courses/{courseId}/assignments",
            cancellationToken);

    public async Task<AssignmentStatusResponse?> GetStatusAsync(
        Guid courseId,
        Guid assignmentId,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Get,
            $"/api/courses/{courseId}/assignments/{assignmentId}/status",
            content: null,
            cancellationToken);
        if (response.StatusCode is HttpStatusCode.Forbidden
            or HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AssignmentStatusResponse>(
            cancellationToken)
            ?? throw new InvalidOperationException(
                "The assignment status API returned an empty response.");
    }

    public async Task<AssignmentArchiveDownloadResult> DownloadArchiveAsync(
        Guid courseId,
        Guid assignmentId,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Get,
            $"/api/courses/{courseId}/assignments/{assignmentId}/archive",
            content: null,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return new(AssignmentArchiveDownloadStatus.Forbidden, null);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new(AssignmentArchiveDownloadStatus.NotFound, null);
        }

        response.EnsureSuccessStatusCode();
        return new(
            AssignmentArchiveDownloadStatus.Success,
            await response.Content.ReadAsByteArrayAsync(cancellationToken));
    }

    public async Task<AssignmentCreationResult> CreateAssignmentAsync(
        Guid courseId,
        CreateAssignmentRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            $"/api/courses/{courseId}/assignments",
            JsonContent.Create(request),
            cancellationToken);
        if (response.StatusCode is HttpStatusCode.BadRequest
            or HttpStatusCode.Conflict
            or HttpStatusCode.Forbidden
            or HttpStatusCode.NotFound
            or HttpStatusCode.UnprocessableEntity)
        {
            return new(AssignmentCreationStatus.Invalid, null);
        }

        if (response.StatusCode == HttpStatusCode.InternalServerError)
        {
            return new(AssignmentCreationStatus.ProvisioningFailed, null);
        }

        response.EnsureSuccessStatusCode();
        return new(
            AssignmentCreationStatus.Created,
            await response.Content.ReadFromJsonAsync<AssignmentResponse>(
                cancellationToken)
                ?? throw new InvalidOperationException(
                    "The assignment API returned an empty response."));
    }

    public async Task<bool> ProvisionRepositoriesAsync(
        Guid courseId,
        Guid assignmentId,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            $"/api/courses/{courseId}/assignments/{assignmentId}/repositories",
            content: null,
            cancellationToken);
        if (response.StatusCode is HttpStatusCode.Conflict
            or HttpStatusCode.Forbidden
            or HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }

    private async Task<T> GetAsync<T>(
        string requestUri,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            HttpMethod.Get,
            requestUri,
            content: null,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
            ?? throw new InvalidOperationException(
                "The assignment API returned an empty response.");
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string requestUri,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        var context = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException(
                "Assignment API calls require an active HTTP request.");
        var accessToken = await context.GetTokenAsync(
            WebAuthenticationDefaults.AuthenticationScheme,
            "access_token")
            ?? throw new InvalidOperationException(
                "The signed-in session does not contain an API access token.");
        using var request = new HttpRequestMessage(method, requestUri)
        {
            Content = content,
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken);
        return await httpClient.SendAsync(request, cancellationToken);
    }
}

public enum AssignmentCreationStatus
{
    Created,
    Invalid,
    ProvisioningFailed,
}

public sealed record AssignmentCreationResult(
    AssignmentCreationStatus Status,
    AssignmentResponse? Assignment);

public enum AssignmentArchiveDownloadStatus
{
    Success,
    NotFound,
    Forbidden,
}

public sealed record AssignmentArchiveDownloadResult(
    AssignmentArchiveDownloadStatus Status,
    byte[]? Content);
