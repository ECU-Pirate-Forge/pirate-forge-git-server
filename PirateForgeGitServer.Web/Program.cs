using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.FluentUI.AspNetCore.Components;
using PirateForgeGitServer.Web;
using PirateForgeGitServer.Web.Components;
using PirateForgeGitServer.Web.Identity;
using PirateForgeGitServer.Web.Repositories;
using PirateForgeGitServer.Web.Courses;
using PirateForgeGitServer.Contracts.Courses;
using PirateForgeGitServer.Contracts.Git;
using PirateForgeGitServer.Web.Assignments;
using System.Text;
using System.Globalization;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
const string LoginRateLimitPolicy = "login";

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();

var storageRoot = builder.Configuration["PirateForge:Storage:RootPath"]
    ?? Path.GetFullPath(
        Path.Combine(builder.Environment.ContentRootPath, "..", ".pirateforge"));
var dataProtectionKeysPath = Path.Combine(storageRoot, "data-protection-keys", "web");
Directory.CreateDirectory(dataProtectionKeysPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath))
    .SetApplicationName("PirateForgeGitServer.Web");

builder.Services.AddAuthentication(WebAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(
        WebAuthenticationDefaults.AuthenticationScheme,
        options =>
        {
            options.Cookie.Name = "__Host-PirateForge.Session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.LoginPath = "/login";
            options.AccessDeniedPath = "/access-denied";
            options.SlidingExpiration = false;
        });
builder.Services.AddAuthorization();
builder.Services.AddScoped<PersonalAccessTokenFlashStore>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(
        LoginRateLimitPolicy,
        context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
});

builder.Services.AddFluentUIComponents();

builder.Services.AddOutputCache();


builder.Services.AddHttpClient<IdentityApiClient>(client =>
    client.BaseAddress = new("https+http://apiservice"));
builder.Services.AddHttpClient<PersonalAccessTokenApiClient>(client =>
    client.BaseAddress = new("https+http://apiservice"));
builder.Services.AddHttpClient<RepositoryApiClient>(client =>
    client.BaseAddress = new("https+http://apiservice"));
builder.Services.AddHttpClient<CourseApiClient>(client =>
    client.BaseAddress = new("https+http://apiservice"));
builder.Services.AddHttpClient<AssignmentApiClient>(client =>
    client.BaseAddress = new("https+http://apiservice"));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();

app.UseOutputCache();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapPost(
        "/account/login",
        async Task<IResult> (
            [FromForm] WebLoginForm form,
            IdentityApiClient identityApiClient,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var session = await identityApiClient.LoginAsync(
                form.Email,
                form.Password,
                cancellationToken);
            if (session is null)
            {
                var query = QueryString.Create(
                    [
                        new KeyValuePair<string, string?>("error", "invalid-credentials"),
                        new KeyValuePair<string, string?>(
                            "returnUrl",
                            WebSessionFactory.NormalizeReturnUrl(form.ReturnUrl)),
                    ]);
                return TypedResults.LocalRedirect($"/login{query}");
            }

            var properties = new AuthenticationProperties
            {
                ExpiresUtc = session.ExpiresAt,
                IsPersistent = form.RememberMe,
            };
            properties.StoreTokens(
                [
                    new AuthenticationToken
                    {
                        Name = "access_token",
                        Value = session.AccessToken,
                    },
                    new AuthenticationToken
                    {
                        Name = "refresh_token",
                        Value = session.RefreshToken,
                    },
                ]);

            await context.SignInAsync(
                WebAuthenticationDefaults.AuthenticationScheme,
                WebSessionFactory.CreatePrincipal(session.User),
                properties);
            return TypedResults.LocalRedirect(
                WebSessionFactory.NormalizeReturnUrl(form.ReturnUrl));
        })
    .AllowAnonymous()
    .RequireRateLimiting(LoginRateLimitPolicy);

app.MapPost(
        "/account/logout",
        async Task<IResult> (
            [FromForm] WebLogoutForm form,
            HttpContext context) =>
        {
            await context.SignOutAsync(WebAuthenticationDefaults.AuthenticationScheme);
            return TypedResults.LocalRedirect(
                WebSessionFactory.NormalizeReturnUrl(form.ReturnUrl));
        })
    .RequireAuthorization();

