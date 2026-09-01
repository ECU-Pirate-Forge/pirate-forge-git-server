using System.Xml.Linq;

namespace PirateForgeGitServer.ArchitectureTests;

public sealed class ProjectDependencyTests
{
    public static TheoryData<string, string[]> AllowedProjectReferences => new()
    {
        { "PirateForgeGitServer.Domain", [] },
        { "PirateForgeGitServer.Contracts", [] },
        { "PirateForgeGitServer.Application", ["PirateForgeGitServer.Domain"] },
        {
            "PirateForgeGitServer.Infrastructure",
            ["PirateForgeGitServer.Application", "PirateForgeGitServer.Domain"]
        },
        {
            "PirateForgeGitServer.ApiService",
            [
                "PirateForgeGitServer.Application",
                "PirateForgeGitServer.Contracts",
                "PirateForgeGitServer.Infrastructure",
                "PirateForgeGitServer.ServiceDefaults"
            ]
        },
        {
            "PirateForgeGitServer.Web",
            ["PirateForgeGitServer.Contracts", "PirateForgeGitServer.ServiceDefaults"]
        },
        {
            "PirateForgeGitServer.AppHost",
            ["PirateForgeGitServer.ApiService", "PirateForgeGitServer.Web"]
        },
        {
            "PirateForgeGitServer.UnitTests",
            [
                "PirateForgeGitServer.Application",
                "PirateForgeGitServer.Contracts",
                "PirateForgeGitServer.Domain",
                "PirateForgeGitServer.Infrastructure",
                "PirateForgeGitServer.Web"
            ]
        },
        {
            "PirateForgeGitServer.IntegrationTests",
            [
                "PirateForgeGitServer.ApiService",
                "PirateForgeGitServer.Contracts",
                "PirateForgeGitServer.Infrastructure",
                "PirateForgeGitServer.Web"
            ]
        }
    };

    [Theory]
    [MemberData(nameof(AllowedProjectReferences))]
    public void ProjectReferencesFollowArchitecture(
        string projectName,
        string[] allowedReferences)
    {
        var projectPath = Path.Combine(FindSolutionRoot(), projectName, $"{projectName}.csproj");
        var project = XDocument.Load(projectPath);

        var actualReferences = project
            .Descendants("ProjectReference")
            .Select(reference => Path.GetFileNameWithoutExtension(
                reference.Attribute("Include")?.Value))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            allowedReferences.Order(StringComparer.Ordinal),
            actualReferences,
            StringComparer.Ordinal);
    }

    [Fact]
    public void AppHostDefinesSingleHostDockerDeployment()
    {
        var solutionRoot = FindSolutionRoot();
        var appHost = File.ReadAllText(
            Path.Combine(
                solutionRoot,
                "PirateForgeGitServer.AppHost",
                "AppHost.cs"));
        var project = XDocument.Load(
            Path.Combine(
                solutionRoot,
                "PirateForgeGitServer.AppHost",
                "PirateForgeGitServer.AppHost.csproj"));

        Assert.Contains("AddDockerComposeEnvironment", appHost, StringComparison.Ordinal);
        Assert.Contains("WithDashboard(false)", appHost, StringComparison.Ordinal);
        Assert.Contains("PublishAsDockerComposeService", appHost, StringComparison.Ordinal);
        Assert.Contains("service.Volumes.Add", appHost, StringComparison.Ordinal);
        Assert.Contains("PublishAsDockerFile", appHost, StringComparison.Ordinal);
        Assert.Contains("allowed-hosts", appHost, StringComparison.Ordinal);
        Assert.Contains("web-port", appHost, StringComparison.Ordinal);
        Assert.Contains("git-port", appHost, StringComparison.Ordinal);
        Assert.Contains("127.0.0.1", appHost, StringComparison.Ordinal);
        Assert.Contains("bootstrap-admin-password", appHost, StringComparison.Ordinal);
        Assert.Contains(
            project.Descendants("PackageReference"),
            reference => string.Equals(
                reference.Attribute("Include")?.Value,
                "Aspire.Hosting.Docker",
                StringComparison.Ordinal));
    }

    [Fact]
    public void ApiPersistsDataProtectionKeys()
    {
        var source = File.ReadAllText(
            Path.Combine(
                FindSolutionRoot(),
                "PirateForgeGitServer.Infrastructure",
                "ServiceCollectionExtensions.cs"));

        Assert.Contains("PersistKeysToFileSystem", source, StringComparison.Ordinal);
        Assert.Contains(
            "PirateForgeGitServer.ApiService",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ApiContainerIncludesGitAndRunsAsNonRoot()
    {
        var solutionRoot = FindSolutionRoot();
        var dockerfilePath = Path.Combine(
            solutionRoot,
            "PirateForgeGitServer.ApiService",
            "Dockerfile");
        var dockerIgnorePath = Path.Combine(solutionRoot, ".dockerignore");

        Assert.True(File.Exists(dockerfilePath), "The API Dockerfile must exist.");
        Assert.True(File.Exists(dockerIgnorePath), "The Docker ignore file must exist.");

        var dockerfile = File.ReadAllText(dockerfilePath);
        var dockerIgnore = File.ReadAllText(dockerIgnorePath);

        Assert.Contains("apt-get install", dockerfile, StringComparison.Ordinal);
        Assert.Contains("git", dockerfile, StringComparison.Ordinal);
        Assert.Contains("USER $APP_UID", dockerfile, StringComparison.Ordinal);
        Assert.Contains(".pirateforge", dockerIgnore, StringComparison.Ordinal);
        Assert.Contains("Bonobo-Git-Server", dockerIgnore, StringComparison.Ordinal);
        Assert.Contains("Gibbon-Git-Server", dockerIgnore, StringComparison.Ordinal);
    }

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PirateForgeGitServer.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the solution root.");
    }
}
