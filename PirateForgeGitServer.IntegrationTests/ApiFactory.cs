using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using PirateForgeGitServer.ApiService;

namespace PirateForgeGitServer.IntegrationTests;

public sealed class ApiFactory : WebApplicationFactory<AssemblyMarker>
{
    public const string AdministratorEmail = "admin@example.edu";
    public const string AdministratorPassword = "Correct-Horse-42!";

    private readonly string _storageRoot =
        Path.Combine(Path.GetTempPath(), $"pirate-forge-api-tests-{Guid.NewGuid():N}");

    public string StorageRoot => _storageRoot;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("PirateForge:Storage:RootPath", _storageRoot);
        builder.UseSetting(
            "PirateForge:BootstrapAdmin:Email",
            AdministratorEmail);
        builder.UseSetting(
            "PirateForge:BootstrapAdmin:Password",
            AdministratorPassword);
        builder.UseSetting(
            "PirateForge:BootstrapAdmin:DisplayName",
            "Test Administrator");
        builder.UseEnvironment("Development");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && Directory.Exists(_storageRoot))
        {
            SqliteConnection.ClearAllPools();
            foreach (var file in Directory.EnumerateFiles(
                         _storageRoot,
                         "*",
                         SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_storageRoot, recursive: true);
        }
    }
}
