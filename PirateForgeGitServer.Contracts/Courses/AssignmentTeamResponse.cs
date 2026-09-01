namespace PirateForgeGitServer.Contracts.Courses;

public sealed record AssignmentTeamResponse(
    Guid TeamId,
    Guid AssignmentId,
    string Name,
    string RepositoryName,
    string RepositoryDisplayName,
    IReadOnlyList<AssignmentTeamMemberResponse> Members,
    IReadOnlyList<AssignmentTeamContributionResponse> Contributions);

public sealed record AssignmentTeamMemberResponse(
    Guid UserId,
    string DisplayName,
    string Email,
    DateTimeOffset JoinedAt);

public sealed record AssignmentTeamContributionResponse(
    Guid UserId,
    string DisplayName,
    int BranchCount,
    int CommitCount,
    int DocumentationCommitCount,
    int UnitTestCommitCount,
    int ImplementationCommitCount,
    DateTimeOffset? LastContributionAt,
    DateTimeOffset UpdatedAt);
