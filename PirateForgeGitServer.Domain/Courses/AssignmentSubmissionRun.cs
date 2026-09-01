namespace PirateForgeGitServer.Domain.Courses;

public sealed class AssignmentSubmissionRun
{
    private readonly List<AssignmentReviewQuestion> _questions = [];

    private AssignmentSubmissionRun()
    {
    }

    public Guid Id { get; private set; }

    public Guid AssignmentId { get; private set; }

    public Guid StudentUserId { get; private set; }

    public Guid RepositoryId { get; private set; }

    public string CommitSha { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? CompletedAt { get; private set; }

    public string Status { get; private set; } = "Queued";

    public int Score { get; private set; }

    public int MaxScore { get; private set; }

    public string Summary { get; private set; } = string.Empty;

    public string Error { get; private set; } = string.Empty;

    public string AssessmentLog { get; private set; } = string.Empty;

    public string ReviewSummary { get; private set; } = string.Empty;

    public IReadOnlyCollection<AssignmentReviewQuestion> Questions => _questions;

    public static AssignmentSubmissionRun Create(
        Guid assignmentId,
        Guid studentUserId,
        Guid repositoryId,
        DateTimeOffset createdAt)
    {
        return new AssignmentSubmissionRun
        {
            Id = Guid.NewGuid(),
            AssignmentId = assignmentId,
            StudentUserId = studentUserId,
            RepositoryId = repositoryId,
            CreatedAt = createdAt,
            Status = "Queued",
            Summary = "Queued for grading and review.",
        };
    }

    public void Start(string commitSha)
    {
        CommitSha = commitSha;
        Status = "Running";
        Summary = "Running the automatic grading and AI review checks.";
    }

    public void Complete(
        int score,
        int maxScore,
        string summary,
        string? reviewSummary,
        string? assessmentLog)
    {
        Score = score;
        MaxScore = maxScore;
        Summary = string.IsNullOrWhiteSpace(summary)
            ? "The submission has been graded."
            : summary.Trim();
        ReviewSummary = string.IsNullOrWhiteSpace(reviewSummary)
            ? string.Empty
            : reviewSummary.Trim();
        AssessmentLog = string.IsNullOrWhiteSpace(assessmentLog)
            ? string.Empty
            : assessmentLog.Trim();
        Status = score >= maxScore ? "Passed" : "NeedsReview";
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void Fail(string error, string? assessmentLog = null)
    {
        Error = string.IsNullOrWhiteSpace(error)
            ? "The grading pipeline failed unexpectedly."
            : error.Trim();
        AssessmentLog = string.IsNullOrWhiteSpace(assessmentLog)
            ? string.Empty
            : assessmentLog.Trim();
        Status = "Failed";
        Summary = "The grading pipeline failed before completing the review.";
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void AddQuestion(AssignmentReviewQuestion question)
    {
        ArgumentNullException.ThrowIfNull(question);
        _questions.Add(question);
    }
}
