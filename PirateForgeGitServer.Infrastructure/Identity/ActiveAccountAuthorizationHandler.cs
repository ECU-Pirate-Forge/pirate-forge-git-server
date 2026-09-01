using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

namespace PirateForgeGitServer.Infrastructure.Identity;

internal sealed class ActiveAccountRequirement : IAuthorizationRequirement;

internal sealed class ActiveAccountAuthorizationHandler(
    UserManager<PirateForgeUser> userManager)
    : AuthorizationHandler<ActiveAccountRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ActiveAccountRequirement requirement)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            return;
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user?.IsActive is true)
        {
            context.Succeed(requirement);
        }
    }
}
