using PirateForgeGitServer.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddInfrastructure(builder.Configuration, runDatabaseMigrations: true);

var host = builder.Build();
await host.RunAsync();
