using System.Security.Claims;
using PirateForgeGitServer.Contracts.Courses;
using PirateForgeGitServer.Domain.Identity;
using PirateForgeGitServer.Infrastructure.Courses;

namespace PirateForgeGitServer.ApiService.Courses;

internal static class CourseAuditEndpoints
{
    public static IEndpointRouteBuilder MapCourseAuditEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/courses/{courseId:guid}/audit-events",
                async Task<IResult> (
                    Guid courseId,
                    ClaimsPrincipal principal,
                    CourseAuditService auditService,
                    CancellationToken cancellationToken) =>
                {
                    var result = await auditService.ListAsync(
                        courseId,
                        GetUserId(principal),
                        principal.IsInRole(ApplicationRoles.Administrator),
                        cancellationToken);
                    return result.Access switch
                    {
                        CourseAuditAccess.Success => TypedResults.Ok(
                            result.Events.Select(view =>
                                new CourseAuditEventResponse(
                                    view.Event.Id,
                                    view.Event.CourseId,
                                    view.Event.AssignmentId,
                                    view.Event.RepositoryId,
                                    view.RepositoryName,
                                    view.Event.ActorUserId,
                                    view.ActorEmail,
                                    view.ActorDisplayName,
                                    view.Event.Action,
                                    view.Event.CreatedAt))),
                        CourseAuditAccess.Forbidden => TypedResults.Forbid(),
                        _ => TypedResults.NotFound(),
                    };
                })
            .WithTags("Course Audit")
            .RequireAuthorization();
        return endpoints;
    }

    private static Guid GetUserId(ClaimsPrincipal principal) =>
        Guid.Parse(
            principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException(
                "The user identifier is missing."));
}
