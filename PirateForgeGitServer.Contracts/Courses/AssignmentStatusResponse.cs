namespace PirateForgeGitServer.Contracts.Courses;

public sealed record AssignmentStatusResponse(
    Guid AssignmentId,
    Guid CourseId,
    string Slug,
    string Title,
    int TotalStudents,
    int ProvisionedRepositories,
    IReadOnlyList<StudentAssignmentStatusResponse> Students);

public sealed record StudentAssignmentStatusResponse(
    Guid StudentUserId,
    string StudentEmail,
    string StudentDisplayName,
    string State,
    string? RepositoryName,
    string? RepositoryDisplayName,
    string? Branch,
    AssignmentLatestCommitResponse? LatestCommit);

public sealed record AssignmentLatestCommitResponse(
    string Id,
    string Subject,
    string AuthorName,
    string AuthorEmail,
    DateTimeOffset AuthoredAt);
