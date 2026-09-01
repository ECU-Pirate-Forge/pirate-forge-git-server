using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PirateForgeGitServer.Contracts.Identity;
using PirateForgeGitServer.Domain.Identity;
using PirateForgeGitServer.Infrastructure.Identity;

namespace PirateForgeGitServer.ApiService.Identity;

internal static class AdminUserEndpoints
{
    public static IEndpointRouteBuilder MapAdminUserEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/users")
            .WithTags("Administration")
            .RequireAuthorization(AuthorizationPolicies.AdministerSystem);

        group.MapGet(
            "/",
            async (UserManager<PirateForgeUser> userManager) =>
            {
                var users = await userManager.Users
                    .OrderBy(user => user.Email)
                    .ToArrayAsync();
                var responses = new List<UserResponse>(users.Length);

                foreach (var user in users)
                {
                    responses.Add(await ToResponseAsync(userManager, user));
                }

                return TypedResults.Ok(responses);
            });

        group.MapPost(
            "/",
            async Task<IResult> (
                CreateUserRequest request,
                UserManager<PirateForgeUser> userManager) =>
            {
                var email = request.Email.Trim();
                var displayName = request.DisplayName.Trim();
                if (string.IsNullOrWhiteSpace(email)
                    || string.IsNullOrWhiteSpace(displayName)
                    || !ApplicationRoles.All.Contains(
                        request.Role,
                        StringComparer.Ordinal))
                {
                    return TypedResults.ValidationProblem(
                        new Dictionary<string, string[]>
                        {
                            ["Account"] =
                            [
                                "Email, display name, and a supported role are required."
                            ]
                        });
                }

                if (await userManager.FindByEmailAsync(email) is not null)
                {
                    return TypedResults.Conflict();
                }

                var user = new PirateForgeUser
                {
                    Id = Guid.NewGuid(),
                    Email = email,
                    UserName = email,
                    DisplayName = displayName,
                    EmailConfirmed = false,
                    IsActive = true
                };

                var createResult = await userManager.CreateAsync(user);
                if (!createResult.Succeeded)
                {
                    return createResult.ToValidationProblem();
                }

                var roleResult = await userManager.AddToRoleAsync(user, request.Role);
                if (!roleResult.Succeeded)
                {
                    await userManager.DeleteAsync(user);
                    return roleResult.ToValidationProblem();
                }

                return TypedResults.Created(
                    $"/api/admin/users/{user.Id}",
                    await ToResponseAsync(userManager, user));
            });

        group.MapPost(
            "/{userId:guid}/activation-token",
            async Task<IResult> (
                Guid userId,
                UserManager<PirateForgeUser> userManager) =>
            {
                var user = await userManager.FindByIdAsync(userId.ToString());
                if (user is null)
                {
                    return TypedResults.NotFound();
                }

                if (await userManager.HasPasswordAsync(user))
                {
                    return TypedResults.Conflict();
                }

                var token = await userManager.GenerateUserTokenAsync(
                    user,
                    TokenOptions.DefaultProvider,
                    AccountTokenPurposes.Activation);
                return TypedResults.Ok(new AccountTokenResponse(
                    user.Id,
                    user.Email ?? string.Empty,
                    token));
            });

        group.MapPost(
            "/{userId:guid}/password-reset-token",
            async Task<IResult> (
                Guid userId,
                UserManager<PirateForgeUser> userManager) =>
            {
                var user = await userManager.FindByIdAsync(userId.ToString());
                if (user is null)
                {
                    return TypedResults.NotFound();
                }

                var token = await userManager.GeneratePasswordResetTokenAsync(user);
                return TypedResults.Ok(new AccountTokenResponse(
                    user.Id,
                    user.Email ?? string.Empty,
                    token));
            });

        group.MapPut(
            "/{userId:guid}/active",
            async Task<IResult> (
                Guid userId,
                SetAccountActiveRequest request,
                UserManager<PirateForgeUser> userManager) =>
            {
                var user = await userManager.FindByIdAsync(userId.ToString());
                if (user is null)
                {
                    return TypedResults.NotFound();
                }

                user.IsActive = request.IsActive;
                var result = await userManager.UpdateSecurityStampAsync(user);
                return result.Succeeded
                    ? TypedResults.NoContent()
                    : result.ToValidationProblem();
            });

        return endpoints;
    }

    private static async Task<UserResponse> ToResponseAsync(
        UserManager<PirateForgeUser> userManager,
        PirateForgeUser user) =>
        new(
            user.Id,
            user.Email ?? string.Empty,
            user.DisplayName,
            user.IsActive,
            await userManager.HasPasswordAsync(user),
            (await userManager.GetRolesAsync(user)).ToArray());
}
