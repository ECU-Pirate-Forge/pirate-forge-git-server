namespace PirateForgeGitServer.Domain.Identity;

public static class AuthorizationPolicies
{
    public const string AdministerSystem = nameof(AdministerSystem);
    public const string ManageCourses = nameof(ManageCourses);
    public const string ReadCourseRepositories = nameof(ReadCourseRepositories);
}
