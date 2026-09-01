namespace PirateForgeGitServer.Domain.Courses;

public sealed class CourseAuditEvent
{
    private CourseAuditEvent()
    {
    }

    public Guid Id { get; private set; }

    public Guid CourseId { get; private set; }

    public Guid? AssignmentId { get; private set; }

    public Guid? RepositoryId { get; private set; }

    public Guid ActorUserId { get; private set; }

    public string Action { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public static CourseAuditEvent Create(
        Guid courseId,
        Guid? assignmentId,
        Guid? repositoryId,
        Guid actorUserId,
        string action,
        DateTimeOffset createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            CourseId = courseId,
            AssignmentId = assignmentId,
            RepositoryId = repositoryId,
            ActorUserId = actorUserId,
            Action = action,
            CreatedAt = createdAt,
        };
}
