using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PirateForgeGitServer.Contracts.Courses;
using PirateForgeGitServer.Domain.Identity;
using PirateForgeGitServer.Infrastructure.Courses;

namespace PirateForgeGitServer.ApiService.Courses;

internal static class AssignmentEndpoints
{
    public static IEndpointRouteBuilder MapAssignmentEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/courses/{courseId:guid}/assignments")
            .WithTags("Assignments")
            .RequireAuthorization();

        group.MapGet(
            "/",
            async Task<IResult> (
                Guid courseId,
                ClaimsPrincipal principal,
                CourseAssignmentService assignmentService,
                CancellationToken cancellationToken) =>
            {
                var result = await assignmentService.ListAsync(
                    courseId,
                    GetUserId(principal),
                    principal.IsInRole(ApplicationRoles.Administrator),
                    cancellationToken);
                return result.Access switch
                {
                    CourseAssignmentAccess.Success => TypedResults.Ok(
                        result.Assignments.Select(view => ToResponse(view))),
                    CourseAssignmentAccess.Forbidden => TypedResults.Forbid(),
                    _ => TypedResults.NotFound(),
                };
            });

        group.MapPost(
            "/",
            async Task<IResult> (
                Guid courseId,
                CreateAssignmentRequest request,
                ClaimsPrincipal principal,
                CourseAssignmentService assignmentService,
                AssignmentProvisioningService provisioningService,
                CancellationToken cancellationToken) =>
            {
                var userId = GetUserId(principal);
                var isAdministrator =
                    principal.IsInRole(ApplicationRoles.Administrator);
                var result = await assignmentService.CreateAsync(
                    courseId,
                    request.Slug,
                    request.Title,
                    request.Description,
                    request.TemplateRepositoryId,
                    request.DueAt,
                    userId,
                    isAdministrator,
                    request.AutoGradeEnabled,
                    request.MaxScore,
                    request.GradingCommand,
                    request.AskUnderstandingQuestions,
                    request.ReviewPrompt,
                    request.LocalAiModel,
                    request.IsGroupAssignment,
                    request.MinTeamSize,
                    request.MaxTeamSize,
                    request.RequireTeamSelection,
                    request.AllowTeamChanges,
                    cancellationToken);
                AssignmentProvisioningResult? provisioning = null;
                if (result.Access == CourseAssignmentAccess.Success)
                {
                    provisioning = await provisioningService.ProvisionAsync(
                        courseId,
                        result.Assignment!.Assignment.Id,
                        userId,
                        isAdministrator,
                        cancellationToken);
                    if (provisioning.Access
                        != AssignmentProvisioningAccess.Success)
                    {
                        return TypedResults.Problem(
                            "The assignment was created, but its student repositories could not be provisioned. Retry provisioning from the course page.",
                            statusCode:
                                StatusCodes.Status500InternalServerError);
                    }
                }

                return result.Access switch
                {
                    CourseAssignmentAccess.Success => TypedResults.Created(
                        $"/api/courses/{courseId}/assignments/{result.Assignment!.Assignment.Id}",
                        ToResponse(
                            result.Assignment,
                            provisioning!.Repositories.Count,
                            provisioning.Repositories.Count)),
                    CourseAssignmentAccess.Forbidden => TypedResults.Forbid(),
                    CourseAssignmentAccess.NotFound => TypedResults.NotFound(),
                    CourseAssignmentAccess.InvalidTemplate =>
                        TypedResults.UnprocessableEntity(
                            new { Error = result.Error }),
                    CourseAssignmentAccess.Invalid =>
                        TypedResults.ValidationProblem(
                            new Dictionary<string, string[]>
                            {
                                ["Assignment"] =
                                [
                                    result.Error
                                    ?? "The assignment is invalid.",
                                ],
                            }),
                    _ => TypedResults.Conflict(),
                };
            });

