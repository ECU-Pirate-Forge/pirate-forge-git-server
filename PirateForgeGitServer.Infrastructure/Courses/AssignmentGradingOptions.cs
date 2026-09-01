namespace PirateForgeGitServer.Infrastructure.Courses;

public sealed class AssignmentGradingOptions
{
    public const string SectionName = "PirateForge:AssignmentGrading";

    public bool Enabled { get; set; } = true;

    public string LocalEndpoint { get; set; } = "http://localhost:11434";

    public string LocalModel { get; set; } = "llama3.2";

    public int DefaultQuestionCount { get; set; } = 3;
}
