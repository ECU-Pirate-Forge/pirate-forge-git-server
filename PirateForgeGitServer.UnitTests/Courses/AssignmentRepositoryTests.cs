using PirateForgeGitServer.Domain.Courses;

namespace PirateForgeGitServer.UnitTests.Courses;

public sealed class AssignmentRepositoryTests
{
    [Fact]
    public void CreateLinksAnAssignmentStudentAndRepository()
    {
        var assignmentId = Guid.NewGuid();
        var studentUserId = Guid.NewGuid();
        var repositoryId = Guid.NewGuid();
        var provisionedAt = DateTimeOffset.UtcNow;

        var assignmentRepository = AssignmentRepository.Create(
            assignmentId,
            studentUserId,
            repositoryId,
            provisionedAt);

        Assert.Equal(assignmentId, assignmentRepository.AssignmentId);
        Assert.Equal(studentUserId, assignmentRepository.StudentUserId);
        Assert.Equal(repositoryId, assignmentRepository.RepositoryId);
        Assert.Equal(provisionedAt, assignmentRepository.ProvisionedAt);
    }

    [Fact]
    public void CreateRepositoryNameIsDeterministicAndIncludesTheStudentIdentifier()
    {
        var studentUserId = Guid.Parse("12345678-1234-1234-1234-1234567890ab");

        var first = AssignmentRepository.CreateRepositoryName(
            "CS-101",
            "project-one",
            studentUserId);
        var second = AssignmentRepository.CreateRepositoryName(
            "CS-101",
            "project-one",
            studentUserId);

        Assert.Equal(
            "cs-101-project-one-123456781234123412341234567890ab",
            first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void CreateRepositoryNameTruncatesLongPrefixesToRepositoryNameLimit()
    {
        var name = AssignmentRepository.CreateRepositoryName(
            new string('C', 30),
            new string('a', 50),
            Guid.NewGuid());

        Assert.Equal(100, name.Length);
    }
}
