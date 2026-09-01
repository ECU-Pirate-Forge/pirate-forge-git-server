using PirateForgeGitServer.Domain.Git;

namespace PirateForgeGitServer.Domain.Courses;

public sealed class AssignmentRepository
{
    private const int StudentIdentifierLength = 32;
    private const int RepositoryNameLimit = 100;

    private AssignmentRepository()
    {
    }

    public Guid Id { get; private set; }

    public Guid AssignmentId { get; private set; }

    public Guid StudentUserId { get; private set; }

    public Guid RepositoryId { get; private set; }

    public DateTimeOffset ProvisionedAt { get; private set; }

    public static AssignmentRepository Create(
        Guid assignmentId,
        Guid studentUserId,
        Guid repositoryId,
        DateTimeOffset provisionedAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            AssignmentId = assignmentId,
            StudentUserId = studentUserId,
            RepositoryId = repositoryId,
            ProvisionedAt = provisionedAt,
        };

    public static string CreateRepositoryName(
        string courseCode,
        string assignmentSlug,
        Guid studentUserId)
    {
        var prefix = $"{courseCode.Trim().ToLowerInvariant()}-{assignmentSlug.Trim().ToLowerInvariant()}";
        var maximumPrefixLength =
            RepositoryNameLimit - StudentIdentifierLength - 1;
        if (prefix.Length > maximumPrefixLength)
        {
            prefix = prefix[..maximumPrefixLength];
        }

        return GitRepository.NormalizeName(
            $"{prefix}-{studentUserId:N}");
    }
}
