namespace PirateForgeGitServer.Domain.Identity;

public static class ApplicationRoles
{
    public const string Administrator = nameof(Administrator);
    public const string Instructor = nameof(Instructor);
    public const string Student = nameof(Student);
    public const string TeachingAssistant = "TA";

    public static IReadOnlyList<string> All =>
    [
        Administrator,
        Instructor,
        Student,
        TeachingAssistant
    ];
}
