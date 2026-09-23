// using Aspire.Hosting.Docker;
// using Aspire.Hosting.Docker.Resources.ServiceNodes;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

// var storageRoot = Path.GetFullPath(
//     Path.Combine(builder.AppHostDirectory, "..", ".pirateforge"));
// const string containerStorageRoot = "/srv/pirateforge";
// var configuredStorageRoot = builder.ExecutionContext.IsPublishMode
//     ? containerStorageRoot
//     : storageRoot;

string webPort = Environment.GetEnvironmentVariable("Parameters__web_port") ?? "8080";
string apiPort = Environment.GetEnvironmentVariable("Parameters__api_port") ?? "8080";
string configuredStorageRoot = Environment.GetEnvironmentVariable("Parameters__storage_root") ?? Path.GetFullPath(
    Path.Combine(builder.AppHostDirectory, "..", ".pirateforge"));
string allowedHosts = Environment.GetEnvironmentVariable("Parameters__allowed_hosts") ?? "*";
string bootstrapAdminEmail = Environment.GetEnvironmentVariable("Parameters__bootstrap_admin_email") ?? "";
string bootstrapAdminDisplayName = Environment.GetEnvironmentVariable("Parameters__bootstrap_admin_display_name") ?? "";
string bootstrapAdminPassword = Environment.GetEnvironmentVariable("Parameters__bootstrap_admin_password") ?? "";


IResourceBuilder<ProjectResource> migrationWorker = builder.AddProject<Projects.PirateForgeGitServer_Worker>("migrationworker")
    .WithEnvironment("PirateForge__Bootstrap_Admin_Email", bootstrapAdminEmail)
    .WithEnvironment("PirateForge__Bootstrap_Admin_Display_Name", bootstrapAdminDisplayName)
    .WithEnvironment("PirateForge__Storage__RootPath", configuredStorageRoot)
    .WithEndpointsInEnvironment(endpoint => endpoint.UriScheme == "http");

IResourceBuilder<ProjectResource> apiService = builder.AddProject<Projects.PirateForgeGitServer_ApiService>("apiservice")
    .WithEnvironment("PirateForge__Storage__RootPath", configuredStorageRoot)
    .WithEnvironment("PirateForge__Api_Port", apiPort)
    .WithEnvironment("PirateForge__Allowed_Hosts", allowedHosts)
    .WithEnvironment("PirateForge__Bootstrap_Admin_Email", bootstrapAdminEmail)
    .WithEnvironment("PirateForge__Bootstrap_Admin_Display_Name", bootstrapAdminDisplayName)
    .WithEndpointsInEnvironment(endpoint => endpoint.UriScheme == "http")
    .WithHttpHealthCheck("/health", endpointName: "http")
    .WaitFor(migrationWorker);

IResourceBuilder<ProjectResource> webFrontend = builder.AddProject<Projects.PirateForgeGitServer_Web>("webfrontend")
    .WithEnvironment("PirateForge__Web_Port", webPort)
    .WithExternalHttpEndpoints()
    .WithEndpointsInEnvironment(endpoint => endpoint.UriScheme == "http")
    .WithHttpHealthCheck("/health", endpointName: "http")
    .WithReference(apiService)
    .WaitFor(apiService);

// if (builder.ExecutionContext.IsPublishMode)
// {
    // var dockerCompose = builder.AddDockerComposeEnvironment("docker-compose")
    //     .WithDashboard(false);


    
    // apiService
    //     .WithEndpointsInEnvironment(endpoint => endpoint.UriScheme == "http");
        // .PublishAsDockerFile(container =>
        //     container.WithDockerfile(
        //         "..",
        //         "PirateForgeGitServer.ApiService/Dockerfile"))
        // .PublishAsDockerComposeService((resource, service) =>
        // {
        //     service.Restart = "unless-stopped";
        //     service.Environment["AllowedHosts"] =
        //         $"apiservice;{allowedHosts.AsEnvironmentPlaceholder(resource)}";
        //     service.Ports.Add(
        //         $"127.0.0.1:{gitPort.AsEnvironmentPlaceholder(resource)}:8080");
        //     service.Networks.Add("pirateforge-network");
        //     service.Volumes.Add(
        //         new Volume
        //         {
        //             Name = "pirateforge-storage",
        //             Type = "bind",
        //             Source = deploymentStoragePath.AsEnvironmentPlaceholder(resource),
        //             Target = containerStorageRoot,
        //         });
        // })

    
    // webFrontend
    //     .WithEndpointsInEnvironment(endpoint => endpoint.UriScheme == "http");
        // .PublishAsDockerComposeService((resource, service) =>
        // {
        //     service.Restart = "unless-stopped";
        //     service.Ports.Clear();
        //     service.Ports.Add(
        //         $"127.0.0.1:{webPort.AsEnvironmentPlaceholder(resource)}:8080");
        //     service.Environment.Remove("APISERVICE_HTTPS");
        //     service.Networks.Add("pirateforge-network");
        //     service.Volumes.Add(
        //         new Volume
        //         {
        //             Name = "pirateforge-web-keys",
        //             Type = "bind",
        //             Source =
        //                 $"{deploymentStoragePath.AsEnvironmentPlaceholder(resource)}/data-protection-keys/web",
        //             Target = $"{containerStorageRoot}/data-protection-keys/web",
        //         });
        // });      
// }

builder.Build().Run();
