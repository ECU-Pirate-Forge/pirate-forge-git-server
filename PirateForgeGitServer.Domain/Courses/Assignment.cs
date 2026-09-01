using System.Text.RegularExpressions;

namespace PirateForgeGitServer.Domain.Courses;

public sealed partial class Assignment
{
    private Assignment()
    {
    }

    public Guid Id { get; private set; }

    public Guid CourseId { get; private set; }

    public string Slug { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public Guid? TemplateRepositoryId { get; private set; }

    public DateTimeOffset? DueAt { get; private set; }

    public bool AutoGradeEnabled { get; private set; } = true;

    public int MaxScore { get; private set; } = 100;

    public string? GradingCommand { get; private set; }

    public bool AskUnderstandingQuestions { get; private set; } = true;

    public string? ReviewPrompt { get; private set; }

    public string? LocalAiModel { get; private set; }

    public bool IsGroupAssignment { get; private set; }

    public int? MinTeamSize { get; private set; }

    public int? MaxTeamSize { get; private set; }

    public bool RequireTeamSelection { get; private set; } = true;

    public bool AllowTeamChanges { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Assignment Create(
        Guid courseId,
        string slug,
        string title,
        string description,
        Guid? templateRepositoryId,
        DateTimeOffset? dueAt,
        Guid createdByUserId,
        DateTimeOffset createdAt,
        string? gradingCommand = null,
        int maxScore = 100,
        bool autoGradeEnabled = true,
        bool askUnderstandingQuestions = true,
        string? reviewPrompt = null,
        string? localAiModel = null,
        bool isGroupAssignment = false,
        int? minTeamSize = null,
        int? maxTeamSize = null,
        bool requireTeamSelection = true,
        bool allowTeamChanges = false)
    {
        var normalizedSlug = slug.Trim().ToLowerInvariant();
        if (!AssignmentSlugPattern().IsMatch(normalizedSlug))
        {
            throw new ArgumentException(
                "Assignment slugs may contain lowercase letters, numbers, and hyphens, and must start with a letter or number.",
                nameof(slug));
        }

        var normalizedTitle = title.Trim();
        if (string.IsNullOrWhiteSpace(normalizedTitle)
            || normalizedTitle.Length > 200)
        {
            throw new ArgumentException(
                "An assignment title between 1 and 200 characters is required.",
                nameof(title));
        }

        var normalizedDescription = description.Trim();
        if (normalizedDescription.Length > 4000)
        {
            throw new ArgumentException(
                "An assignment description cannot exceed 4,000 characters.",
                nameof(description));
        }

        if (dueAt <= createdAt)
        {
            throw new ArgumentException(
                "The assignment deadline must be after its creation time.",
                nameof(dueAt));
        }

        if (maxScore <= 0)
        {
            throw new ArgumentException(
                "Assignment maximum score must be greater than zero.",
                nameof(maxScore));
        }

        if (isGroupAssignment)
        {
            var effectiveMinTeamSize = minTeamSize ?? 2;
            var effectiveMaxTeamSize = maxTeamSize ?? 4;

            if (effectiveMinTeamSize < 2)
            {
                throw new ArgumentException(
                    "Group assignments require at least two students per team.",
                    nameof(minTeamSize));
            }

            if (effectiveMaxTeamSize < effectiveMinTeamSize)
            {
                throw new ArgumentException(
                    "The maximum team size must be greater than or equal to the minimum team size.",
                    nameof(maxTeamSize));
            }

            minTeamSize = effectiveMinTeamSize;
            maxTeamSize = effectiveMaxTeamSize;
        }
        else
        {
            minTeamSize = null;
            maxTeamSize = null;
        }

        return new Assignment
        {
            Id = Guid.NewGuid(),
            CourseId = courseId,
            Slug = normalizedSlug,
            Title = normalizedTitle,
            Description = normalizedDescription,
            TemplateRepositoryId = templateRepositoryId,
            DueAt = dueAt,
            AutoGradeEnabled = autoGradeEnabled,
            MaxScore = maxScore,
            GradingCommand = string.IsNullOrWhiteSpace(gradingCommand)
                ? null
                : gradingCommand.Trim(),
            AskUnderstandingQuestions = askUnderstandingQuestions,
            ReviewPrompt = string.IsNullOrWhiteSpace(reviewPrompt)
                ? null
                : reviewPrompt.Trim(),
            LocalAiModel = string.IsNullOrWhiteSpace(localAiModel)
                ? null
                : localAiModel.Trim(),
            IsGroupAssignment = isGroupAssignment,
            MinTeamSize = minTeamSize,
            MaxTeamSize = maxTeamSize,
            RequireTeamSelection = requireTeamSelection,
            AllowTeamChanges = allowTeamChanges,
            CreatedByUserId = createdByUserId,
            CreatedAt = createdAt,
        };
    }

    [GeneratedRegex(
        @"^[a-z0-9][a-z0-9-]{0,49}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex AssignmentSlugPattern();
}
