using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using PirateForgeGitServer.Contracts.Courses;
using PirateForgeGitServer.Web.Identity;

namespace PirateForgeGitServer.Web.Courses;

public sealed class CourseApiClient(
    HttpClient httpClient,
    IHttpContextAccessor httpContextAccessor)
{
    public Task<IReadOnlyList<CourseResponse>> ListAsync(
        CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<CourseResponse>>(
            "/api/courses",
            cancellationToken);

    public async Task<CourseResponse?> GetAsync(
        Guid courseId,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Get,
            $"/api/courses/{courseId}",
            content: null,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CourseResponse>(
            cancellationToken)
            ?? throw new InvalidOperationException(
                "The course API returned an empty response.");
    }

    public Task<IReadOnlyList<CourseStaffCandidateResponse>> ListStaffCandidatesAsync(
        CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<CourseStaffCandidateResponse>>(
            "/api/courses/staff-candidates",
            cancellationToken);

    public Task<IReadOnlyList<CourseStudentResponse>> ListStudentsAsync(
        Guid courseId,
        CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<CourseStudentResponse>>(
            $"/api/courses/{courseId}/students",
            cancellationToken);

    public Task<IReadOnlyList<CourseAuditEventResponse>> ListAuditEventsAsync(
        Guid courseId,
        CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<CourseAuditEventResponse>>(
            $"/api/courses/{courseId}/audit-events",
            cancellationToken);

    public async Task<CourseRosterImportResponse?> ImportRosterAsync(
        Guid courseId,
        string csv,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            $"/api/courses/{courseId}/roster/import",
            JsonContent.Create(new ImportCourseRosterRequest(csv)),
            cancellationToken);
        if (response.StatusCode is HttpStatusCode.BadRequest
            or HttpStatusCode.Forbidden
            or HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CourseRosterImportResponse>(
            cancellationToken)
            ?? throw new InvalidOperationException(
                "The course API returned an empty roster import response.");
    }

    public async Task<CourseCreationResult> CreateAsync(
        CreateCourseRequest course,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            "/api/courses",
            JsonContent.Create(course),
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return new(CourseCreationStatus.Duplicate, null);
        }

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            return new(CourseCreationStatus.Invalid, null);
        }

        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<CourseResponse>(
                cancellationToken)
            ?? throw new InvalidOperationException(
                "The course API returned an empty response.");
        return new(CourseCreationStatus.Created, created);
    }

    public async Task<bool> AddStaffAsync(
        Guid courseId,
        AddCourseStaffRequest staff,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            $"/api/courses/{courseId}/staff",
            JsonContent.Create(staff),
            cancellationToken);
        if (response.StatusCode is HttpStatusCode.Conflict
            or HttpStatusCode.UnprocessableEntity
            or HttpStatusCode.BadRequest
            or HttpStatusCode.Forbidden
            or HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }

    public async Task<bool> RemoveStaffAsync(
        Guid courseId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Delete,
            $"/api/courses/{courseId}/staff/{userId}",
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
                "The course API returned an empty response.");
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string requestUri,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        var context = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException(
                "Course API calls require an active HTTP request.");
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

public sealed record CourseCreationResult(
    CourseCreationStatus Status,
    CourseResponse? Course);

public enum CourseCreationStatus
{
    Created,
    Duplicate,
    Invalid,
}
