using System.Security.Claims;
using PirateForgeGitServer.Contracts.Identity;
using PirateForgeGitServer.Web.Identity;

namespace PirateForgeGitServer.UnitTests.Identity;

public sealed class WebSessionFactoryTests
{
    [Fact]
    public void CreatePrincipalMapsIdentityAndRoles()
    {
        var user = new CurrentUserResponse(
            Guid.Parse("76ce1da1-134a-4fa8-a7a8-c99fab63cbad"),
            "captain@example.edu",
            "Captain Flint",
            ["Instructor", "Administrator"]);

        var principal = WebSessionFactory.CreatePrincipal(user);

        Assert.Equal(user.Id.ToString(), principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal(user.Email, principal.FindFirstValue(ClaimTypes.Email));
        Assert.Equal(user.DisplayName, principal.FindFirstValue(ClaimTypes.Name));
        Assert.True(principal.IsInRole("Instructor"));
        Assert.True(principal.IsInRole("Administrator"));
    }

    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("https://attacker.example", "/")]
    [InlineData("//attacker.example", "/")]
    [InlineData("/courses/42", "/courses/42")]
    public void NormalizeReturnUrlAllowsOnlyLocalPaths(string? returnUrl, string expected)
    {
        Assert.Equal(expected, WebSessionFactory.NormalizeReturnUrl(returnUrl));
    }
}
