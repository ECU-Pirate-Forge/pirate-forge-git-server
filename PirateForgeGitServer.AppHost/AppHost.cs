using Aspire.Hosting.Docker;
using Aspire.Hosting.Docker.Resources.ServiceNodes;

var builder = DistributedApplication.CreateBuilder(args);

var storageRoot = Path.GetFullPath(
    Path.Combine(builder.AppHostDirectory, "..", ".pirateforge"));
const string containerStorageRoot = "/var/lib/pirateforge";
var configuredStorageRoot = builder.ExecutionContext.IsPublishMode
    ? containerStorageRoot
    : storageRoot;

var migrationWorker = builder.AddProject<Projects.PirateForgeGitServer_Worker>("migrationworker")
    .WithEnvironment("PirateForge__Storage__RootPath", configuredStorageRoot);

var apiService = builder.AddProject<Projects.PirateForgeGitServer_ApiService>("apiservice")
    .WithEnvironment("PirateForge__Storage__RootPath", configuredStorageRoot)
    .WithHttpHealthCheck("/health")
    .WaitFor(migrationWorker);

var webFrontend = builder.AddProject<Projects.PirateForgeGitServer_Web>("webfrontend")
    .WithEnvironment("PirateForge__Storage__RootPath", configuredStorageRoot)
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WaitFor(apiService);

if (builder.ExecutionContext.IsPublishMode)
{
    builder.AddDockerComposeEnvironment("docker-compose")
        .WithDashboard(false);

    var deploymentStoragePath = builder.AddParameter("storage-path");
    var allowedHosts = builder.AddParameter("allowed-hosts");
    var webPort = builder.AddParameter("web-port");
    var gitPort = builder.AddParameter("git-port");
    var bootstrapAdminEmail = builder.AddParameter("bootstrap-admin-email");
    var bootstrapAdminPassword = builder.AddParameter(
        "bootstrap-admin-password",
        secret: true);
    var bootstrapAdminDisplayName = builder.AddParameter(
        "bootstrap-admin-display-name");
    apiService
        .WithEndpointsInEnvironment(endpoint => endpoint.UriScheme == "http")
        .PublishAsDockerFile(container =>
            container.WithDockerfile(
                "..",
                "PirateForgeGitServer.ApiService/Dockerfile"))
        .PublishAsDockerComposeService((resource, service) =>
        {
            service.Restart = "unless-stopped";
            service.Environment["AllowedHosts"] =
                $"apiservice;{allowedHosts.AsEnvironmentPlaceholder(resource)}";
            service.Ports.Add(
                $"127.0.0.1:{gitPort.AsEnvironmentPlaceholder(resource)}:8080");
            service.Volumes.Add(
                new Volume
                {
                    Name = "pirateforge-storage",
                    Type = "bind",
                    Source = deploymentStoragePath.AsEnvironmentPlaceholder(resource),
                    Target = containerStorageRoot,
                });
        })
        .WithEnvironment(
            "PirateForge__BootstrapAdmin__Email",
            bootstrapAdminEmail)
        .WithEnvironment(
            "PirateForge__BootstrapAdmin__Password",
            bootstrapAdminPassword)
        .WithEnvironment(
            "PirateForge__BootstrapAdmin__DisplayName",
            bootstrapAdminDisplayName);
    webFrontend
        .WithEnvironment("AllowedHosts", allowedHosts)
        .WithEnvironment("HTTP_PORTS", "8080")
        .PublishAsDockerComposeService((resource, service) =>
        {
            service.Restart = "unless-stopped";
            service.Ports.Clear();
            service.Ports.Add(
                $"127.0.0.1:{webPort.AsEnvironmentPlaceholder(resource)}:8080");
            service.Environment.Remove("APISERVICE_HTTPS");
            service.Volumes.Add(
                new Volume
                {
                    Name = "pirateforge-web-keys",
                    Type = "bind",
                    Source =
                        $"{deploymentStoragePath.AsEnvironmentPlaceholder(resource)}/data-protection-keys/web",
                    Target = $"{containerStorageRoot}/data-protection-keys/web",
                });
        });
}

builder.Build().Run();
