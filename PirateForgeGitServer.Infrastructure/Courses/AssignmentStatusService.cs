using Microsoft.EntityFrameworkCore;
using PirateForgeGitServer.Domain.Identity;
using PirateForgeGitServer.Infrastructure.Git;
using PirateForgeGitServer.Infrastructure.Persistence;
using PirateForgeGitServer.Infrastructure.Storage;

namespace PirateForgeGitServer.Infrastructure.Courses;

public sealed class AssignmentStatusService(
    IDbContextFactory<PirateForgeDbContext> dbContextFactory,
    StoragePaths storagePaths)
{
    public async Task<AssignmentStatusResult> GetAsync(
        Guid courseId,
        Guid assignmentId,
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
            return AssignmentStatusResult.NotFound();
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
                return AssignmentStatusResult.NotFound();
            }

            if (membership.Role is not (
                ApplicationRoles.Instructor
                or ApplicationRoles.TeachingAssistant))
            {
                return AssignmentStatusResult.Forbidden();
            }
        }

        var assignment = await context.Assignments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.Id == assignmentId
                    && value.CourseId == courseId,
                cancellationToken);
        if (assignment is null)
        {
            return AssignmentStatusResult.NotFound();
        }

        var students = await (
                from membership in context.CourseMemberships.AsNoTracking()
                join user in context.Users.AsNoTracking()
                    on membership.UserId equals user.Id
                where membership.CourseId == courseId
                    && membership.Role == ApplicationRoles.Student
                orderby user.DisplayName, user.Email
                select new StatusStudent(
                    user.Id,
                    user.Email!,
                    user.DisplayName))
            .ToArrayAsync(cancellationToken);
        var repositories = await (
                from link in context.AssignmentRepositories.AsNoTracking()
                join repository in context.GitRepositories.AsNoTracking()
                    on link.RepositoryId equals repository.Id
                where link.AssignmentId == assignmentId
                select new StatusRepository(
                    link.StudentUserId,
                    repository.Name,
                    repository.DisplayName))
            .ToDictionaryAsync(value => value.StudentUserId, cancellationToken);
        var statuses = new StudentAssignmentStatusView[students.Length];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, students.Length),
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = 8,
            },
            async (index, token) =>
            {
                var student = students[index];
                statuses[index] = repositories.TryGetValue(
                    student.Id,
                    out var repository)
                    ? await ReadRepositoryStatusAsync(student, repository, token)
                    : StudentAssignmentStatusView.NotProvisioned(
                        student.Id,
                        student.Email,
                        student.DisplayName);
            });

        return AssignmentStatusResult.Success(
            assignment.Id,
            assignment.CourseId,
            assignment.Slug,
            assignment.Title,
            statuses);
    }

    private async Task<StudentAssignmentStatusView> ReadRepositoryStatusAsync(
        StatusStudent student,
        StatusRepository repository,
        CancellationToken cancellationToken)
    {
        try
        {
            var repositoryPath = Path.Combine(
                storagePaths.RepositoriesPath,
                $"{repository.Name}.git");
            var branches = await GitRepositoryReader.ListBranchesAsync(
                repositoryPath,
                cancellationToken);
            if (branches.Count == 0)
            {
                return StudentAssignmentStatusView.Empty(
                    student.Id,
                    student.Email,
                    student.DisplayName,
                    repository.Name,
                    repository.DisplayName);
            }

            var branch = branches[0];
            for (var index = 1; index < branches.Count; index++)
            {
                if (branches[index].Name == "main")
                {
                    branch = branches[index];
                    break;
                }
            }

            var latestCommit = await GitRepositoryReader.ReadLatestCommitAsync(
                repositoryPath,
                branch.Name,
                cancellationToken);
            return StudentAssignmentStatusView.Active(
                student.Id,
                student.Email,
                student.DisplayName,
                repository.Name,
                repository.DisplayName,
                branch.Name,
                latestCommit);
        }
        catch (Exception exception)
            when (exception is GitCommandException or ArgumentException)
        {
            return StudentAssignmentStatusView.Unavailable(
                student.Id,
                student.Email,
                student.DisplayName,
                repository.Name,
                repository.DisplayName);
        }
    }

    private sealed record StatusStudent(
        Guid Id,
        string Email,
        string DisplayName);

    private sealed record StatusRepository(
        Guid StudentUserId,
        string Name,
        string DisplayName);
}

public enum AssignmentStatusAccess
{
    Success,
    NotFound,
    Forbidden,
}

public sealed record AssignmentStatusResult(
    AssignmentStatusAccess Access,
    Guid AssignmentId,
    Guid CourseId,
    string Slug,
    string Title,
    IReadOnlyList<StudentAssignmentStatusView> Students)
{
    public static AssignmentStatusResult Success(
        Guid assignmentId,
        Guid courseId,
        string slug,
        string title,
        IReadOnlyList<StudentAssignmentStatusView> students) =>
        new(
            AssignmentStatusAccess.Success,
            assignmentId,
            courseId,
            slug,
            title,
            students);

    public static AssignmentStatusResult NotFound() =>
        Empty(AssignmentStatusAccess.NotFound);

    public static AssignmentStatusResult Forbidden() =>
        Empty(AssignmentStatusAccess.Forbidden);

    private static AssignmentStatusResult Empty(AssignmentStatusAccess access) =>
        new(access, Guid.Empty, Guid.Empty, string.Empty, string.Empty, []);
}

public sealed record StudentAssignmentStatusView(
    Guid StudentUserId,
    string StudentEmail,
    string StudentDisplayName,
    string State,
    string? RepositoryName,
    string? RepositoryDisplayName,
    string? Branch,
    GitCommitInfo? LatestCommit)
{
    public static StudentAssignmentStatusView NotProvisioned(
        Guid userId,
        string email,
        string displayName) =>
        new(
            userId,
            email,
            displayName,
            "NotProvisioned",
            null,
            null,
            null,
            null);

    public static StudentAssignmentStatusView Empty(
        Guid userId,
        string email,
        string displayName,
        string repositoryName,
        string repositoryDisplayName) =>
        new(
            userId,
            email,
            displayName,
            "Empty",
            repositoryName,
            repositoryDisplayName,
            null,
            null);

    public static StudentAssignmentStatusView Active(
        Guid userId,
        string email,
        string displayName,
        string repositoryName,
        string repositoryDisplayName,
        string branch,
        GitCommitInfo? latestCommit) =>
        new(
            userId,
            email,
            displayName,
            "Active",
            repositoryName,
            repositoryDisplayName,
            branch,
            latestCommit);

    public static StudentAssignmentStatusView Unavailable(
        Guid userId,
        string email,
        string displayName,
        string repositoryName,
        string repositoryDisplayName) =>
        new(
            userId,
            email,
            displayName,
            "Unavailable",
            repositoryName,
            repositoryDisplayName,
            null,
            null);
}
