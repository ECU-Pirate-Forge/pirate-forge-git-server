namespace PirateForgeGitServer.Domain.Courses;

public sealed class CourseMembership
{
    private CourseMembership()
    {
    }

    public Guid CourseId { get; private set; }

    public Guid UserId { get; private set; }

    public string Role { get; private set; } = string.Empty;

    public DateTimeOffset AddedAt { get; private set; }

    internal static CourseMembership Create(
        Guid courseId,
        Guid userId,
        string role,
        DateTimeOffset addedAt) =>
        new()
        {
            CourseId = courseId,
            UserId = userId,
            Role = role,
            AddedAt = addedAt,
        };
}
