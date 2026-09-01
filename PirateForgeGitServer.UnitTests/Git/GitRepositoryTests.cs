using PirateForgeGitServer.Domain.Git;

namespace PirateForgeGitServer.UnitTests.Git;

public sealed class GitRepositoryTests
{
    [Theory]
    [InlineData("course-template")]
    [InlineData("cs101.assignment-1")]
    [InlineData("student_repo")]
    public void CreateAcceptsSafeRepositoryNames(string name)
    {
        var repository = GitRepository.Create(
            name,
            "Starter repository",
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);

        Assert.Equal(name, repository.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../outside")]
    [InlineData("course/repository")]
    [InlineData("course\\repository")]
    [InlineData("-option")]
    [InlineData("name.git")]
    [InlineData("name lock")]
    public void CreateRejectsUnsafeRepositoryNames(string name)
    {
        Assert.Throws<ArgumentException>(() =>
            GitRepository.Create(
                name,
                "Starter repository",
                Guid.NewGuid(),
                DateTimeOffset.UtcNow));
    }

    [Fact]
    public void CreateNormalizesRepositoryNameToLowercase()
    {
        var repository = GitRepository.Create(
            "CS101-Template",
            "Starter repository",
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);

        Assert.Equal("cs101-template", repository.Name);
    }

    [Fact]
    public void CreateCanMarkARepositoryAsATemplate()
    {
        var repository = GitRepository.Create(
            "cs101-template",
            "Starter repository",
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            RepositoryPurpose.Template);

        Assert.Equal(RepositoryPurpose.Template, repository.Purpose);
    }
}
