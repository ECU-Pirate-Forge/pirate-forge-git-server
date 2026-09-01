namespace PirateForgeGitServer.Domain.Courses;

public sealed class AssignmentTeamContribution
{
    private AssignmentTeamContribution()
    {
    }

    public Guid Id { get; private set; }

    public Guid TeamId { get; private set; }

    public Guid UserId { get; private set; }

    public int BranchCount { get; private set; }

    public int CommitCount { get; private set; }

    public int DocumentationCommitCount { get; private set; }

    public int UnitTestCommitCount { get; private set; }

    public int ImplementationCommitCount { get; private set; }

    public DateTimeOffset? LastContributionAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static AssignmentTeamContribution Create(
        Guid teamId,
        Guid userId,
        DateTimeOffset createdAt)
    {
        if (teamId == Guid.Empty)
        {
            throw new ArgumentException(
                "A team identifier is required.",
                nameof(teamId));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException(
                "A user identifier is required.",
                nameof(userId));
        }

        return new AssignmentTeamContribution
        {
            Id = Guid.NewGuid(),
            TeamId = teamId,
            UserId = userId,
            BranchCount = 0,
            CommitCount = 0,
            DocumentationCommitCount = 0,
            UnitTestCommitCount = 0,
            ImplementationCommitCount = 0,
            LastContributionAt = null,
            UpdatedAt = createdAt,
        };
    }

    public void Update(
        int branchCount,
        int commitCount,
        int documentationCommitCount,
        int unitTestCommitCount,
        int implementationCommitCount,
        DateTimeOffset? lastContributionAt,
        DateTimeOffset updatedAt)
    {
        if (branchCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(branchCount),
                "Branch counts cannot be negative.");
        }

        if (commitCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(commitCount),
                "Commit counts cannot be negative.");
        }

        if (documentationCommitCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(documentationCommitCount),
                "Documentation commit counts cannot be negative.");
        }

        if (unitTestCommitCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(unitTestCommitCount),
                "Unit test commit counts cannot be negative.");
        }

        if (implementationCommitCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(implementationCommitCount),
                "Implementation commit counts cannot be negative.");
        }

        BranchCount = branchCount;
        CommitCount = commitCount;
        DocumentationCommitCount = documentationCommitCount;
        UnitTestCommitCount = unitTestCommitCount;
        ImplementationCommitCount = implementationCommitCount;
        LastContributionAt = lastContributionAt;
        UpdatedAt = updatedAt;
    }
}
