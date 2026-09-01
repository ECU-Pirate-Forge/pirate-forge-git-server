using Microsoft.EntityFrameworkCore;
using PirateForgeGitServer.Application.Identity;
using PirateForgeGitServer.Domain.Identity;
using PirateForgeGitServer.Infrastructure.Persistence;

namespace PirateForgeGitServer.Infrastructure.Identity;

internal sealed class PersonalAccessTokenRepository(PirateForgeDbContext dbContext)
    : IPersonalAccessTokenRepository
{
    public async Task AddAsync(
        PersonalAccessToken token,
        CancellationToken cancellationToken) =>
        await dbContext.PersonalAccessTokens.AddAsync(token, cancellationToken);

    public Task<PersonalAccessToken?> FindByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken) =>
        dbContext.PersonalAccessTokens.SingleOrDefaultAsync(
            token => token.Prefix == prefix,
            cancellationToken);

    public Task<PersonalAccessToken?> FindByIdAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken) =>
        dbContext.PersonalAccessTokens.SingleOrDefaultAsync(
            token => token.Id == id && token.UserId == userId,
            cancellationToken);

    public async Task<IReadOnlyList<PersonalAccessToken>> ListForUserAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var tokens = await dbContext.PersonalAccessTokens
            .AsNoTracking()
            .Where(token => token.UserId == userId)
            .ToArrayAsync(cancellationToken);

        return tokens
            .OrderByDescending(token => token.CreatedAt)
            .ToArray();
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
