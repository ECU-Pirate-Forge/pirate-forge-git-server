using System.Security.Cryptography;
using System.Text;
using PirateForgeGitServer.Application.Identity;
using PirateForgeGitServer.Domain.Identity;

namespace PirateForgeGitServer.UnitTests.Identity;

public sealed class PersonalAccessTokenServiceTests
{
    [Fact]
    public async Task CreateStoresAHashAndReturnsTheSecretOnce()
    {
        var repository = new FakeTokenRepository();
        var service = new PersonalAccessTokenService(repository, TimeProvider.System);

        var generated = await service.CreateAsync(
            Guid.NewGuid(),
            "Laptop",
            DateTimeOffset.UtcNow.AddDays(30));

        var stored = Assert.Single(repository.Tokens);
        Assert.StartsWith("pfg_", generated.Token, StringComparison.Ordinal);
        Assert.Same(stored, generated.Record);
        Assert.Equal(
            SHA256.HashData(Encoding.UTF8.GetBytes(generated.Token)),
            stored.SecretHash);
        Assert.DoesNotContain(
            generated.Token,
            Convert.ToHexString(stored.SecretHash),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateReturnsTheOwnerForAnActiveToken()
    {
        var repository = new FakeTokenRepository();
        var service = new PersonalAccessTokenService(repository, TimeProvider.System);
        var userId = Guid.NewGuid();
        var generated = await service.CreateAsync(userId, "Laptop", expiresAt: null);

        var validatedUserId = await service.ValidateAsync(generated.Token);

        Assert.Equal(userId, validatedUserId);
        Assert.NotNull(generated.Record.LastUsedAt);
    }

    [Fact]
    public async Task RevokePreventsFutureValidation()
    {
        var repository = new FakeTokenRepository();
        var service = new PersonalAccessTokenService(repository, TimeProvider.System);
        var userId = Guid.NewGuid();
        var generated = await service.CreateAsync(userId, "Laptop", expiresAt: null);

        var revoked = await service.RevokeAsync(generated.Record.Id, userId);

        Assert.True(revoked);
        Assert.Null(await service.ValidateAsync(generated.Token));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("pfg_short_secret")]
    public async Task ValidateRejectsMalformedTokens(string token)
    {
        var service = new PersonalAccessTokenService(
            new FakeTokenRepository(),
            TimeProvider.System);

        Assert.Null(await service.ValidateAsync(token));
    }

    [Fact]
    public async Task ValidateRejectsATamperedToken()
    {
        var repository = new FakeTokenRepository();
        var service = new PersonalAccessTokenService(repository, TimeProvider.System);
        var generated = await service.CreateAsync(
            Guid.NewGuid(),
            "Laptop",
            expiresAt: null);

        Assert.Null(await service.ValidateAsync($"{generated.Token}0"));
    }

    [Fact]
    public async Task ValidateRejectsAnExpiredToken()
    {
        var timeProvider = new AdjustableTimeProvider(
            new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero));
        var repository = new FakeTokenRepository();
        var service = new PersonalAccessTokenService(repository, timeProvider);
        var generated = await service.CreateAsync(
            Guid.NewGuid(),
            "Laptop",
            timeProvider.GetUtcNow().AddMinutes(1));
        timeProvider.Advance(TimeSpan.FromMinutes(2));

        Assert.Null(await service.ValidateAsync(generated.Token));
    }

    private sealed class FakeTokenRepository : IPersonalAccessTokenRepository
    {
        public List<PersonalAccessToken> Tokens { get; } = [];

        public Task AddAsync(
            PersonalAccessToken token,
            CancellationToken cancellationToken)
        {
            Tokens.Add(token);
            return Task.CompletedTask;
        }

        public Task<PersonalAccessToken?> FindByPrefixAsync(
            string prefix,
            CancellationToken cancellationToken) =>
            Task.FromResult(Tokens.SingleOrDefault(token => token.Prefix == prefix));

        public Task<PersonalAccessToken?> FindByIdAsync(
            Guid id,
            Guid userId,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Tokens.SingleOrDefault(token => token.Id == id && token.UserId == userId));

        public Task<IReadOnlyList<PersonalAccessToken>> ListForUserAsync(
            Guid userId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PersonalAccessToken>>(
                Tokens.Where(token => token.UserId == userId).ToArray());

        public Task SaveChangesAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;

        public void Advance(TimeSpan duration) => utcNow += duration;
    }
}
