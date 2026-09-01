namespace PirateForgeGitServer.Domain.Courses;

public sealed class AssignmentReviewQuestion
{
    private AssignmentReviewQuestion()
    {
    }

    public Guid Id { get; private set; }

    public Guid SubmissionRunId { get; private set; }

    public AssignmentSubmissionRun SubmissionRun { get; private set; } = null!;

    public string Question { get; private set; } = string.Empty;

    public string FocusArea { get; private set; } = string.Empty;

    public string? StudentAnswer { get; set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? AnsweredAt { get; set; }

    public static AssignmentReviewQuestion Create(
        Guid submissionRunId,
        string question,
        string focusArea)
    {
        var normalizedQuestion = question.Trim();
        if (string.IsNullOrWhiteSpace(normalizedQuestion))
        {
            throw new ArgumentException(
                "A review question is required.",
                nameof(question));
        }

        var normalizedFocusArea = string.IsNullOrWhiteSpace(focusArea)
            ? "Core implementation"
            : focusArea.Trim();

        return new AssignmentReviewQuestion
        {
            Id = Guid.NewGuid(),
            SubmissionRunId = submissionRunId,
            Question = normalizedQuestion,
            FocusArea = normalizedFocusArea,
        };
    }
}
