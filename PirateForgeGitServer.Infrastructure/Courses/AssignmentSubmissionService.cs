using Microsoft.EntityFrameworkCore;
using PirateForgeGitServer.Domain.Courses;
using PirateForgeGitServer.Infrastructure.Persistence;

namespace PirateForgeGitServer.Infrastructure.Courses;

public sealed class AssignmentSubmissionService(
    IDbContextFactory<PirateForgeDbContext> dbContextFactory)
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
}
