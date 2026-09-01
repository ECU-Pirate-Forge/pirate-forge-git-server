namespace PirateForgeGitServer.Domain.Identity;

public sealed class PersonalAccessToken
{
    private PersonalAccessToken()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Prefix { get; private set; } = string.Empty;

    public byte[] SecretHash { get; private set; } = [];

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public DateTimeOffset? LastUsedAt { get; private set; }

    public static PersonalAccessToken Create(
        Guid userId,
        string name,
        string prefix,
        byte[] secretHash,
        DateTimeOffset createdAt,
        DateTimeOffset? expiresAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = name,
            Prefix = prefix,
            SecretHash = secretHash,
            CreatedAt = createdAt,
            ExpiresAt = expiresAt
        };

    public bool IsActive(DateTimeOffset now) =>
        RevokedAt is null && (ExpiresAt is null || ExpiresAt > now);

    public void RecordUsed(DateTimeOffset usedAt)
    {
        LastUsedAt = usedAt;
    }

    public void Revoke(DateTimeOffset revokedAt)
    {
        RevokedAt ??= revokedAt;
    }
}
