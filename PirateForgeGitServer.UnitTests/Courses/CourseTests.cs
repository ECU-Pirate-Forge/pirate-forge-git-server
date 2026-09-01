using PirateForgeGitServer.Domain.Courses;
using PirateForgeGitServer.Domain.Identity;

namespace PirateForgeGitServer.UnitTests.Courses;

public sealed class CourseTests
{
    [Fact]
    public void CreateNormalizesCodeAndAddsCreatorAsInstructor()
    {
        var creatorId = Guid.NewGuid();

        var course = Course.Create(
            " cs-101_fall.2026 ",
            " Introduction to Computer Science ",
            creatorId,
            DateTimeOffset.UtcNow);

        Assert.Equal("CS-101_FALL.2026", course.Code);
        Assert.Equal("Introduction to Computer Science", course.Name);
        var membership = Assert.Single(course.Memberships);
        Assert.Equal(creatorId, membership.UserId);
        Assert.Equal(ApplicationRoles.Instructor, membership.Role);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-CS101")]
    [InlineData("CS 101")]
    [InlineData("CS/101")]
    [InlineData("CS\\101")]
    public void CreateRejectsInvalidCourseCodes(string code)
    {
        Assert.Throws<ArgumentException>(() =>
            Course.Create(
                code,
                "Computer Science",
                Guid.NewGuid(),
                DateTimeOffset.UtcNow));
    }

    [Fact]
    public void AddStaffRejectsUnsupportedRoles()
    {
        var course = Course.Create(
            "CS101",
            "Computer Science",
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentException>(() =>
            course.AddStaff(
                Guid.NewGuid(),
                ApplicationRoles.Student,
                DateTimeOffset.UtcNow));
    }

    [Fact]
    public void RemoveStaffPreservesAtLeastOneInstructor()
    {
        var creatorId = Guid.NewGuid();
        var course = Course.Create(
            "CS101",
            "Computer Science",
            creatorId,
            DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() =>
            course.RemoveStaff(creatorId));
    }

    [Fact]
    public void EnrollStudentIsIdempotent()
    {
        var course = Course.Create(
            "CS101",
            "Computer Science",
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);
        var studentId = Guid.NewGuid();

        var firstEnrollment = course.EnrollStudent(
            studentId,
            DateTimeOffset.UtcNow);
        var secondEnrollment = course.EnrollStudent(
            studentId,
            DateTimeOffset.UtcNow);

        Assert.True(firstEnrollment);
        Assert.False(secondEnrollment);
        Assert.Contains(
            course.Memberships,
            membership => membership.UserId == studentId
                && membership.Role == ApplicationRoles.Student);
    }
}
