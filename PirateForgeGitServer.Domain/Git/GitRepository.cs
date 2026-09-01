using System.Text.RegularExpressions;

namespace PirateForgeGitServer.Domain.Git;

public sealed partial class GitRepository
{
    private GitRepository()
    {
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public Guid OwnerUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public RepositoryPurpose Purpose { get; private set; }

    public static GitRepository Create(
        string name,
        string displayName,
        Guid ownerUserId,
        DateTimeOffset createdAt,
        RepositoryPurpose purpose = RepositoryPurpose.Standard)
    {
        var normalizedName = NormalizeName(name);

        var normalizedDisplayName = displayName.Trim();
        if (string.IsNullOrWhiteSpace(normalizedDisplayName))
        {
            throw new ArgumentException(
                "A repository display name is required.",
                nameof(displayName));
        }

        return new GitRepository
        {
            Id = Guid.NewGuid(),
            Name = normalizedName,
            DisplayName = normalizedDisplayName,
            OwnerUserId = ownerUserId,
            CreatedAt = createdAt,
            Purpose = purpose,
        };
    }

    public static string NormalizeName(string name)
    {
        var normalizedName = name.Trim().ToLowerInvariant();
        if (!RepositoryNamePattern().IsMatch(normalizedName)
            || normalizedName.EndsWith(".git", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Repository names may contain letters, numbers, periods, underscores, and hyphens, and must start with a letter or number.",
                nameof(name));
        }

        return normalizedName;
    }

    [GeneratedRegex(
        @"^[a-z0-9][a-z0-9._-]{0,99}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex RepositoryNamePattern();
}
