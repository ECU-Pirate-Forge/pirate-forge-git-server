using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PirateForgeGitServer.Domain.Git;
using PirateForgeGitServer.Domain.Identity;
using PirateForgeGitServer.Infrastructure.Persistence;

namespace PirateForgeGitServer.Infrastructure.Git;

public sealed class GitRepositoryAccessService(PirateForgeDbContext dbContext)
{
    public async Task<IReadOnlyList<GitRepository>> ListAccessibleAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var query = dbContext.GitRepositories.AsNoTracking();
        if (!principal.IsInRole(ApplicationRoles.Administrator))
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userId, out var parsedUserId))
            {
                return [];
            }

            query = ApplyAccessFilter(query, parsedUserId, write: false);
        }

        return await query
            .OrderBy(repository => repository.Name)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<GitRepository?> FindAccessibleAsync(
        string repositoryName,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken,
        bool write = false)
    {
        string normalizedName;
        try
        {
            normalizedName = GitRepository.NormalizeName(repositoryName);
        }
        catch (ArgumentException)
        {
            return null;
        }

        if (principal.IsInRole(ApplicationRoles.Administrator))
        {
            return await dbContext.GitRepositories.SingleOrDefaultAsync(
                candidate => candidate.Name == normalizedName,
                cancellationToken);
        }

        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userId, out var parsedUserId))
        {
            return null;
        }

        return await ApplyAccessFilter(
                dbContext.GitRepositories,
                parsedUserId,
                write)
            .SingleOrDefaultAsync(
                candidate => candidate.Name == normalizedName,
                cancellationToken);
    }

    private IQueryable<GitRepository> ApplyAccessFilter(
        IQueryable<GitRepository> repositories,
        Guid userId,
        bool write)
    {
        var courseIds = dbContext.CourseMemberships
            .Where(membership =>
                membership.UserId == userId
                && (membership.Role == ApplicationRoles.Instructor
                    || !write
                    && membership.Role
                        == ApplicationRoles.TeachingAssistant))
            .Select(membership => membership.CourseId);
        var assignmentIds = dbContext.Assignments
            .Where(assignment => courseIds.Contains(assignment.CourseId))
            .Select(assignment => assignment.Id);
        var staffRepositoryIds = dbContext.AssignmentRepositories
            .Where(link => assignmentIds.Contains(link.AssignmentId))
            .Select(link => link.RepositoryId);

        return repositories.Where(repository =>
            repository.OwnerUserId == userId
            || staffRepositoryIds.Contains(repository.Id));
    }
}
