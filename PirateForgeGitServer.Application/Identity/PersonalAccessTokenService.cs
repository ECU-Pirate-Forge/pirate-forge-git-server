using System.Security.Cryptography;
using System.Text;
using PirateForgeGitServer.Domain.Identity;

namespace PirateForgeGitServer.Application.Identity;

public sealed class PersonalAccessTokenService(
    IPersonalAccessTokenRepository repository,
    TimeProvider timeProvider)
{
    public Task<GeneratedPersonalAccessToken> CreateAsync(
        Guid userId,
        string name,
        DateTimeOffset? expiresAt,
        CancellationToken cancellationToken = default)
    {
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32))
            .ToLowerInvariant();
        var prefix = secret[..12];
        var rawToken = $"pfg_{prefix}_{secret}";
        var createdAt = timeProvider.GetUtcNow();
        var record = PersonalAccessToken.Create(
            userId,
            name,
            prefix,
            SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)),
            createdAt,
            expiresAt);

        return StoreAsync(record, rawToken, cancellationToken);
    }

    public async Task<Guid?> ValidateAsync(
        string rawToken,
        CancellationToken cancellationToken = default)
    {
        var segments = rawToken.Split('_', 3, StringSplitOptions.None);
        if (segments is not ["pfg", var prefix, _] || prefix.Length != 12)
        {
            return null;
        }

        var record = await repository.FindByPrefixAsync(prefix, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (record is null || !record.IsActive(now))
        {
            return null;
        }

        var presentedHash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        if (!CryptographicOperations.FixedTimeEquals(
                presentedHash,
                record.SecretHash))
        {
            return null;
        }

        record.RecordUsed(now);
        await repository.SaveChangesAsync(cancellationToken);
        return record.UserId;
    }

    public async Task<bool> RevokeAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var record = await repository.FindByIdAsync(id, userId, cancellationToken);
        if (record is null)
        {
            return false;
        }

        record.Revoke(timeProvider.GetUtcNow());
        await repository.SaveChangesAsync(cancellationToken);
        return true;
    }

    public Task<IReadOnlyList<PersonalAccessToken>> ListAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        repository.ListForUserAsync(userId, cancellationToken);

    private async Task<GeneratedPersonalAccessToken> StoreAsync(
        PersonalAccessToken record,
        string rawToken,
        CancellationToken cancellationToken)
    {
        await repository.AddAsync(record, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return new GeneratedPersonalAccessToken(rawToken, record);
    }
}
