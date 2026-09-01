using PirateForgeGitServer.Domain.Courses;

namespace PirateForgeGitServer.UnitTests.Courses;

public sealed class AssignmentTests
{
    [Fact]
    public void CreateNormalizesAnAssignmentWithAStarterTemplate()
    {
        var courseId = Guid.NewGuid();
        var creatorId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;
        var dueAt = createdAt.AddDays(7);

        var assignment = Assignment.Create(
            courseId,
            " Project-1 ",
            " First Project ",
            " Build the first project. ",
            templateId,
            dueAt,
            creatorId,
            createdAt);

        Assert.Equal(courseId, assignment.CourseId);
        Assert.Equal("project-1", assignment.Slug);
        Assert.Equal("First Project", assignment.Title);
        Assert.Equal("Build the first project.", assignment.Description);
        Assert.Equal(templateId, assignment.TemplateRepositoryId);
        Assert.Equal(dueAt, assignment.DueAt);
        Assert.Equal(creatorId, assignment.CreatedByUserId);
    }

    [Fact]
    public void CreateAllowsBlankStarterCode()
    {
        var assignment = Assignment.Create(
            Guid.NewGuid(),
            "reading-1",
            "Reading One",
            string.Empty,
            templateRepositoryId: null,
            dueAt: null,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);

        Assert.Null(assignment.TemplateRepositoryId);
        Assert.Null(assignment.DueAt);
    }

    [Fact]
    public void CreateSetsDefaultTeamParametersForGroupAssignments()
    {
        var assignment = Assignment.Create(
            Guid.NewGuid(),
            "project-team",
            "Group Project",
            "Build together.",
            templateRepositoryId: null,
            dueAt: DateTimeOffset.UtcNow.AddDays(7),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            isGroupAssignment: true);

        Assert.True(assignment.IsGroupAssignment);
        Assert.Equal(2, assignment.MinTeamSize);
        Assert.Equal(4, assignment.MaxTeamSize);
        Assert.True(assignment.RequireTeamSelection);
    }

    [Fact]
    public void CreateRejectsInvalidTeamSizesForGroupAssignments()
    {
        Assert.Throws<ArgumentException>(() =>
            Assignment.Create(
                Guid.NewGuid(),
                "project-team",
                "Group Project",
                "Build together.",
                null,
                DateTimeOffset.UtcNow.AddDays(7),
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                isGroupAssignment: true,
                minTeamSize: 5,
                maxTeamSize: 3));
    }

    [Theory]
    [InlineData("")]
    [InlineData("-project")]
    [InlineData("project one")]
    [InlineData("project/one")]
    public void CreateRejectsInvalidSlugs(string slug)
    {
        Assert.Throws<ArgumentException>(() =>
            Assignment.Create(
                Guid.NewGuid(),
                slug,
                "Project",
                string.Empty,
                null,
                null,
                Guid.NewGuid(),
                DateTimeOffset.UtcNow));
    }

    [Fact]
    public void CreateRejectsADeadlineBeforeCreation()
    {
        var createdAt = DateTimeOffset.UtcNow;

        Assert.Throws<ArgumentException>(() =>
            Assignment.Create(
                Guid.NewGuid(),
                "project",
                "Project",
                string.Empty,
                null,
                createdAt.AddMinutes(-1),
                Guid.NewGuid(),
                createdAt));
    }
}
