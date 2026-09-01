namespace PirateForgeGitServer.Contracts.Courses;

public sealed record CreateAssignmentRequest(
    string Slug,
    string Title,
    string? Description,
    Guid? TemplateRepositoryId,
    DateTimeOffset? DueAt,
    bool AutoGradeEnabled = true,
    int MaxScore = 100,
    string? GradingCommand = null,
    bool AskUnderstandingQuestions = true,
    string? ReviewPrompt = null,
    string? LocalAiModel = null,
    bool IsGroupAssignment = false,
    int? MinTeamSize = null,
    int? MaxTeamSize = null,
    bool RequireTeamSelection = true,
    bool AllowTeamChanges = false);
