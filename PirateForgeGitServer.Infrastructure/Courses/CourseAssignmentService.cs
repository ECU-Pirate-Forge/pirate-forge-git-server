using Microsoft.EntityFrameworkCore;
using PirateForgeGitServer.Domain.Courses;
using PirateForgeGitServer.Domain.Git;
using PirateForgeGitServer.Domain.Identity;
using PirateForgeGitServer.Infrastructure.Persistence;

namespace PirateForgeGitServer.Infrastructure.Courses;

public sealed class CourseAssignmentService(
    IDbContextFactory<PirateForgeDbContext> dbContextFactory,
    TimeProvider timeProvider)
{
    public async Task<CourseAssignmentListResult> ListAsync(
        Guid courseId,
        Guid actingUserId,
        bool isAdministrator,
        CancellationToken cancellationToken)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(
            cancellationToken);
        var access = await GetAccessAsync(
            context,
            courseId,
            actingUserId,
            isAdministrator,
            requireInstructor: false,
            cancellationToken);
        if (access != CourseAssignmentAccess.Success)
        {
            return new(access, []);
        }

        var assignments = await context.Assignments
            .AsNoTracking()
            .Where(value => value.CourseId == courseId)
            .OrderBy(value => value.Title)
            .ToArrayAsync(cancellationToken);
        return new(
            CourseAssignmentAccess.Success,
            await BuildViewsAsync(context, assignments, cancellationToken));
    }

    public async Task<CourseAssignmentCreateResult> CreateAsync(
        Guid courseId,
        string slug,
        string title,
        string? description,
        Guid? templateRepositoryId,
        DateTimeOffset? dueAt,
        Guid actingUserId,
        bool isAdministrator,
        bool autoGradeEnabled = true,
        int maxScore = 100,
        string? gradingCommand = null,
        bool askUnderstandingQuestions = true,
        string? reviewPrompt = null,
        string? localAiModel = null,
        bool isGroupAssignment = false,
        int? minTeamSize = null,
        int? maxTeamSize = null,
        bool requireTeamSelection = true,
        bool allowTeamChanges = false,
        CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(
            cancellationToken);
        var access = await GetAccessAsync(
            context,
            courseId,
            actingUserId,
            isAdministrator,
            requireInstructor: true,
            cancellationToken);
        if (access != CourseAssignmentAccess.Success)
        {
            return new(access, null, null);
        }

        if (description is null)
        {
            return new(
                CourseAssignmentAccess.Invalid,
                null,
                "An assignment description is required.");
        }

        GitRepository? template = null;
        if (templateRepositoryId.HasValue)
        {
            template = await context.GitRepositories
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    repository =>
                        repository.Id == templateRepositoryId.Value
                        && repository.Purpose == RepositoryPurpose.Template
                        && (isAdministrator
                            || repository.OwnerUserId == actingUserId),
                    cancellationToken);
            if (template is null)
            {
                return new(
                    CourseAssignmentAccess.InvalidTemplate,
                    null,
                    "The selected repository template is not available.");
            }
        }

        Assignment assignment;
        try
        {
            assignment = Assignment.Create(
                courseId,
                slug,
                title,
                description,
                templateRepositoryId,
                dueAt,
                actingUserId,
                timeProvider.GetUtcNow(),
                gradingCommand,
                maxScore,
                autoGradeEnabled,
                askUnderstandingQuestions,
                reviewPrompt,
                localAiModel,
                isGroupAssignment,
                minTeamSize,
                maxTeamSize,
                requireTeamSelection,
                allowTeamChanges);
        }
        catch (ArgumentException exception)
        {
            return new(CourseAssignmentAccess.Invalid, null, exception.Message);
        }

        if (await context.Assignments.AnyAsync(
                value => value.CourseId == courseId
                    && value.Slug == assignment.Slug,
                cancellationToken))
        {
            return new(CourseAssignmentAccess.Conflict, null, null);
        }

        context.Assignments.Add(assignment);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is Microsoft.Data.Sqlite.SqliteException
            {
                SqliteErrorCode: 19,
            })
        {
            return new(CourseAssignmentAccess.Conflict, null, null);
        }

        return new(
            CourseAssignmentAccess.Success,
            (await BuildViewsAsync(
                context,
                [assignment],
                cancellationToken))[0],
            null);
    }

    private static async Task<CourseAssignmentAccess> GetAccessAsync(
        PirateForgeDbContext context,
        Guid courseId,
        Guid actingUserId,
        bool isAdministrator,
        bool requireInstructor,
        CancellationToken cancellationToken)
    {
        var course = await context.Courses
            .AsNoTracking()
            .Include(value => value.Memberships)
            .SingleOrDefaultAsync(value => value.Id == courseId, cancellationToken);
        if (course is null)
        {
            return CourseAssignmentAccess.NotFound;
        }

        if (isAdministrator)
        {
            return CourseAssignmentAccess.Success;
        }

        var membership = course.Memberships.SingleOrDefault(
            value => value.UserId == actingUserId);
        if (membership is null)
        {
            return CourseAssignmentAccess.NotFound;
        }

        return !requireInstructor
            || membership.Role == ApplicationRoles.Instructor
                ? CourseAssignmentAccess.Success
                : CourseAssignmentAccess.Forbidden;
    }

    private static async Task<IReadOnlyList<AssignmentView>> BuildViewsAsync(
        PirateForgeDbContext context,
        IReadOnlyList<Assignment> assignments,
        CancellationToken cancellationToken)
    {
        var templateIds = assignments
            .Where(value => value.TemplateRepositoryId.HasValue)
            .Select(value => value.TemplateRepositoryId!.Value)
            .Distinct()
            .ToArray();
        var templates = await context.GitRepositories
            .AsNoTracking()
            .Where(value => templateIds.Contains(value.Id))
            .ToDictionaryAsync(value => value.Id, cancellationToken);
        var courseIds = assignments
            .Select(value => value.CourseId)
            .Distinct()
            .ToArray();
        var studentCounts = await context.CourseMemberships
            .AsNoTracking()
            .Where(value =>
                courseIds.Contains(value.CourseId)
                && value.Role == ApplicationRoles.Student)
            .GroupBy(value => value.CourseId)
            .Select(group => new { CourseId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(value => value.CourseId, value => value.Count, cancellationToken);
        var assignmentIds = assignments.Select(value => value.Id).ToArray();
        var repositoryCounts = await context.AssignmentRepositories
            .AsNoTracking()
            .Where(value => assignmentIds.Contains(value.AssignmentId))
            .GroupBy(value => value.AssignmentId)
            .Select(group => new
            {
                AssignmentId = group.Key,
                Count = group.Count(),
            })
            .ToDictionaryAsync(
                value => value.AssignmentId,
                value => value.Count,
                cancellationToken);
        return assignments.Select(assignment => new AssignmentView(
                assignment,
                assignment.TemplateRepositoryId.HasValue
                    ? templates.GetValueOrDefault(
                        assignment.TemplateRepositoryId.Value)
                    : null,
                studentCounts.GetValueOrDefault(assignment.CourseId),
                repositoryCounts.GetValueOrDefault(assignment.Id)))
            .ToArray();
    }
}

public enum CourseAssignmentAccess
{
    Success,
    NotFound,
    Forbidden,
    Invalid,
    InvalidTemplate,
    Conflict,
}

public sealed record CourseAssignmentListResult(
    CourseAssignmentAccess Access,
    IReadOnlyList<AssignmentView> Assignments);

public sealed record CourseAssignmentCreateResult(
    CourseAssignmentAccess Access,
    AssignmentView? Assignment,
    string? Error);

public sealed record AssignmentView(
    Assignment Assignment,
    GitRepository? Template,
    int StudentCount,
    int ProvisionedRepositoryCount);
