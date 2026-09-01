using Microsoft.EntityFrameworkCore;
using PirateForgeGitServer.Domain.Courses;
using PirateForgeGitServer.Domain.Git;
using PirateForgeGitServer.Domain.Identity;
using PirateForgeGitServer.Infrastructure.Persistence;

namespace PirateForgeGitServer.Infrastructure.Courses;

public sealed class AssignmentTeamService(
    IDbContextFactory<PirateForgeDbContext> dbContextFactory,
    TimeProvider timeProvider)
{
    public async Task<AssignmentTeamCreateResult> CreateAsync(
        Guid courseId,
        Guid assignmentId,
        Guid actingUserId,
        bool isAdministrator,
        IReadOnlyCollection<Guid> memberUserIds,
        string? teamName = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(
            cancellationToken);

        if (!isAdministrator)
        {
            var membership = await context.CourseMemberships
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    value => value.CourseId == courseId
                        && value.UserId == actingUserId,
                    cancellationToken);
            if (membership is null)
            {
                return AssignmentTeamCreateResult.NotFound();
            }
        }

        var assignment = await context.Assignments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.Id == assignmentId
                    && value.CourseId == courseId,
                cancellationToken);
        if (assignment is null)
        {
            return AssignmentTeamCreateResult.NotFound();
        }

        if (!assignment.IsGroupAssignment)
        {
            return AssignmentTeamCreateResult.Invalid(
                "This assignment is not configured as a group assignment.");
        }

        var selectedMembers = memberUserIds
            .Where(value => value != Guid.Empty)
            .Distinct()
            .OrderBy(value => value)
            .ToArray();
        if (selectedMembers.Length == 0)
        {
            return AssignmentTeamCreateResult.Invalid(
                "At least one team member must be selected.");
        }

        var minimumTeamSize = assignment.MinTeamSize ?? 2;
        var maximumTeamSize = assignment.MaxTeamSize ?? 4;
        if (selectedMembers.Length < minimumTeamSize
            || selectedMembers.Length > maximumTeamSize)
        {
            return AssignmentTeamCreateResult.Invalid(
                $"Group assignments require between {minimumTeamSize} and {maximumTeamSize} students per team.");
        }

        if (assignment.RequireTeamSelection
            && !selectedMembers.Contains(actingUserId))
        {
            return AssignmentTeamCreateResult.Invalid(
                "The acting user must be included in the selected team.");
        }

        var memberships = await context.CourseMemberships
            .AsNoTracking()
            .Where(value =>
                value.CourseId == courseId
                && selectedMembers.Contains(value.UserId))
            .ToArrayAsync(cancellationToken);
        if (memberships.Length != selectedMembers.Length)
        {
            return AssignmentTeamCreateResult.Invalid(
                "Every selected team member must be enrolled in the course.");
        }

        if (memberships.Any(value => value.Role != ApplicationRoles.Student))
        {
            return AssignmentTeamCreateResult.Invalid(
                "Only students can be assigned to a group team.");
        }

        var existingMembers = await context.AssignmentTeamMembers
            .AsNoTracking()
            .Join(
                context.AssignmentTeams.AsNoTracking(),
                member => member.TeamId,
                team => team.Id,
                (member, team) => new { team.AssignmentId, member.UserId })
            .Where(value =>
                value.AssignmentId == assignmentId
                && selectedMembers.Contains(value.UserId))
            .Select(value => value.UserId)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        if (existingMembers.Length != 0)
        {
            return AssignmentTeamCreateResult.Conflict();
        }

        var course = await context.Courses
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == courseId, cancellationToken);
        if (course is null)
        {
            return AssignmentTeamCreateResult.NotFound();
        }

        var repositoryName = AssignmentTeam.CreateRepositoryName(
            course.Code,
            assignment.Slug,
            Guid.NewGuid());
        var normalizedTeamName = string.IsNullOrWhiteSpace(teamName)
            ? $"Team {selectedMembers.Length}"
            : teamName.Trim();
        var repositoryDisplayName = string.IsNullOrWhiteSpace(teamName)
            ? $"{assignment.Title} - Team {selectedMembers.Length}"
            : teamName.Trim();
        var repository = GitRepository.Create(
            repositoryName,
            repositoryDisplayName,
            actingUserId,
            timeProvider.GetUtcNow(),
            RepositoryPurpose.TeamAssignment);
        context.GitRepositories.Add(repository);

        var team = AssignmentTeam.Create(
            assignmentId,
            repository.Id,
            normalizedTeamName,
            timeProvider.GetUtcNow());
        context.AssignmentTeams.Add(team);

        var memberViews = new List<AssignmentTeamMemberView>(selectedMembers.Length);
        foreach (var memberUserId in selectedMembers)
        {
            var joinedAt = timeProvider.GetUtcNow();
            var member = AssignmentTeamMember.Create(team.Id, memberUserId, joinedAt);
            context.AssignmentTeamMembers.Add(member);
            context.AssignmentTeamContributions.Add(
                AssignmentTeamContribution.Create(team.Id, memberUserId, joinedAt));

            memberViews.Add(new AssignmentTeamMemberView(
                memberUserId,
                string.Empty,
                string.Empty,
                joinedAt));
        }

        await context.SaveChangesAsync(cancellationToken);

        var userRows = await context.Users
            .AsNoTracking()
            .Where(value => selectedMembers.Contains(value.Id))
            .Select(value => new
            {
                value.Id,
                value.DisplayName,
                value.Email,
            })
            .ToArrayAsync(cancellationToken);
        var userLookup = userRows.ToDictionary(value => value.Id, value => value);
        var createdMembers = memberViews
            .Select(member =>
            {
                var user = userLookup.TryGetValue(member.UserId, out var details)
                    ? details
                    : null;
                return new AssignmentTeamMemberView(
                    member.UserId,
                    user?.DisplayName ?? "Unknown user",
                    user?.Email ?? string.Empty,
                    member.JoinedAt);
            })
            .ToArray();

        var createdTeam = new AssignmentTeamView(
            team.Id,
            assignmentId,
            team.Name,
            repository.Name,
            repository.DisplayName,
            createdMembers,
            []);

        return AssignmentTeamCreateResult.Success(createdTeam);
    }

    public async Task<AssignmentTeamListResult> GetForAssignmentAsync(
        Guid courseId,
        Guid assignmentId,
        Guid actingUserId,
        bool isAdministrator,
        CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(
            cancellationToken);

        var assignment = await context.Assignments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.Id == assignmentId
                    && value.CourseId == courseId,
                cancellationToken);
        if (assignment is null)
        {
            return AssignmentTeamListResult.NotFound();
        }

        if (!assignment.IsGroupAssignment)
        {
            return AssignmentTeamListResult.Invalid(
                "This assignment is not configured as a group assignment.");
        }

        if (!isAdministrator)
        {
            var membership = await context.CourseMemberships
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    value => value.CourseId == courseId
                        && value.UserId == actingUserId,
                    cancellationToken);
            if (membership is null)
            {
                return AssignmentTeamListResult.NotFound();
            }
        }

        var teamRows = await context.AssignmentTeams
            .AsNoTracking()
            .Where(value => value.AssignmentId == assignmentId)
            .OrderBy(value => value.Name)
            .ToArrayAsync(cancellationToken);
        if (teamRows.Length == 0)
        {
            return AssignmentTeamListResult.Success([]);
        }

        var teamIds = teamRows.Select(value => value.Id).ToArray();
        var repositoryLookup = await (
                from team in context.AssignmentTeams.AsNoTracking()
                join repository in context.GitRepositories.AsNoTracking()
                    on team.RepositoryId equals repository.Id
                where teamIds.Contains(team.Id)
                select new
                {
                    TeamId = team.Id,
                    RepositoryName = repository.Name,
                    RepositoryDisplayName = repository.DisplayName,
                })
            .ToDictionaryAsync(value => value.TeamId, cancellationToken);
        var memberGroups = await (
                from member in context.AssignmentTeamMembers.AsNoTracking()
                join user in context.Users.AsNoTracking()
                    on member.UserId equals user.Id
                where teamIds.Contains(member.TeamId)
                select new
                {
                    member.TeamId,
                    member.UserId,
                    user.DisplayName,
                    user.Email,
                    member.JoinedAt,
                })
            .GroupBy(value => value.TeamId)
            .ToDictionaryAsync(
                value => value.Key,
                value => value
                    .OrderBy(member => member.DisplayName)
                    .ThenBy(member => member.Email)
                    .Select(member => new AssignmentTeamMemberView(
                        member.UserId,
                        member.DisplayName,
                        member.Email ?? string.Empty,
                        member.JoinedAt))
                    .ToArray(),
                cancellationToken);
        var contributionGroups = await (
                from contribution in context.AssignmentTeamContributions.AsNoTracking()
                join user in context.Users.AsNoTracking()
                    on contribution.UserId equals user.Id
                where teamIds.Contains(contribution.TeamId)
                select new
                {
                    contribution.TeamId,
                    contribution.UserId,
                    user.DisplayName,
                    contribution.BranchCount,
                    contribution.CommitCount,
                    contribution.DocumentationCommitCount,
                    contribution.UnitTestCommitCount,
                    contribution.ImplementationCommitCount,
                    contribution.LastContributionAt,
                    contribution.UpdatedAt,
                })
            .GroupBy(value => value.TeamId)
            .ToDictionaryAsync(
                value => value.Key,
                value => value
                    .OrderBy(member => member.DisplayName)
                    .Select(member => new AssignmentTeamContributionView(
                        member.UserId,
                        member.DisplayName,
                        member.BranchCount,
                        member.CommitCount,
                        member.DocumentationCommitCount,
                        member.UnitTestCommitCount,
                        member.ImplementationCommitCount,
                        member.LastContributionAt,
                        member.UpdatedAt))
                    .ToArray(),
                cancellationToken);

        var views = teamRows
            .Select(team =>
            {
                var repository = repositoryLookup.GetValueOrDefault(team.Id);
                return new AssignmentTeamView(
                    team.Id,
                    team.AssignmentId,
                    team.Name,
                    repository?.RepositoryName ?? string.Empty,
                    repository?.RepositoryDisplayName ?? team.Name,
                    memberGroups.GetValueOrDefault(team.Id, []),
                    contributionGroups.GetValueOrDefault(team.Id, []));
            })
            .ToArray();

        return AssignmentTeamListResult.Success(views);
    }
}

