namespace PirateForgeGitServer.Contracts.Courses;

public sealed record CourseResponse(
    Guid Id,
    string Code,
    string Name,
    DateTimeOffset CreatedAt,
    IReadOnlyList<CourseStaffResponse> Staff);

public sealed record CourseStaffResponse(
    Guid UserId,
    string Email,
    string DisplayName,
    string Role,
    DateTimeOffset AddedAt);
