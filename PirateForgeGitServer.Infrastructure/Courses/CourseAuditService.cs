using Microsoft.EntityFrameworkCore;
using PirateForgeGitServer.Domain.Courses;
using PirateForgeGitServer.Domain.Identity;
using PirateForgeGitServer.Infrastructure.Persistence;

namespace PirateForgeGitServer.Infrastructure.Courses;

public sealed class CourseAuditService(
    IDbContextFactory<PirateForgeDbContext> dbContextFactory,
    TimeProvider timeProvider)
{
    public async Task RecordAsync(
        Guid courseId,
        Guid? assignmentId,
        Guid? repositoryId,
        Guid actorUserId,
        string action,
        CancellationToken cancellationToken)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(
            cancellationToken);
        context.CourseAuditEvents.Add(CourseAuditEvent.Create(
            courseId,
            assignmentId,
            repositoryId,
            actorUserId,
            action,
            timeProvider.GetUtcNow()));
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<CourseAuditListResult> ListAsync(
        Guid courseId,
        Guid actingUserId,
        bool isAdministrator,
        CancellationToken cancellationToken)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(
            cancellationToken);
        var courseExists = await context.Courses
            .AsNoTracking()
            .AnyAsync(value => value.Id == courseId, cancellationToken);
        if (!courseExists)
        {
            return CourseAuditListResult.NotFound();
        }

        if (!isAdministrator)
        {
            var membership = await context.CourseMemberships
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    value => value.CourseId == courseId
                        && value.UserId == actingUserId,
                    cancellationToken);
            if (membership is null)
            {
                return CourseAuditListResult.NotFound();
            }

            if (membership.Role != ApplicationRoles.Instructor)
            {
                return CourseAuditListResult.Forbidden();
            }
        }

        var events = await (
                from auditEvent in context.CourseAuditEvents.AsNoTracking()
                join actor in context.Users.AsNoTracking()
                    on auditEvent.ActorUserId equals actor.Id
                join repository in context.GitRepositories.AsNoTracking()
                    on auditEvent.RepositoryId equals repository.Id
                    into repositories
                from repository in repositories.DefaultIfEmpty()
                where auditEvent.CourseId == courseId
                select new CourseAuditEventView(
                    auditEvent,
                    repository == null ? null : repository.Name,
                    actor.Email!,
                    actor.DisplayName))
            .ToArrayAsync(cancellationToken);
        return CourseAuditListResult.Success(events
            .OrderByDescending(value => value.Event.CreatedAt)
            .Take(200)
            .ToArray());
    }
}

public enum CourseAuditAccess
{
    Success,
    NotFound,
    Forbidden,
}

public sealed record CourseAuditListResult(
    CourseAuditAccess Access,
    IReadOnlyList<CourseAuditEventView> Events)
{
    public static CourseAuditListResult Success(
        IReadOnlyList<CourseAuditEventView> events) =>
        new(CourseAuditAccess.Success, events);

    public static CourseAuditListResult NotFound() =>
        new(CourseAuditAccess.NotFound, []);

    public static CourseAuditListResult Forbidden() =>
        new(CourseAuditAccess.Forbidden, []);
}

public sealed record CourseAuditEventView(
    CourseAuditEvent Event,
    string? RepositoryName,
    string ActorEmail,
    string ActorDisplayName);
