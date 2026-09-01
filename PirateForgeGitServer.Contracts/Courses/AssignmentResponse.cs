namespace PirateForgeGitServer.Contracts.Courses;

public sealed record AssignmentResponse(
    Guid Id,
    Guid CourseId,
    string Slug,
    string Title,
    string Description,
    AssignmentTemplateResponse? Template,
    DateTimeOffset? DueAt,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt,
    int StudentCount,
    int ProvisionedRepositoryCount,
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

public sealed record AssignmentTemplateResponse(
    Guid Id,
    string Name,
    string DisplayName);
