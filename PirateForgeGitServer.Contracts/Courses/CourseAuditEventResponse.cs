namespace PirateForgeGitServer.Contracts.Courses;

public sealed record CourseAuditEventResponse(
    Guid Id,
    Guid CourseId,
    Guid? AssignmentId,
    Guid? RepositoryId,
    string? RepositoryName,
    Guid ActorUserId,
    string ActorEmail,
    string ActorDisplayName,
    string Action,
    DateTimeOffset CreatedAt);
