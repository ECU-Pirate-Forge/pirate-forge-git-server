namespace PirateForgeGitServer.Contracts.Courses;

public sealed record CreateCourseRequest(
    string Code,
    string Name);
