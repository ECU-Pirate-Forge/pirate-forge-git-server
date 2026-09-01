using PirateForgeGitServer.Domain.Courses;

namespace PirateForgeGitServer.UnitTests.Courses;

public sealed class AssignmentSubmissionRunTests
{
    [Fact]
    public void ArchiveStoresArchiveMetadata()
    {
        var run = CreateRun();
        run.Start("abc123");
        run.Archive("C:\\temp\\student-submission.zip");

        Assert.True(run.IsArchived);
        Assert.False(run.IsPurged);
        Assert.Equal("C:\\temp\\student-submission.zip", run.ArchiveFilePath);
        Assert.NotNull(run.ArchivedAt);
    }

    [Fact]
    public void PurgeMarksTheSubmissionAsPurged()
    {
        var run = CreateRun();
        run.Archive("C:\\temp\\student-submission.zip");

        run.Purge();

        Assert.True(run.IsPurged);
        Assert.NotNull(run.PurgedAt);
        Assert.True(run.IsArchived);
        Assert.Throws<InvalidOperationException>(() =>
            run.Archive("C:\\temp\\another-submission.zip"));
    }

    private static AssignmentSubmissionRun CreateRun()
    {
        return AssignmentSubmissionRun.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);
    }
}