app.MapPost(
        "/account/tokens/create",
        async Task<IResult> (
            [FromForm] CreatePersonalAccessTokenForm form,
            PersonalAccessTokenApiClient tokenApi,
            PersonalAccessTokenFlashStore flashStore,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var token = await tokenApi.CreateAsync(
                new PirateForgeGitServer.Contracts.Identity.CreatePersonalAccessTokenRequest(
                    form.Name,
                    ExpiresAt: null),
                cancellationToken);
            if (token is null)
            {
                return TypedResults.LocalRedirect(
                    "/account/tokens?error=invalid");
            }

            flashStore.Store(context.Response, token.Token);
            return TypedResults.LocalRedirect("/account/tokens?created=true");
        })
    .RequireAuthorization();

app.MapPost(
        "/account/tokens/{id:guid}/revoke",
        async Task<IResult> (
            Guid id,
            [FromForm] RevokePersonalAccessTokenForm _,
            PersonalAccessTokenApiClient tokenApi,
            CancellationToken cancellationToken) =>
        {
            var revoked = await tokenApi.RevokeAsync(
                id,
                cancellationToken);
            return TypedResults.LocalRedirect(
                revoked
                    ? "/account/tokens?revoked=true"
                    : "/account/tokens?error=not-found");
        })
    .RequireAuthorization();

app.MapPost(
        "/account/activate",
        async Task<IResult> (
            [FromForm] ActivateAccountForm form,
            IdentityApiClient identityApiClient,
            CancellationToken cancellationToken) =>
        {
            var activated = form.Password == form.ConfirmPassword
                && await identityApiClient.ActivateAsync(
                    form.UserId,
                    form.Token,
                    form.Password,
                    cancellationToken);
            if (activated)
            {
                return TypedResults.LocalRedirect("/login?activated=true");
            }

            var query = QueryString.Create(
                [
                    new KeyValuePair<string, string?>(
                        "userId",
                        form.UserId.ToString()),
                    new KeyValuePair<string, string?>("token", form.Token),
                    new KeyValuePair<string, string?>("error", "invalid"),
                ]);
            return TypedResults.LocalRedirect($"/activate{query}");
        })
    .AllowAnonymous();

app.MapGet(
        "/repositories/{repositoryName}/download",
        async Task<IResult> (
            string repositoryName,
            string branch,
            RepositoryApiClient repositoryApi,
            CancellationToken cancellationToken) =>
        {
            var archive = await repositoryApi.DownloadArchiveAsync(
                repositoryName,
                branch,
                cancellationToken);
            return TypedResults.File(
                archive,
                "application/zip",
                $"{repositoryName}-{branch}.zip");
        })
    .RequireAuthorization();

app.MapGet(
        "/courses/{courseId:guid}/assignments/{assignmentId:guid}/download",
        async Task<IResult> (
            Guid courseId,
            Guid assignmentId,
            AssignmentApiClient assignmentApi,
            CancellationToken cancellationToken) =>
        {
            var archive = await assignmentApi.DownloadArchiveAsync(
                courseId,
                assignmentId,
                cancellationToken);
            return archive.Status switch
            {
                AssignmentArchiveDownloadStatus.Success => TypedResults.File(
                    archive.Content!,
                    "application/zip",
                    $"assignment-{assignmentId}-repositories.zip"),
                AssignmentArchiveDownloadStatus.Forbidden =>
                    Results.StatusCode(StatusCodes.Status403Forbidden),
                _ => TypedResults.NotFound(),
            };
        })
    .RequireAuthorization(policy =>
        policy.RequireRole("Administrator", "Instructor", "TA"));

app.MapPost(
        "/courses/create",
        async Task<IResult> (
            [FromForm] CreateCourseForm form,
            CourseApiClient courseApi,
            CancellationToken cancellationToken) =>
        {
            var result = await courseApi.CreateAsync(
                new CreateCourseRequest(form.Code, form.Name),
                cancellationToken);
            return result.Status switch
            {
                CourseCreationStatus.Created =>
                    TypedResults.LocalRedirect(
                        $"/courses/{result.Course!.Id}"),
                CourseCreationStatus.Duplicate =>
                    TypedResults.LocalRedirect("/courses?error=duplicate"),
                _ => TypedResults.LocalRedirect("/courses?error=invalid"),
            };
        })
    .RequireAuthorization(policy =>
        policy.RequireRole("Administrator", "Instructor"));

