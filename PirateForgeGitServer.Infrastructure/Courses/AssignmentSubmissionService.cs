using Microsoft.EntityFrameworkCore;
using PirateForgeGitServer.Domain.Courses;
using PirateForgeGitServer.Domain.Identity;
using PirateForgeGitServer.Infrastructure.Git;
using PirateForgeGitServer.Infrastructure.Persistence;
using PirateForgeGitServer.Infrastructure.Storage;

namespace PirateForgeGitServer.Infrastructure.Courses;

public sealed class AssignmentSubmissionService(
    IDbContextFactory<PirateForgeDbContext> dbContextFactory,
    StoragePaths storagePaths,
    CourseAuditService auditService)
{
    public async Task<AssignmentSubmissionRun?> GetLatestForStudentAsync(
        Guid courseId,
        Guid assignmentId,
        Guid studentUserId,
        CancellationToken cancellationToken)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(
            cancellationToken);

        var assignment = await context.Assignments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.Id == assignmentId && value.CourseId == courseId,
                cancellationToken);
        if (assignment is null)
        {
            return null;
        }

        return await context.AssignmentSubmissionRuns
            .Include(value => value.Questions)
            .Where(value =>
                value.AssignmentId == assignmentId
                && value.StudentUserId == studentUserId)
            .OrderByDescending(value => value.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AssignmentSubmissionRun>> ListArchivedAsync(
        Guid courseId,
        Guid assignmentId,
        Guid actingUserId,
        bool isAdministrator,
        CancellationToken cancellationToken)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(
            cancellationToken);

        if (!await CanManageAsync(
                context,
                courseId,
                actingUserId,
                isAdministrator,
                cancellationToken))
        {
            return [];
        }

        return await context.AssignmentSubmissionRuns
            .Include(value => value.Questions)
            .Where(value =>
                value.AssignmentId == assignmentId
                && value.ArchivedAt != null
                && value.PurgedAt == null)
            .OrderByDescending(value => value.ArchivedAt)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<bool> ArchiveAsync(
        Guid courseId,
        Guid assignmentId,
        Guid submissionRunId,
        Guid actingUserId,
        bool isAdministrator,
        CancellationToken cancellationToken)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(
            cancellationToken);

        if (!await CanManageAsync(
                context,
                courseId,
                actingUserId,
                isAdministrator,
                cancellationToken))
        {
            return false;
        }

        var run = await context.AssignmentSubmissionRuns
            .SingleOrDefaultAsync(
                value =>
                    value.Id == submissionRunId
                    && value.AssignmentId == assignmentId,
                cancellationToken);
        if (run is null || run.IsPurged || string.IsNullOrWhiteSpace(run.CommitSha))
        {
            return false;
        }

        var repository = await context.GitRepositories
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.Id == run.RepositoryId,
                cancellationToken);
        if (repository is null)
        {
            return false;
        }

        var archivePath = BuildArchivePath(
            assignmentId,
            run.StudentUserId,
            submissionRunId);
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        var archiveBytes = await GitRepositoryReader.CreateArchiveAsync(
            Path.Combine(storagePaths.RepositoriesPath, $"{repository.Name}.git"),
            run.CommitSha,
            cancellationToken);
        await File.WriteAllBytesAsync(archivePath, archiveBytes, cancellationToken);
        run.Archive(archivePath);
        await context.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(
            courseId,
            assignmentId,
            run.RepositoryId,
            actingUserId,
            "SubmissionArchived",
            cancellationToken);
        return true;
    }

    public async Task<bool> PurgeAsync(
        Guid courseId,
        Guid assignmentId,
        Guid submissionRunId,
        Guid actingUserId,
        bool isAdministrator,
        CancellationToken cancellationToken)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(
            cancellationToken);

        if (!await CanManageAsync(
                context,
                courseId,
                actingUserId,
                isAdministrator,
                cancellationToken))
        {
            return false;
        }

        var run = await context.AssignmentSubmissionRuns
            .Include(value => value.Questions)
            .SingleOrDefaultAsync(
                value =>
                    value.Id == submissionRunId
                    && value.AssignmentId == assignmentId,
                cancellationToken);
        if (run is null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(run.ArchiveFilePath)
            && File.Exists(run.ArchiveFilePath))
        {
            File.Delete(run.ArchiveFilePath);
        }

        run.Purge();
        context.AssignmentSubmissionRuns.Remove(run);
        await context.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(
            courseId,
            assignmentId,
            run.RepositoryId,
            actingUserId,
            "SubmissionPurged",
            cancellationToken);
        return true;
    }

    public async Task<AssignmentReviewQuestion?> SubmitAnswerAsync(
        Guid assignmentId,
        Guid questionId,
        Guid studentUserId,
        string answer,
        CancellationToken cancellationToken)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(
            cancellationToken);

        var question = await context.AssignmentReviewQuestions
            .Include(value => value.SubmissionRun)
            .SingleOrDefaultAsync(
                value => value.Id == questionId,
                cancellationToken);
        if (question is null)
        {
            return null;
        }

        if (question.SubmissionRun.AssignmentId != assignmentId
            || question.SubmissionRun.StudentUserId != studentUserId)
        {
            return null;
        }

        question.StudentAnswer = string.IsNullOrWhiteSpace(answer)
            ? null
            : answer.Trim();
        question.AnsweredAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        return question;
    }

    private static async Task<bool> CanManageAsync(
        PirateForgeDbContext context,
        Guid courseId,
        Guid actingUserId,
        bool isAdministrator,
        CancellationToken cancellationToken)
    {
        if (isAdministrator)
        {
            return true;
        }

        var membership = await context.CourseMemberships
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.CourseId == courseId
                    && value.UserId == actingUserId,
                cancellationToken);

        return membership is not null
            && membership.Role == ApplicationRoles.Instructor;
    }

    private string BuildArchivePath(
        Guid assignmentId,
        Guid studentUserId,
        Guid submissionRunId) =>
        Path.Combine(
            storagePaths.SubmissionArchivesPath,
            assignmentId.ToString("N"),
            studentUserId.ToString("N"),
            $"{submissionRunId:N}.zip");
}
