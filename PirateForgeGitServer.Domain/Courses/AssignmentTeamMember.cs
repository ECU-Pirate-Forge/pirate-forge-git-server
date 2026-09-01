namespace PirateForgeGitServer.Domain.Courses;

public sealed class AssignmentTeamMember
{
    private AssignmentTeamMember()
    {
    }

    public Guid TeamId { get; private set; }

    public Guid UserId { get; private set; }

    public DateTimeOffset JoinedAt { get; private set; }

    public static AssignmentTeamMember Create(
        Guid teamId,
        Guid userId,
        DateTimeOffset joinedAt)
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

        return new AssignmentTeamMember
        {
            TeamId = teamId,
            UserId = userId,
            JoinedAt = joinedAt,
        };
    }
}
