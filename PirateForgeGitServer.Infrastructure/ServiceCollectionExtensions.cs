using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PirateForgeGitServer.Application.Identity;
using PirateForgeGitServer.Domain.Identity;
using PirateForgeGitServer.Infrastructure.Identity;
using PirateForgeGitServer.Infrastructure.Git;
using PirateForgeGitServer.Infrastructure.Persistence;
using PirateForgeGitServer.Infrastructure.Storage;
using PirateForgeGitServer.Infrastructure.Courses;

namespace PirateForgeGitServer.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetRequiredSection(StorageOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.RootPath),
                "A storage root path is required.")
            .ValidateOnStart();

        services.AddOptions<AssignmentGradingOptions>()
            .Bind(configuration.GetSection(AssignmentGradingOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<BootstrapAdminOptions>()
            .Bind(configuration.GetSection(BootstrapAdminOptions.SectionName))
            .Validate(
                options => options.IsEmpty || options.IsConfigured,
                "Bootstrap administrator email, password, and display name must be configured together.")
            .ValidateOnStart();

        var storageOptions = configuration
            .GetRequiredSection(StorageOptions.SectionName)
            .Get<StorageOptions>()
            ?? throw new InvalidOperationException(
                "Pirate Forge storage configuration is missing.");
        var apiKeyPath = Path.Combine(
            Path.GetFullPath(storageOptions.RootPath),
            "data-protection-keys",
            "api");
        Directory.CreateDirectory(apiKeyPath);
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(apiKeyPath))
            .SetApplicationName("PirateForgeGitServer.ApiService");

        services.AddSingleton<StoragePaths>();
        services.AddHostedService<StorageInitializer>();

        services.AddDbContextFactory<PirateForgeDbContext>((serviceProvider, options) =>
        {
            var paths = serviceProvider.GetRequiredService<StoragePaths>();
            options.UseSqlite($"Data Source={paths.DatabasePath}");
        });

        services.AddIdentityCore<PirateForgeUser>(options =>
            {
                options.SignIn.RequireConfirmedEmail = true;
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<PirateForgeDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = PirateForgeAuthenticationDefaults.Scheme;
                options.DefaultChallengeScheme = PirateForgeAuthenticationDefaults.Scheme;
            })
            .AddPolicyScheme(
                PirateForgeAuthenticationDefaults.Scheme,
                displayName: null,
                options => options.ForwardDefaultSelector = context =>
                    context.Request.Path.Value?.Contains(
                        ".git/",
                        StringComparison.OrdinalIgnoreCase) is true
                    || context.Request.Headers.Authorization
                        .ToString()
                        .StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)
                        ? PirateForgeAuthenticationDefaults.PersonalAccessTokenScheme
                        : IdentityConstants.BearerScheme)
            .AddBearerToken(IdentityConstants.BearerScheme, options =>
            {
                options.BearerTokenExpiration = TimeSpan.FromHours(1);
                options.RefreshTokenExpiration = TimeSpan.FromDays(14);
            })
            .AddScheme<AuthenticationSchemeOptions, PersonalAccessTokenAuthenticationHandler>(
                PirateForgeAuthenticationDefaults.PersonalAccessTokenScheme,
                configureOptions: null);

        var activeAccountRequirement = new ActiveAccountRequirement();
        services.AddAuthorizationBuilder()
            .SetDefaultPolicy(
                new AuthorizationPolicyBuilder(PirateForgeAuthenticationDefaults.Scheme)
                    .RequireAuthenticatedUser()
                    .AddRequirements(activeAccountRequirement)
                    .Build())
            .AddPolicy(
                AuthorizationPolicies.AdministerSystem,
                policy => policy
                    .RequireRole(ApplicationRoles.Administrator)
                    .AddRequirements(activeAccountRequirement))
            .AddPolicy(
                AuthorizationPolicies.ManageCourses,
                policy => policy
                    .RequireRole(
                        ApplicationRoles.Administrator,
                        ApplicationRoles.Instructor)
                    .AddRequirements(activeAccountRequirement))
            .AddPolicy(
                AuthorizationPolicies.ReadCourseRepositories,
                policy => policy
                    .RequireRole(
                        ApplicationRoles.Administrator,
                        ApplicationRoles.Instructor,
                        ApplicationRoles.TeachingAssistant,
                        ApplicationRoles.Student)
                    .AddRequirements(activeAccountRequirement));

        services.AddScoped<IAuthorizationHandler, ActiveAccountAuthorizationHandler>();
        services.AddScoped<GitRepositoryService>();
        services.AddScoped<GitRepositoryAccessService>();
        services.AddScoped<CourseService>();
        services.AddScoped<CourseRosterService>();
        services.AddScoped<CourseAssignmentService>();
        services.AddScoped<AssignmentProvisioningService>();
        services.AddScoped<AssignmentStatusService>();
        services.AddScoped<CourseAuditService>();
        services.AddScoped<AssignmentArchiveService>();
        services.AddScoped<AssignmentSubmissionService>();
        services.AddSingleton<AssignmentSubmissionQueue>();
        services.AddHostedService<AssignmentSubmissionProcessor>();
        services.AddScoped<IPersonalAccessTokenRepository, PersonalAccessTokenRepository>();
        services.AddScoped<PersonalAccessTokenService>();
        services.AddSingleton(TimeProvider.System);
        services.AddHostedService<DatabaseMigrationService>();
        services.AddHostedService<IdentityBootstrapService>();

        services.AddHealthChecks()
            .AddCheck<StorageHealthCheck>("storage", tags: ["ready"])
            .AddCheck<SqliteHealthCheck>("sqlite", tags: ["ready"]);

        return services;
    }
}
