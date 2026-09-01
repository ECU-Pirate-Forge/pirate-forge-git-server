namespace PirateForgeGitServer.Contracts.Courses;

public sealed record CreateAssignmentTeamRequest(
    IReadOnlyList<Guid> MemberUserIds,
    string? TeamName = null);
