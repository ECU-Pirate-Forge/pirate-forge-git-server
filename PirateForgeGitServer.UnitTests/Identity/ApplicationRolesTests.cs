using PirateForgeGitServer.Domain.Identity;

namespace PirateForgeGitServer.UnitTests.Identity;

public sealed class ApplicationRolesTests
{
    [Fact]
    public void AllContainsExactlyTheFourSupportedRoles()
    {
        Assert.Equal(
            [
                ApplicationRoles.Administrator,
                ApplicationRoles.Instructor,
                ApplicationRoles.Student,
                ApplicationRoles.TeachingAssistant
            ],
            ApplicationRoles.All);
    }
}
