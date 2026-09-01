using Microsoft.EntityFrameworkCore;
using PirateForgeGitServer.Domain.Courses;
using PirateForgeGitServer.Domain.Git;
using PirateForgeGitServer.Domain.Identity;
using PirateForgeGitServer.Infrastructure.Git;
using PirateForgeGitServer.Infrastructure.Persistence;
using PirateForgeGitServer.Infrastructure.Storage;

namespace PirateForgeGitServer.Infrastructure.Courses;

public sealed class AssignmentProvisioningService(
    IDbContextFactory<PirateForgeDbContext> dbContextFactory,
    StoragePaths storagePaths,
    TimeProvider timeProvider)
{
    private static readonly SemaphoreSlim ProvisioningLock = new(1, 1);

    public async Task<AssignmentProvisioningResult> ProvisionAsync(
        Guid courseId,
        Guid assignmentId,
        Guid actingUserId,
        bool isAdministrator,
        CancellationToken cancellationToken)
    {
        await ProvisioningLock.WaitAsync(cancellationToken);
        try
        {
            await using var context = await dbContextFactory.CreateDbContextAsync(
                cancellationToken);
            var course = await context.Courses
                .AsNoTracking()
                .SingleOrDefaultAsync(value => value.Id == courseId, cancellationToken);
            if (course is null)
            {
                return AssignmentProvisioningResult.NotFound();
            }

            if (!isAdministrator)
            {
                var isInstructor = await context.CourseMemberships
                    .AsNoTracking()
                    .AnyAsync(
                        membership =>
                            membership.CourseId == courseId
                            && membership.UserId == actingUserId
                            && membership.Role == ApplicationRoles.Instructor,
                        cancellationToken);
                if (!isInstructor)
                {
                    var isMember = await context.CourseMemberships
                        .AsNoTracking()
                        .AnyAsync(
                            membership =>
                                membership.CourseId == courseId
                                && membership.UserId == actingUserId,
                            cancellationToken);
                    return isMember
                        ? AssignmentProvisioningResult.Forbidden()
                        : AssignmentProvisioningResult.NotFound();
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
                return AssignmentProvisioningResult.NotFound();
            }

            var students = await (
                    from membership in context.CourseMemberships.AsNoTracking()
                    join user in context.Users.AsNoTracking()
                        on membership.UserId equals user.Id
                    where membership.CourseId == courseId
                        && membership.Role == ApplicationRoles.Student
                    orderby user.Email
                    select new ProvisioningStudent(
                        user.Id,
                        user.Email!,
                        user.DisplayName))
                .ToArrayAsync(cancellationToken);
            var views = new List<AssignmentRepositoryView>(students.Length);
            var createdCount = 0;

            foreach (var student in students)
            {
                var repositoryName = AssignmentRepository.CreateRepositoryName(
                    course.Code,
                    assignment.Slug,
                    student.Id);
                var existingLink = await context.AssignmentRepositories
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        value => value.AssignmentId == assignmentId
                            && value.StudentUserId == student.Id,
                        cancellationToken);
                GitRepository repository;
                AssignmentRepository link;
                if (existingLink is not null)
                {
                    repository = await context.GitRepositories
                        .AsNoTracking()
                        .SingleAsync(
                            value => value.Id == existingLink.RepositoryId,
                            cancellationToken);
                    link = existingLink;
                    await EnsureRepositoryExistsAsync(
                        repository.Name,
                        assignment.TemplateRepositoryId,
                        cancellationToken);
                }
                else
                {
                    repository = await context.GitRepositories
                        .SingleOrDefaultAsync(
                            value => value.Name == repositoryName,
                            cancellationToken)
                        ?? GitRepository.Create(
                            repositoryName,
                            $"{assignment.Title} - {student.DisplayName}",
                            student.Id,
                            timeProvider.GetUtcNow(),
                            RepositoryPurpose.StudentAssignment);
                    if (repository.OwnerUserId != student.Id
                        || repository.Purpose
                            != RepositoryPurpose.StudentAssignment)
                    {
                        return AssignmentProvisioningResult.Conflict();
                    }

                    var repositoryWasAdded =
                        context.Entry(repository).State == EntityState.Detached;
                    if (repositoryWasAdded)
                    {
                        context.GitRepositories.Add(repository);
                    }

                    link = AssignmentRepository.Create(
                        assignmentId,
                        student.Id,
                        repository.Id,
                        timeProvider.GetUtcNow());
                    context.AssignmentRepositories.Add(link);
                    var repositoryPath = GetRepositoryPath(repository.Name);
                    var directoryWasCreated = !Directory.Exists(repositoryPath);
                    try
                    {
                        await EnsureRepositoryExistsAsync(
                            repository.Name,
                            assignment.TemplateRepositoryId,
                            cancellationToken);
                        await context.SaveChangesAsync(cancellationToken);
                    }
                    catch
                    {
                        if (directoryWasCreated && Directory.Exists(repositoryPath))
                        {
                            Directory.Delete(repositoryPath, recursive: true);
                        }

                        throw;
                    }

                    createdCount++;
                }

                views.Add(new AssignmentRepositoryView(
                    student.Id,
                    student.Email,
                    student.DisplayName,
                    repository.Name,
                    repository.DisplayName,
                    link.ProvisionedAt));
            }

            return AssignmentProvisioningResult.Success(
                assignmentId,
                createdCount,
                students.Length - createdCount,
                views);
        }
        finally
        {
            ProvisioningLock.Release();
        }
    }

    private async Task EnsureRepositoryExistsAsync(
        string repositoryName,
        Guid? templateRepositoryId,
        CancellationToken cancellationToken)
    {
        var destinationPath = GetRepositoryPath(repositoryName);
        if (IsBareRepository(destinationPath))
        {
            return;
        }

        if (Directory.Exists(destinationPath))
        {
            NormalizeFileAttributes(destinationPath);
            Directory.Delete(destinationPath, recursive: true);
        }

        if (templateRepositoryId is null)
        {
            await GitProcessRunner.InitializeBareRepositoryAsync(
                destinationPath,
                cancellationToken);
            return;
        }

        await using var context = await dbContextFactory.CreateDbContextAsync(
            cancellationToken);
        var templateName = await context.GitRepositories
            .AsNoTracking()
            .Where(value => value.Id == templateRepositoryId.Value)
            .Select(value => value.Name)
            .SingleAsync(cancellationToken);
        await GitProcessRunner.CloneBareRepositoryAsync(
            GetRepositoryPath(templateName),
            destinationPath,
            cancellationToken);
    }

    private string GetRepositoryPath(string repositoryName) =>
        Path.Combine(storagePaths.RepositoriesPath, $"{repositoryName}.git");

    private static bool IsBareRepository(string repositoryPath) =>
        File.Exists(Path.Combine(repositoryPath, "HEAD"))
        && Directory.Exists(Path.Combine(repositoryPath, "objects"))
        && Directory.Exists(Path.Combine(repositoryPath, "refs"));

    private static void NormalizeFileAttributes(string repositoryPath)
    {
        foreach (var file in Directory.EnumerateFiles(
                     repositoryPath,
                     "*",
                     SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
    }

    private sealed record ProvisioningStudent(
        Guid Id,
        string Email,
        string DisplayName);
}

public enum AssignmentProvisioningAccess
{
    Success,
    NotFound,
    Forbidden,
    Conflict,
}

public sealed record AssignmentProvisioningResult(
    AssignmentProvisioningAccess Access,
    Guid AssignmentId,
    int CreatedRepositories,
    int ExistingRepositories,
    IReadOnlyList<AssignmentRepositoryView> Repositories)
{
    public static AssignmentProvisioningResult Success(
        Guid assignmentId,
        int createdRepositories,
        int existingRepositories,
        IReadOnlyList<AssignmentRepositoryView> repositories) =>
        new(
            AssignmentProvisioningAccess.Success,
            assignmentId,
            createdRepositories,
            existingRepositories,
            repositories);

    public static AssignmentProvisioningResult NotFound() =>
        Empty(AssignmentProvisioningAccess.NotFound);

    public static AssignmentProvisioningResult Forbidden() =>
        Empty(AssignmentProvisioningAccess.Forbidden);

    public static AssignmentProvisioningResult Conflict() =>
        Empty(AssignmentProvisioningAccess.Conflict);

    private static AssignmentProvisioningResult Empty(
        AssignmentProvisioningAccess access) =>
        new(access, Guid.Empty, 0, 0, []);
}

public sealed record AssignmentRepositoryView(
    Guid StudentUserId,
    string StudentEmail,
    string StudentDisplayName,
    string RepositoryName,
    string RepositoryDisplayName,
    DateTimeOffset ProvisionedAt);