        group.MapGet(
            "/{assignmentId:guid}/teams",
            async Task<IResult> (
                Guid courseId,
                Guid assignmentId,
                ClaimsPrincipal principal,
                AssignmentTeamService teamService,
                CancellationToken cancellationToken) =>
            {
                var result = await teamService.GetForAssignmentAsync(
                    courseId,
                    assignmentId,
                    GetUserId(principal),
                    principal.IsInRole(ApplicationRoles.Administrator),
                    cancellationToken);
                return result.Access switch
                {
                    AssignmentTeamAccess.Success => TypedResults.Ok(
                        result.Teams.Select(ToResponse).ToArray()),
                    AssignmentTeamAccess.Forbidden => TypedResults.Forbid(),
                    AssignmentTeamAccess.NotFound => TypedResults.NotFound(),
                    AssignmentTeamAccess.Invalid => TypedResults.ValidationProblem(
                        new Dictionary<string, string[]>
                        {
                            ["Assignment"] =
                            [
                                "This assignment is not a valid group assignment.",
                            ],
                        }),
                    _ => TypedResults.Conflict(),
                };
            });

        group.MapPost(
            "/{assignmentId:guid}/teams",
            async Task<IResult> (
                Guid courseId,
                Guid assignmentId,
                CreateAssignmentTeamRequest request,
                ClaimsPrincipal principal,
                AssignmentTeamService teamService,
                CancellationToken cancellationToken) =>
            {
                var result = await teamService.CreateAsync(
                    courseId,
                    assignmentId,
                    GetUserId(principal),
                    principal.IsInRole(ApplicationRoles.Administrator),
                    request.MemberUserIds,
                    request.TeamName,
                    cancellationToken);
                return result.Access switch
                {
                    AssignmentTeamAccess.Success => TypedResults.Created(
                        $"/api/courses/{courseId}/assignments/{assignmentId}/teams/{result.Team!.TeamId}",
                        ToResponse(result.Team)),
                    AssignmentTeamAccess.Forbidden => TypedResults.Forbid(),
                    AssignmentTeamAccess.NotFound => TypedResults.NotFound(),
                    AssignmentTeamAccess.Invalid => TypedResults.ValidationProblem(
                        new Dictionary<string, string[]>
                        {
                            ["Team"] =
                            [
                                result.Error ?? "The selected team is invalid.",
                            ],
                        }),
                    AssignmentTeamAccess.Conflict => TypedResults.Conflict(),
                    _ => TypedResults.Conflict(),
                };
            });

        group.MapGet(
            "/{assignmentId:guid}/status",
            async Task<IResult> (
                Guid courseId,
                Guid assignmentId,
                ClaimsPrincipal principal,
                AssignmentStatusService statusService,
                CancellationToken cancellationToken) =>
            {
                var result = await statusService.GetAsync(
                    courseId,
                    assignmentId,
                    GetUserId(principal),
                    principal.IsInRole(ApplicationRoles.Administrator),
                    cancellationToken);
                return result.Access switch
                {
                    AssignmentStatusAccess.Success => TypedResults.Ok(
                        new AssignmentStatusResponse(
                            result.AssignmentId,
                            result.CourseId,
                            result.Slug,
                            result.Title,
                            result.Students.Count,
                            result.Students.Count(value =>
                                value.RepositoryName is not null),
                            result.Students.Select(ToResponse).ToArray())),
                    AssignmentStatusAccess.Forbidden => TypedResults.Forbid(),
                    _ => TypedResults.NotFound(),
                };
            });

        group.MapGet(
            "/{assignmentId:guid}/submissions/latest",
            async Task<IResult> (
                Guid courseId,
                Guid assignmentId,
                ClaimsPrincipal principal,
                AssignmentSubmissionService submissionService,
                CancellationToken cancellationToken) =>
            {
                var result = await submissionService.GetLatestForStudentAsync(
                    courseId,
                    assignmentId,
                    GetUserId(principal),
                    cancellationToken);
                return result is null
                    ? TypedResults.NotFound()
                    : TypedResults.Ok(ToSubmissionResponse(result));
            });

        group.MapGet(
            "/{assignmentId:guid}/submissions/archived",
            async Task<IResult> (
                Guid courseId,
                Guid assignmentId,
                ClaimsPrincipal principal,
                AssignmentSubmissionService submissionService,
                CancellationToken cancellationToken) =>
            {
                var results = await submissionService.ListArchivedAsync(
                    courseId,
                    assignmentId,
                    GetUserId(principal),
                    principal.IsInRole(ApplicationRoles.Administrator),
                    cancellationToken);
                return TypedResults.Ok(results.Select(ToSubmissionResponse));
            });

