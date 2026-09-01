using PirateForgeGitServer.Domain.Git;

namespace PirateForgeGitServer.Domain.Courses;

public sealed class AssignmentTeam
{
    private AssignmentTeam()
    {
    }

    public Guid Id { get; private set; }

    public Guid AssignmentId { get; private set; }

    public Guid RepositoryId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public static AssignmentTeam Create(
        Guid assignmentId,
        Guid repositoryId,
        string name,
        DateTimeOffset createdAt)
    {
        var normalizedName = name.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName)
            || normalizedName.Length > 200)
        {
            throw new ArgumentException(
                "A team name between 1 and 200 characters is required.",
                nameof(name));
        }

        return new AssignmentTeam
        {
            Id = Guid.NewGuid(),
            AssignmentId = assignmentId,
            RepositoryId = repositoryId,
            Name = normalizedName,
            CreatedAt = createdAt,
        };
    }

    public static string CreateRepositoryName(
        string courseCode,
        string assignmentSlug,
        Guid teamId)
    {
        var prefix = $"{courseCode.Trim().ToLowerInvariant()}-{assignmentSlug.Trim().ToLowerInvariant()}-team";
        const int maximumPrefixLength = 100 - 32 - 1;
        if (prefix.Length > maximumPrefixLength)
        {
            prefix = prefix[..maximumPrefixLength];
        }

        return GitRepository.NormalizeName(
            $"{prefix}-{teamId:N}");
    }
}
