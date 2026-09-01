using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PirateForgeGitServer.Domain.Identity;

namespace PirateForgeGitServer.Infrastructure.Identity;

internal sealed class IdentityBootstrapService(
    IServiceScopeFactory scopeFactory,
    IOptions<BootstrapAdminOptions> options,
    ILogger<IdentityBootstrapService> logger) : IHostedService
{
    private static readonly Action<ILogger, Exception?> LogBootstrapNotConfigured =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(1, nameof(LogBootstrapNotConfigured)),
            "Administrator bootstrap is not configured. Existing accounts are unchanged.");

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var roleManager = scope.ServiceProvider
            .GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        foreach (var roleName in ApplicationRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                EnsureSucceeded(
                    await roleManager.CreateAsync(
                        new IdentityRole<Guid>(roleName)),
                    $"create the {roleName} role");
            }
        }

        if (!options.Value.IsConfigured)
        {
            LogBootstrapNotConfigured(logger, null);
            return;
        }

        var userManager = scope.ServiceProvider
            .GetRequiredService<UserManager<PirateForgeUser>>();
        var email = options.Value.Email!.Trim();
        var user = await userManager.FindByEmailAsync(email);

        if (user is null)
        {
            user = new PirateForgeUser
            {
                Id = Guid.NewGuid(),
                Email = email,
                UserName = email,
                DisplayName = options.Value.DisplayName!.Trim(),
                EmailConfirmed = true,
                IsActive = true
            };

            EnsureSucceeded(
                await userManager.CreateAsync(user, options.Value.Password!),
                "create the bootstrap administrator");
        }

        if (!await userManager.IsInRoleAsync(user, ApplicationRoles.Administrator))
        {
            EnsureSucceeded(
                await userManager.AddToRoleAsync(
                    user,
                    ApplicationRoles.Administrator),
                "assign the Administrator role");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (result.Succeeded)
        {
            return;
        }

        var errors = string.Join(
            "; ",
            result.Errors.Select(error => $"{error.Code}: {error.Description}"));
        throw new InvalidOperationException($"Failed to {operation}: {errors}");
    }
}
