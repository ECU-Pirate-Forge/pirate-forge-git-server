using PirateForgeGitServer.ApiService.Identity;
using PirateForgeGitServer.ApiService.Git;
using PirateForgeGitServer.ApiService.Courses;
using PirateForgeGitServer.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();
builder.Services.AddInfrastructure(builder.Configuration);
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

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();
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