public enum AssignmentTeamAccess
{
    Success,
    NotFound,
    Forbidden,
    Invalid,
    Conflict,
}

public sealed record AssignmentTeamCreateResult(
    AssignmentTeamAccess Access,
    AssignmentTeamView? Team,
    string? Error)
{
    public static AssignmentTeamCreateResult Success(AssignmentTeamView team) =>
        new(AssignmentTeamAccess.Success, team, null);

    public static AssignmentTeamCreateResult NotFound() =>
        new(AssignmentTeamAccess.NotFound, null, null);

    public static AssignmentTeamCreateResult Invalid(string error) =>
        new(AssignmentTeamAccess.Invalid, null, error);

    public static AssignmentTeamCreateResult Conflict() =>
        new(AssignmentTeamAccess.Conflict, null, null);
}

public sealed record AssignmentTeamListResult(
    AssignmentTeamAccess Access,
    IReadOnlyList<AssignmentTeamView> Teams)
{
    public static AssignmentTeamListResult Success(
        IReadOnlyList<AssignmentTeamView> teams) =>
        new(AssignmentTeamAccess.Success, teams);

    public static AssignmentTeamListResult NotFound() =>
        new(AssignmentTeamAccess.NotFound, []);

    public static AssignmentTeamListResult Invalid(string error) =>
        new(AssignmentTeamAccess.Invalid, []);
}

public sealed record AssignmentTeamView(
    Guid TeamId,
    Guid AssignmentId,
    string Name,
    string RepositoryName,
    string RepositoryDisplayName,
    IReadOnlyList<AssignmentTeamMemberView> Members,
    IReadOnlyList<AssignmentTeamContributionView> Contributions);

public sealed record AssignmentTeamMemberView(
    Guid UserId,
    string DisplayName,
    string Email,
    DateTimeOffset JoinedAt);

public sealed record AssignmentTeamContributionView(
    Guid UserId,
    string DisplayName,
    int BranchCount,
    int CommitCount,
    int DocumentationCommitCount,
    int UnitTestCommitCount,
    int ImplementationCommitCount,
    DateTimeOffset? LastContributionAt,
    DateTimeOffset UpdatedAt);
