using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PirateForgeGitServer.Contracts.Courses;
using PirateForgeGitServer.Domain.Courses;
using PirateForgeGitServer.Domain.Identity;
using PirateForgeGitServer.Infrastructure.Courses;
using PirateForgeGitServer.Infrastructure.Identity;

namespace PirateForgeGitServer.ApiService.Courses;

internal static class CourseEndpoints
{
    public static IEndpointRouteBuilder MapCourseEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/courses")
            .WithTags("Courses")
            .RequireAuthorization();

        group.MapGet(
            "/",
            async (
                ClaimsPrincipal principal,
                CourseService courseService,
                UserManager<PirateForgeUser> userManager,
                CancellationToken cancellationToken) =>
            {
                var courses = await courseService.ListAccessibleAsync(
                    GetUserId(principal),
                    principal.IsInRole(ApplicationRoles.Administrator),
                    cancellationToken);
                var responses = new List<CourseResponse>(courses.Count);
                foreach (var course in courses)
                {
                    responses.Add(await ToResponseAsync(
                        course,
                        userManager));
                }

                return TypedResults.Ok(responses);
            });

        group.MapPost(
                "/",
                async Task<IResult> (
                    CreateCourseRequest request,
                    ClaimsPrincipal principal,
                    CourseService courseService,
                    UserManager<PirateForgeUser> userManager,
                    CancellationToken cancellationToken) =>
                {
                    Course course;
                    try
                    {
                        var created = await courseService.CreateAsync(
                            request.Code,
                            request.Name,
                            GetUserId(principal),
                            cancellationToken);
                        if (created is null)
                        {
                            return TypedResults.Conflict();
                        }

                        course = created;
                    }
                    catch (ArgumentException exception)
                    {
                        return TypedResults.ValidationProblem(
                            new Dictionary<string, string[]>
                            {
                                ["Course"] = [exception.Message],
                            });
                    }

                    return TypedResults.Created(
                        $"/api/courses/{course.Id}",
                        await ToResponseAsync(course, userManager));
                })
            .RequireAuthorization(AuthorizationPolicies.ManageCourses);

        group.MapGet(
                "/staff-candidates",
                async (UserManager<PirateForgeUser> userManager) =>
                {
                    var candidates = new List<CourseStaffCandidateResponse>();
                    foreach (var role in new[]
                             {
                                 ApplicationRoles.Instructor,
                                 ApplicationRoles.TeachingAssistant,
                             })
                    {
                        var users = await userManager.GetUsersInRoleAsync(role);
                        candidates.AddRange(users
                            .Where(user => user.IsActive)
                            .Select(user => new CourseStaffCandidateResponse(
                                user.Id,
                                user.Email ?? string.Empty,
                                user.DisplayName,
                                role)));
                    }

                    return TypedResults.Ok(candidates
                        .OrderBy(candidate => candidate.DisplayName)
                        .ThenBy(candidate => candidate.Email)
                        .ToArray());
                })
            .RequireAuthorization(AuthorizationPolicies.ManageCourses);

        group.MapGet(
            "/{courseId:guid}",
            async Task<IResult> (
                Guid courseId,
                ClaimsPrincipal principal,
                CourseService courseService,
                UserManager<PirateForgeUser> userManager,
                CancellationToken cancellationToken) =>
            {
                var course = await courseService.FindAccessibleAsync(
                    courseId,
                    GetUserId(principal),
                    principal.IsInRole(ApplicationRoles.Administrator),
                    cancellationToken);
                return course is null
                    ? TypedResults.NotFound()
                    : TypedResults.Ok(await ToResponseAsync(course, userManager));
            });

        group.MapGet(
            "/{courseId:guid}/students",
            async Task<IResult> (
               Guid courseId,
               ClaimsPrincipal principal,
               CourseRosterService rosterService,
               CancellationToken cancellationToken) =>
            {
                var result = await rosterService.ListAsync(
                    courseId,
                    GetUserId(principal),
                    principal.IsInRole(ApplicationRoles.Administrator),
                    cancellationToken);
                return result.Access switch
                {
                    CourseRosterAccess.Success => TypedResults.Ok(
                        result.Students.Select(student =>
                            new CourseStudentResponse(
                                student.UserId,
                                student.Email,
                                student.DisplayName,
                                student.EnrolledAt))),
                    CourseRosterAccess.Forbidden => TypedResults.Forbid(),
                    _ => TypedResults.NotFound(),
                };
            })
            .WithMetadata(new RequestSizeLimitAttribute(1_100_000));

