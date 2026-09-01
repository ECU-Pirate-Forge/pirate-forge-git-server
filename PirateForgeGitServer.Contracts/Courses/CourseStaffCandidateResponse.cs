namespace PirateForgeGitServer.Contracts.Courses;

public sealed record CourseStaffCandidateResponse(
    Guid UserId,
    string Email,
    string DisplayName,
    string Role);
