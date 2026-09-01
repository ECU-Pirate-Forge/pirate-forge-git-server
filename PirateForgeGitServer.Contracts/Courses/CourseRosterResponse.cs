namespace PirateForgeGitServer.Contracts.Courses;

public sealed record CourseStudentResponse(
    Guid UserId,
    string Email,
    string DisplayName,
    DateTimeOffset EnrolledAt);

public sealed record CourseRosterImportResponse(
    IReadOnlyList<CourseRosterImportRowResponse> Rows);

public sealed record CourseRosterImportRowResponse(
    int RowNumber,
    string Email,
    string DisplayName,
    string Status,
    Guid? UserId,
    string? ActivationToken,
    string? Message);