        group.MapPost(
            "/{assignmentId:guid}/submissions/{submissionId:guid}/archive",
            async Task<IResult> (
                Guid courseId,
                Guid assignmentId,
                Guid submissionId,
                ClaimsPrincipal principal,
                AssignmentSubmissionService submissionService,
                CancellationToken cancellationToken) =>
            {
                var archived = await submissionService.ArchiveAsync(
                    courseId,
                    assignmentId,
                    submissionId,
                    GetUserId(principal),
                    principal.IsInRole(ApplicationRoles.Administrator),
                    cancellationToken);
                return archived
                    ? TypedResults.NoContent()
                    : TypedResults.NotFound();
            });

        group.MapPost(
            "/{assignmentId:guid}/submissions/{submissionId:guid}/purge",
            async Task<IResult> (
                Guid courseId,
                Guid assignmentId,
                Guid submissionId,
                ClaimsPrincipal principal,
                AssignmentSubmissionService submissionService,
                CancellationToken cancellationToken) =>
            {
                var purged = await submissionService.PurgeAsync(
                    courseId,
                    assignmentId,
                    submissionId,
                    GetUserId(principal),
                    principal.IsInRole(ApplicationRoles.Administrator),
                    cancellationToken);
                return purged
                    ? TypedResults.NoContent()
                    : TypedResults.NotFound();
            });

        group.MapPost(
            "/{assignmentId:guid}/submissions/questions/{questionId:guid}/answer",
            async Task<IResult> (
                Guid courseId,
                Guid assignmentId,
                Guid questionId,
                [FromBody] string answer,
                ClaimsPrincipal principal,
                AssignmentSubmissionService submissionService,
                CancellationToken cancellationToken) =>
            {
                var question = await submissionService.SubmitAnswerAsync(
                    assignmentId,
                    questionId,
                    GetUserId(principal),
                    answer,
                    cancellationToken);
                if (question is null)
                {
                    return TypedResults.NotFound();
                }

                return TypedResults.Ok(
                    new AssignmentReviewQuestionResponse(
                        question.Id,
                        question.Question,
                        question.FocusArea,
                        question.StudentAnswer,
                        question.CreatedAt,
                        question.AnsweredAt));
            });

        group.MapGet(
            "/{assignmentId:guid}/archive",
            async Task<IResult> (
                Guid courseId,
                Guid assignmentId,
                ClaimsPrincipal principal,
                AssignmentArchiveService archiveService,
                CancellationToken cancellationToken) =>
            {
                var result = await archiveService.CreateAsync(
                    courseId,
                    assignmentId,
                    GetUserId(principal),
                    principal.IsInRole(ApplicationRoles.Administrator),
                    cancellationToken);
                return result.Access switch
                {
                    AssignmentArchiveAccess.Success => TypedResults.File(
                        result.Content!,
                        "application/zip",
                        result.FileName),
                    AssignmentArchiveAccess.Forbidden => TypedResults.Forbid(),
                    _ => TypedResults.NotFound(),
                };
            });

        group.MapPost(
            "/{assignmentId:guid}/repositories",
            async Task<IResult> (
                Guid courseId,
                Guid assignmentId,
                ClaimsPrincipal principal,
                AssignmentProvisioningService provisioningService,
                CancellationToken cancellationToken) =>
            {
                var result = await provisioningService.ProvisionAsync(
                    courseId,
                    assignmentId,
                    GetUserId(principal),
                    principal.IsInRole(ApplicationRoles.Administrator),
                    cancellationToken);
                return result.Access switch
                {
                    AssignmentProvisioningAccess.Success => TypedResults.Ok(
                        new AssignmentProvisioningResponse(
                            result.AssignmentId,
                            result.Repositories.Count,
                            result.CreatedRepositories,
                            result.ExistingRepositories,
                            result.Repositories.Select(ToResponse).ToArray())),
                    AssignmentProvisioningAccess.Forbidden =>
                        TypedResults.Forbid(),
                    AssignmentProvisioningAccess.Conflict =>
                        TypedResults.Conflict(),
                    _ => TypedResults.NotFound(),
                };
            });

