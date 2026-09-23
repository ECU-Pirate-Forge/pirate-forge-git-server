using PirateForgeGitServer.ApiService.Identity;
using PirateForgeGitServer.ApiService.Git;
using PirateForgeGitServer.ApiService.Courses;
using PirateForgeGitServer.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);


var apiPort = builder.Configuration["pirateforge:api_port"] ?? "8080";
var storagePath = builder.Configuration["pirateforge:storage_path"] ?? Path.GetFullPath(
    Path.Combine(builder.Environment.ContentRootPath, "..", ".pirateforge"));
var webPort = builder.Configuration["pirateforge:web_port"] ?? "8080";
var allowedHosts = builder.Configuration["pirateforge:allowed_hosts"] ?? "*";
var bootstrapAdminEmail = builder.Configuration["pirateforge:bootstrap_admin_email"];
var bootstrapAdminDisplayName = builder.Configuration["pirateforge:bootstrap_admin_display_name"];



// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();
builder.Services.AddInfrastructure(builder.Configuration, runDatabaseMigrations: false);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(
        IdentityEndpoints.AuthenticationRateLimitPolicy,
        context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 1_000,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(builder =>
    {
        builder.AllowAnyOrigin()
               .AllowAnyMethod()
               .AllowAnyHeader();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{

}

app.MapGet("/", () => "Pirate Forge Git Service is running.");

app.MapIdentityEndpoints();
app.MapAdminUserEndpoints();
app.MapPersonalAccessTokenEndpoints();
app.MapRepositoryEndpoints();
app.MapGitSmartHttpEndpoints();
app.MapCourseEndpoints();
app.MapAssignmentEndpoints();
app.MapCourseAuditEndpoints();

app.MapDefaultEndpoints();

app.Run();
