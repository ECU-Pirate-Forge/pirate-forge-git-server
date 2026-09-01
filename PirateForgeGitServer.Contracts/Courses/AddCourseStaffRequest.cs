namespace PirateForgeGitServer.Contracts.Courses;

public sealed record AddCourseStaffRequest(
    Guid UserId,
    string Role);