        return endpoints;
    }

    private static AssignmentResponse ToResponse(
        AssignmentView view,
        int? studentCount = null,
        int? provisionedRepositoryCount = null) =>
        new(
            view.Assignment.Id,
            view.Assignment.CourseId,
            view.Assignment.Slug,
            view.Assignment.Title,
            view.Assignment.Description,
            view.Template is null
                ? null
                : new AssignmentTemplateResponse(
                    view.Template.Id,
                    view.Template.Name,
                    view.Template.DisplayName),
            view.Assignment.DueAt,
            view.Assignment.CreatedByUserId,
            view.Assignment.CreatedAt,
            studentCount ?? view.StudentCount,
            provisionedRepositoryCount ?? view.ProvisionedRepositoryCount,
            view.Assignment.AutoGradeEnabled,
            view.Assignment.MaxScore,
            view.Assignment.GradingCommand,
            view.Assignment.AskUnderstandingQuestions,
            view.Assignment.ReviewPrompt,
            view.Assignment.LocalAiModel,
            view.Assignment.IsGroupAssignment,
            view.Assignment.MinTeamSize,
            view.Assignment.MaxTeamSize,
            view.Assignment.RequireTeamSelection,
            view.Assignment.AllowTeamChanges);

    private static AssignmentRepositoryResponse ToResponse(
        AssignmentRepositoryView view) =>
        new(
            view.StudentUserId,
            view.StudentEmail,
            view.StudentDisplayName,
            view.RepositoryName,
            view.RepositoryDisplayName,
            view.ProvisionedAt);

    private static StudentAssignmentStatusResponse ToResponse(
        StudentAssignmentStatusView view) =>
        new(
            view.StudentUserId,
            view.StudentEmail,
            view.StudentDisplayName,
            view.State,
            view.RepositoryName,
            view.RepositoryDisplayName,
            view.Branch,
            view.LatestCommit is null
                ? null
                : new AssignmentLatestCommitResponse(
                    view.LatestCommit.Id,
                    view.LatestCommit.Subject,
                    view.LatestCommit.AuthorName,
                    view.LatestCommit.AuthorEmail,
                    view.LatestCommit.AuthoredAt));

    private static AssignmentTeamResponse ToResponse(
        AssignmentTeamView view) =>
        new(
            view.TeamId,
            view.AssignmentId,
            view.Name,
            view.RepositoryName,
            view.RepositoryDisplayName,
            view.Members.Select(ToResponse).ToArray(),
            view.Contributions.Select(ToResponse).ToArray());

    private static AssignmentTeamMemberResponse ToResponse(
        AssignmentTeamMemberView view) =>
        new(
            view.UserId,
            view.DisplayName,
            view.Email,
            view.JoinedAt);

    private static AssignmentTeamContributionResponse ToResponse(
        AssignmentTeamContributionView view) =>
        new(
            view.UserId,
            view.DisplayName,
            view.BranchCount,
            view.CommitCount,
            view.DocumentationCommitCount,
            view.UnitTestCommitCount,
            view.ImplementationCommitCount,
            view.LastContributionAt,
            view.UpdatedAt);

    private static AssignmentSubmissionResponse ToSubmissionResponse(
        Domain.Courses.AssignmentSubmissionRun run) =>
        new(
            run.Id,
            run.AssignmentId,
            run.StudentUserId,
            run.CommitSha,
            run.CreatedAt,
            run.CompletedAt,
            run.Status,
            run.Score,
            run.MaxScore,
            run.Summary,
            string.IsNullOrWhiteSpace(run.Error) ? null : run.Error,
            string.IsNullOrWhiteSpace(run.ReviewSummary) ? null : run.ReviewSummary,
            run.Questions.Select(value => new AssignmentReviewQuestionResponse(
                    value.Id,
                    value.Question,
                    value.FocusArea,
                    value.StudentAnswer,
                    value.CreatedAt,
                    value.AnsweredAt)).ToArray());

    private static Guid GetUserId(ClaimsPrincipal principal) =>
        Guid.Parse(
            principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException(
                "The user identifier is missing."));
}
