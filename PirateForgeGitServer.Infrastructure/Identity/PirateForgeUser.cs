using Microsoft.AspNetCore.Identity;

namespace PirateForgeGitServer.Infrastructure.Identity;

public sealed class PirateForgeUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}
