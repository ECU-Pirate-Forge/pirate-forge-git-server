using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using PirateForgeGitServer.Contracts.Identity;
using PirateForgeGitServer.Infrastructure.Identity;

namespace PirateForgeGitServer.ApiService.Identity;

internal static class IdentityEndpoints
{
    internal const string AuthenticationRateLimitPolicy = "authentication";

    public static IEndpointRouteBuilder MapIdentityEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth")
            .WithTags("Identity");

        group.MapPost(
            "/login",
            async Task<IResult> (
                LoginRequest request,
                UserManager<PirateForgeUser> userManager,
                SignInManager<PirateForgeUser> signInManager) =>
            {
                var user = await userManager.FindByEmailAsync(request.Email.Trim());
                if (user is null || !user.IsActive)
                {
                    return TypedResults.Unauthorized();
                }

                var result = await signInManager.CheckPasswordSignInAsync(
                    user,
                    request.Password,
                    lockoutOnFailure: true);
                if (!result.Succeeded)
                {
                    return TypedResults.Unauthorized();
                }

                var principal = await signInManager.CreateUserPrincipalAsync(user);
                return TypedResults.SignIn(
                    principal,
                    authenticationScheme: IdentityConstants.BearerScheme);
            })
            .RequireRateLimiting(AuthenticationRateLimitPolicy);

        group.MapPost(
            "/activate",
            async Task<IResult> (
                ActivateAccountRequest request,
                UserManager<PirateForgeUser> userManager) =>
            {
                var user = await userManager.FindByIdAsync(request.UserId.ToString());
                if (user is null || await userManager.HasPasswordAsync(user))
                {
                    return TypedResults.BadRequest();
                }

                var validToken = await userManager.VerifyUserTokenAsync(
                    user,
                    TokenOptions.DefaultProvider,
                    AccountTokenPurposes.Activation,
                    request.Token);
                if (!validToken)
                {
                    return TypedResults.BadRequest();
                }

                var passwordResult = await userManager.AddPasswordAsync(
                    user,
                    request.Password);
                if (!passwordResult.Succeeded)
                {
                    return passwordResult.ToValidationProblem();
                }

                user.EmailConfirmed = true;
                var updateResult = await userManager.UpdateAsync(user);
                return updateResult.Succeeded
                    ? TypedResults.NoContent()
                    : updateResult.ToValidationProblem();
            })
            .RequireRateLimiting(AuthenticationRateLimitPolicy);

        group.MapPost(
            "/reset-password",
            async Task<IResult> (
                ResetPasswordRequest request,
                UserManager<PirateForgeUser> userManager) =>
            {
                var user = await userManager.FindByIdAsync(request.UserId.ToString());
                if (user is null)
                {
                    return TypedResults.BadRequest();
                }

                var result = await userManager.ResetPasswordAsync(
                    user,
                    request.Token,
                    request.Password);
                return result.Succeeded
                    ? TypedResults.NoContent()
                    : result.ToValidationProblem();
            })
            .RequireRateLimiting(AuthenticationRateLimitPolicy);

        group.MapGet("/me", (ClaimsPrincipal user) =>
            {
                var id = Guid.Parse(
                    user.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? throw new InvalidOperationException("The user identifier is missing."));
                var roles = user.FindAll(ClaimTypes.Role)
                    .Select(claim => claim.Value)
                    .Order(StringComparer.Ordinal)
                    .ToArray();

                return TypedResults.Ok(new CurrentUserResponse(
                    id,
                    user.FindFirstValue(ClaimTypes.Email) ?? string.Empty,
                    user.FindFirstValue(ClaimTypes.Name) ?? string.Empty,
                    roles));
            })
            .RequireAuthorization();

        return endpoints;
    }
}
