namespace PirateForgeGitServer.Contracts.Courses;

public sealed record AssignmentSubmissionResponse(
    Guid Id,
    Guid AssignmentId,
    Guid StudentUserId,
    string CommitSha,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    string Status,
    int Score,
    int MaxScore,
    string Summary,
    string? Error,
    string? ReviewSummary,
    IReadOnlyList<AssignmentReviewQuestionResponse> Questions);

public sealed record AssignmentReviewQuestionResponse(
    Guid Id,
    string Question,
    string FocusArea,
    string? StudentAnswer,
    DateTimeOffset CreatedAt,
    DateTimeOffset? AnsweredAt);
