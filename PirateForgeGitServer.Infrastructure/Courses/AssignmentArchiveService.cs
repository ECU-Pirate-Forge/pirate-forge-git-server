using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using PirateForgeGitServer.Infrastructure.Git;
using PirateForgeGitServer.Infrastructure.Persistence;
using PirateForgeGitServer.Infrastructure.Storage;

namespace PirateForgeGitServer.Infrastructure.Courses;

public sealed class AssignmentArchiveService(
    AssignmentStatusService statusService,
    IDbContextFactory<PirateForgeDbContext> dbContextFactory,
    StoragePaths storagePaths,
    CourseAuditService auditService)
{
    public async Task<AssignmentArchiveResult> CreateAsync(
        Guid courseId,
        Guid assignmentId,
        Guid actingUserId,
        bool isAdministrator,
        CancellationToken cancellationToken)
    {
        var status = await statusService.GetAsync(
            courseId,
            assignmentId,
            actingUserId,
            isAdministrator,
            cancellationToken);
        if (status.Access != AssignmentStatusAccess.Success)
        {
            return status.Access == AssignmentStatusAccess.Forbidden
                ? AssignmentArchiveResult.Forbidden()
                : AssignmentArchiveResult.NotFound();
        }

        await using var output = new MemoryStream();
        using (var archive = new ZipArchive(
                   output,
                   ZipArchiveMode.Create,
                   leaveOpen: true))
        {
            foreach (var student in status.Students.Where(value =>
                         value.RepositoryName is not null))
            {
                var prefix = $"{student.RepositoryName}/";
                if (student.State == "Active" && student.Branch is not null)
                {
                    var repositoryPath = Path.Combine(
                        storagePaths.RepositoriesPath,
                        $"{student.RepositoryName}.git");
                    var bytes = await GitRepositoryReader.CreateArchiveAsync(
                        repositoryPath,
                        student.Branch,
                        cancellationToken);
                    using var sourceStream = new MemoryStream(bytes);
                    using var sourceArchive = new ZipArchive(
                        sourceStream,
                        ZipArchiveMode.Read);
                    foreach (var sourceEntry in sourceArchive.Entries)
                    {
                        var entry = archive.CreateEntry(
                            prefix + sourceEntry.FullName,
                            CompressionLevel.Fastest);
                        await using var input = sourceEntry.Open();
                        await using var destination = entry.Open();
                        await input.CopyToAsync(destination, cancellationToken);
                    }
                }
                else
                {
                    var entry = archive.CreateEntry(
                        prefix + (student.State == "Unavailable"
                            ? "_UNAVAILABLE.txt"
                            : "_EMPTY.txt"));
                    await using var writer = new StreamWriter(entry.Open());
                    await writer.WriteAsync(
                        student.State == "Unavailable"
                            ? "Repository content is currently unavailable."
                            : "Repository has no branches.");
                }
            }
        }

        await auditService.RecordAsync(
            courseId,
            assignmentId,
            repositoryId: null,
            actingUserId,
            "AssignmentArchiveDownloaded",
            cancellationToken);
        return AssignmentArchiveResult.Success(
            output.ToArray(),
            $"{status.Slug}-repositories.zip");
    }

    public async Task RecordRepositoryDownloadAsync(
        Guid repositoryId,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(
            cancellationToken);
        var assignmentRepository = await (
                from link in context.AssignmentRepositories.AsNoTracking()
                join assignment in context.Assignments.AsNoTracking()
                    on link.AssignmentId equals assignment.Id
                where link.RepositoryId == repositoryId
                select new { assignment.CourseId, AssignmentId = assignment.Id })
            .SingleOrDefaultAsync(cancellationToken);
        if (assignmentRepository is null)
        {
            return;
        }

        await auditService.RecordAsync(
            assignmentRepository.CourseId,
            assignmentRepository.AssignmentId,
            repositoryId,
            actorUserId,
            "RepositoryArchiveDownloaded",
            cancellationToken);
    }
}

public enum AssignmentArchiveAccess
{
    Success,
    NotFound,
    Forbidden,
}

public sealed record AssignmentArchiveResult(
    AssignmentArchiveAccess Access,
    byte[]? Content,
    string? FileName)
{
    public static AssignmentArchiveResult Success(
        byte[] content,
        string fileName) =>
        new(AssignmentArchiveAccess.Success, content, fileName);

    public static AssignmentArchiveResult NotFound() =>
        new(AssignmentArchiveAccess.NotFound, null, null);

    public static AssignmentArchiveResult Forbidden() =>
        new(AssignmentArchiveAccess.Forbidden, null, null);
}