app.MapPost(
        "/templates/create",
        async Task<IResult> (
            [FromForm] CreateRepositoryTemplateForm form,
            AssignmentApiClient assignmentApi,
            CancellationToken cancellationToken) =>
        {
            var template = await assignmentApi.CreateTemplateAsync(
                new CreateRepositoryRequest(form.Name, form.DisplayName),
                cancellationToken);
            return TypedResults.LocalRedirect(
                template is null
                    ? "/templates?error=invalid"
                    : $"/templates?created={Uri.EscapeDataString(template.Name)}");
        })
    .RequireAuthorization(policy =>
        policy.RequireRole("Administrator", "Instructor"));

app.MapPost(
        "/courses/{courseId:guid}/assignments/create",
        async Task<IResult> (
            Guid courseId,
            [FromForm] CreateAssignmentForm form,
            AssignmentApiClient assignmentApi,
            CancellationToken cancellationToken) =>
        {
            if (!string.IsNullOrWhiteSpace(form.TemplateRepositoryId)
                && !Guid.TryParse(
                    form.TemplateRepositoryId,
                    out _))
            {
                return TypedResults.LocalRedirect(
                    $"/courses/{courseId}?tab=assignments"
                    + "&action=create-assignment&error=invalid-assignment");
            }

            if (!string.IsNullOrWhiteSpace(form.DueAt)
                && !DateTimeOffset.TryParse(
                    form.DueAt,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal,
                    out _))
            {
                return TypedResults.LocalRedirect(
                    $"/courses/{courseId}?tab=assignments"
                    + "&action=create-assignment&error=invalid-assignment");
            }

            Guid? templateId = Guid.TryParse(
                form.TemplateRepositoryId,
                out var parsedTemplateId)
                ? parsedTemplateId
                : null;
            DateTimeOffset? dueAt = DateTimeOffset.TryParse(
                form.DueAt,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out var parsedDueAt)
                ? parsedDueAt
                : null;
            int? minTeamSize = int.TryParse(form.MinTeamSize, out var parsedMinTeamSize)
                ? parsedMinTeamSize
                : null;
            int? maxTeamSize = int.TryParse(form.MaxTeamSize, out var parsedMaxTeamSize)
                ? parsedMaxTeamSize
                : null;
            var result = await assignmentApi.CreateAssignmentAsync(
                courseId,
                new CreateAssignmentRequest(
                    form.Slug,
                    form.Title,
                    form.Description,
                    templateId,
                    dueAt,
                    IsGroupAssignment: form.IsGroupAssignment,
                    MinTeamSize: minTeamSize,
                    MaxTeamSize: maxTeamSize,
                    RequireTeamSelection: form.RequireTeamSelection,
                    AllowTeamChanges: form.AllowTeamChanges),
                cancellationToken);
            return TypedResults.LocalRedirect(
                result.Status switch
                {
                    AssignmentCreationStatus.Created =>
                        $"/courses/{courseId}?tab=assignments",
                    AssignmentCreationStatus.ProvisioningFailed =>
                        $"/courses/{courseId}?tab=assignments&error=provisioning",
                    _ => $"/courses/{courseId}?tab=assignments"
                        + "&action=create-assignment&error=invalid-assignment",
                });
        })
    .RequireAuthorization(policy =>
        policy.RequireRole("Administrator", "Instructor"));

app.MapPost(
        "/courses/{courseId:guid}/assignments/{assignmentId:guid}/repositories",
        async Task<IResult> (
            Guid courseId,
            Guid assignmentId,
            [FromForm] ProvisionAssignmentRepositoriesForm _,
            AssignmentApiClient assignmentApi,
            CancellationToken cancellationToken) =>
        {
            var provisioned = await assignmentApi.ProvisionRepositoriesAsync(
                courseId,
                assignmentId,
                cancellationToken);
            return TypedResults.LocalRedirect(
                provisioned
                    ? $"/courses/{courseId}?tab=assignments"
                    : $"/courses/{courseId}?tab=assignments&error=provisioning");
        })
    .RequireAuthorization(policy =>
        policy.RequireRole("Administrator", "Instructor"));