        group.MapPost(
            "/{courseId:guid}/roster/import",
            async Task<IResult> (
               Guid courseId,
               ImportCourseRosterRequest request,
               ClaimsPrincipal principal,
               CourseRosterService rosterService,
               CancellationToken cancellationToken) =>
            {
                var result = await rosterService.ImportAsync(
                    courseId,
                    request.Csv,
                    GetUserId(principal),
                    principal.IsInRole(ApplicationRoles.Administrator),
                    cancellationToken);
                return result.Access switch
                {
                    CourseRosterAccess.Success => TypedResults.Ok(
                        new CourseRosterImportResponse(
                            result.Rows.Select(row =>
                                new CourseRosterImportRowResponse(
                                    row.RowNumber,
                                    row.Email,
                                    row.DisplayName,
                                    row.Status,
                                    row.UserId,
                                    row.ActivationToken,
                                    row.Message))
                                .ToArray())),
                    CourseRosterAccess.Invalid => TypedResults.ValidationProblem(
                        new Dictionary<string, string[]>
                        {
                            ["Roster"] = [result.Error ?? "The roster is invalid."],
                        }),
                    CourseRosterAccess.Forbidden => TypedResults.Forbid(),
                    _ => TypedResults.NotFound(),
                };
            });

        group.MapPost(
            "/{courseId:guid}/staff",
            async Task<IResult> (
                Guid courseId,
                AddCourseStaffRequest request,
                ClaimsPrincipal principal,
                CourseService courseService,
                CancellationToken cancellationToken) =>
            {
                var result = await courseService.AddStaffAsync(
                    courseId,
                    request.UserId,
                    request.Role,
                    GetUserId(principal),
                    principal.IsInRole(ApplicationRoles.Administrator),
                    cancellationToken);
                return result switch
                {
                    CourseStaffChangeResult.Success =>
                        TypedResults.Created(
                            $"/api/courses/{courseId}/staff/{request.UserId}",
                            new { request.UserId, request.Role }),
                    CourseStaffChangeResult.Forbidden => TypedResults.Forbid(),
                    CourseStaffChangeResult.NotFound
                        or CourseStaffChangeResult.UserNotFound =>
                        TypedResults.NotFound(),
                    CourseStaffChangeResult.InvalidRole
                        or CourseStaffChangeResult.RoleMismatch =>
                        TypedResults.ValidationProblem(
                            new Dictionary<string, string[]>
                            {
                                ["Staff"] =
                                [
                                    "The account must have the requested Instructor or TA role."
                                ],
                            }),
                    _ => TypedResults.Conflict(),
                };
            });

        group.MapDelete(
            "/{courseId:guid}/staff/{userId:guid}",
            async Task<IResult> (
                Guid courseId,
                Guid userId,
                ClaimsPrincipal principal,
                CourseService courseService,
                CancellationToken cancellationToken) =>
            {
                var result = await courseService.RemoveStaffAsync(
                    courseId,
                    userId,
                    GetUserId(principal),
                    principal.IsInRole(ApplicationRoles.Administrator),
                    cancellationToken);
                return result switch
                {
                    CourseStaffChangeResult.Success => TypedResults.NoContent(),
                    CourseStaffChangeResult.Forbidden => TypedResults.Forbid(),
                    CourseStaffChangeResult.NotFound => TypedResults.NotFound(),
                    _ => TypedResults.Conflict(),
                };
            });

        return endpoints;
    }

    private static Guid GetUserId(ClaimsPrincipal principal) =>
        Guid.Parse(
            principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException(
                "The user identifier is missing."));

    private static async Task<CourseResponse> ToResponseAsync(
        Course course,
        UserManager<PirateForgeUser> userManager)
    {
        var staff = new List<CourseStaffResponse>(course.Memberships.Count);
        foreach (var membership in course.Memberships
                     .Where(value => value.Role != ApplicationRoles.Student)
                     .OrderBy(value => value.Role)
                     .ThenBy(value => value.UserId))
        {
            var user = await userManager.FindByIdAsync(
                membership.UserId.ToString());
            staff.Add(new CourseStaffResponse(
                membership.UserId,
                user?.Email ?? string.Empty,
                user?.DisplayName ?? string.Empty,
                membership.Role,
                membership.AddedAt));
        }

        return new CourseResponse(
            course.Id,
            course.Code,
            course.Name,
            course.CreatedAt,
            staff);
    }
}
