using System.Security.Claims;
using PirateForgeGitServer.Application.Identity;
using PirateForgeGitServer.Contracts.Identity;
using PirateForgeGitServer.Domain.Identity;

namespace PirateForgeGitServer.ApiService.Identity;

internal static class PersonalAccessTokenEndpoints
{
    public static IEndpointRouteBuilder MapPersonalAccessTokenEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/personal-access-tokens")
            .WithTags("Personal access tokens")
            .RequireAuthorization();

        group.MapGet(
            "/",
            async (
                ClaimsPrincipal user,
                PersonalAccessTokenService service,
                CancellationToken cancellationToken) =>
            {
                var tokens = await service.ListAsync(
                    GetUserId(user),
                    cancellationToken);
                return TypedResults.Ok(tokens.Select(ToResponse).ToArray());
            });

        group.MapPost(
            "/",
            async Task<IResult> (
                CreatePersonalAccessTokenRequest request,
                ClaimsPrincipal user,
                PersonalAccessTokenService service,
                TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                var name = request.Name.Trim();
                if (name.Length is < 1 or > 100
                    || request.ExpiresAt <= timeProvider.GetUtcNow())
                {
                    return TypedResults.ValidationProblem(
                        new Dictionary<string, string[]>
                        {
                            ["Token"] =
                            [
                                "A name of 1-100 characters and a future expiration are required."
                            ]
                        });
                }

                var generated = await service.CreateAsync(
                    GetUserId(user),
                    name,
                    request.ExpiresAt,
                    cancellationToken);
                var record = generated.Record;
                return TypedResults.Created(
                    $"/api/personal-access-tokens/{record.Id}",
                    new CreatedPersonalAccessTokenResponse(
                        record.Id,
                        record.Name,
                        record.Prefix,
                        generated.Token,
                        record.CreatedAt,
                        record.ExpiresAt));
            });

        group.MapDelete(
            "/{id:guid}",
            async Task<IResult> (
                Guid id,
                ClaimsPrincipal user,
                PersonalAccessTokenService service,
                CancellationToken cancellationToken) =>
                await service.RevokeAsync(
                    id,
                    GetUserId(user),
                    cancellationToken)
                    ? TypedResults.NoContent()
                    : TypedResults.NotFound());

        return endpoints;
    }

    private static Guid GetUserId(ClaimsPrincipal user) =>
        Guid.Parse(
            user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("The user identifier is missing."));

    private static PersonalAccessTokenResponse ToResponse(PersonalAccessToken token) =>
        new(
            token.Id,
            token.Name,
            token.Prefix,
            token.CreatedAt,
            token.ExpiresAt,
            token.RevokedAt,
            token.LastUsedAt);
}