app.MapPost(
        "/courses/{courseId:guid}/staff",
        async Task<IResult> (
            Guid courseId,
            [FromForm] AddCourseStaffForm form,
            CourseApiClient courseApi,
            CancellationToken cancellationToken) =>
        {
            var separator = form.StaffSelection.LastIndexOf(':');
            if (separator <= 0
                || !Guid.TryParse(
                    form.StaffSelection[..separator],
                    out var userId))
            {
                return TypedResults.LocalRedirect(
                    $"/courses/{courseId}?tab=staff"
                    + "&action=add-course-staff&error=invalid-staff");
            }

            var added = await courseApi.AddStaffAsync(
                courseId,
                new AddCourseStaffRequest(
                    userId,
                    form.StaffSelection[(separator + 1)..]),
                cancellationToken);
            return TypedResults.LocalRedirect(
                added
                    ? $"/courses/{courseId}?tab=staff"
                    : $"/courses/{courseId}?tab=staff"
                        + "&action=add-course-staff&error=staff-conflict");
        })
    .RequireAuthorization(policy =>
        policy.RequireRole("Administrator", "Instructor"));

app.MapPost(
        "/courses/{courseId:guid}/staff/remove",
        async Task<IResult> (
            Guid courseId,
            [FromForm] RemoveCourseStaffForm form,
            CourseApiClient courseApi,
            CancellationToken cancellationToken) =>
        {
            var removed = await courseApi.RemoveStaffAsync(
                courseId,
                form.UserId,
                cancellationToken);
            return TypedResults.LocalRedirect(
                removed
                    ? $"/courses/{courseId}?tab=staff"
                    : $"/courses/{courseId}?tab=staff&error=staff-conflict");
        })
    .RequireAuthorization(policy =>
        policy.RequireRole("Administrator", "Instructor"));

app.MapPost(
        "/courses/{courseId:guid}/roster/import",
        async Task<IResult> (
            Guid courseId,
            [FromForm] ImportCourseRosterForm form,
            CourseApiClient courseApi,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (form.RosterFile is null
                || form.RosterFile.Length == 0
                || form.RosterFile.Length > 1024 * 1024)
            {
                return TypedResults.LocalRedirect(
                    $"/courses/{courseId}?tab=students"
                    + "&action=import-students&error=invalid-roster");
            }

            string csv;
            try
            {
                await using var stream = form.RosterFile.OpenReadStream();
                using var reader = new StreamReader(
                    stream,
                    new UTF8Encoding(
                        encoderShouldEmitUTF8Identifier: false,
                        throwOnInvalidBytes: true),
                    detectEncodingFromByteOrderMarks: true);
                csv = await reader.ReadToEndAsync(cancellationToken);
            }
            catch (DecoderFallbackException)
            {
                return TypedResults.LocalRedirect(
                    $"/courses/{courseId}?tab=students"
                    + "&action=import-students&error=invalid-roster");
            }

            var result = await courseApi.ImportRosterAsync(
                courseId,
                csv,
                cancellationToken);
            if (result is null)
            {
                return TypedResults.LocalRedirect(
                    $"/courses/{courseId}?tab=students"
                    + "&action=import-students&error=invalid-roster");
            }

            var output = new StringBuilder(
                "row,email,displayName,status,userId,activationUrl,message\r\n");
            foreach (var row in result.Rows)
            {
                var activationUrl = row.UserId.HasValue
                    && !string.IsNullOrWhiteSpace(row.ActivationToken)
                    ? $"{context.Request.Scheme}://{context.Request.Host}/activate"
                        + QueryString.Create(
                            [
                                new KeyValuePair<string, string?>(
                                    "userId",
                                    row.UserId.Value.ToString()),
                                new KeyValuePair<string, string?>(
                                    "token",
                                    row.ActivationToken),
                            ])
                    : string.Empty;
                AppendCsvRow(
                    output,
                    row.RowNumber.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    row.Email,
                    row.DisplayName,
                    row.Status,
                    row.UserId?.ToString() ?? string.Empty,
                    activationUrl,
                    row.Message ?? string.Empty);
            }

            return TypedResults.File(
                Encoding.UTF8.GetBytes(output.ToString()),
                "text/csv",
                "roster-import-results.csv");
        })
    .WithMetadata(
        new RequestSizeLimitAttribute(1_100_000),
        new RequestFormLimitsAttribute
        {
            MultipartBodyLengthLimit = 1_100_000,
        })
    .RequireAuthorization(policy =>
        policy.RequireRole("Administrator", "Instructor"));

app.MapDefaultEndpoints();

app.Run();

static void AppendCsvRow(StringBuilder output, params string[] fields)
{
    output.AppendJoin(
        ',',
        fields.Select(field =>
        {
            var safeField = field.Length > 0
                && field[0] is '=' or '+' or '-' or '@' or '\t' or '\r'
                ? $"'{field}"
                : field;
            return $"\"{safeField.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        }));
    output.Append("\r\n");
}
