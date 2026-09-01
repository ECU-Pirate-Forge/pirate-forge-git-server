using PirateForgeGitServer.Domain.Identity;

namespace PirateForgeGitServer.Application.Identity;

public interface IPersonalAccessTokenRepository
{
    Task AddAsync(PersonalAccessToken token, CancellationToken cancellationToken);

    Task<PersonalAccessToken?> FindByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken);

    Task<PersonalAccessToken?> FindByIdAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PersonalAccessToken>> ListForUserAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
