using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PirateForgeGitServer.Web.Components;
using PirateForgeGitServer.Web.Courses;
using PirateForgeGitServer.Web.Identity;
using PirateForgeGitServer.Web.Assignments;
using PirateForgeGitServer.Web.Repositories;

namespace PirateForgeGitServer.IntegrationTests.Courses;

public sealed class CourseWebEndpointTests
{
    [Fact]
    public async Task RepositoriesPageGroupsAssignmentsWithoutListingStudentRepositories()
    {
        var tracker = new RepositoryPageRequestTracker();
        using var factory = CreateFactory(() => new RepositoryPageApiHandler(tracker));
        using var client = CreateClient(factory);
        await LoginAsync(client);

        using var page = await client.GetAsync("/repositories");

        page.EnsureSuccessStatusCode();
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("CS101 · Existing Course", html, StringComparison.Ordinal);
        Assert.Contains("Project One", html, StringComparison.Ordinal);
        Assert.Contains(
            "href=\"/courses/11111111-1111-1111-1111-111111111112/assignments/44444444-4444-4444-4444-444444444444/repositories\"",
            html,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Project One - Active Student",
            html,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "/repositories/cs101-project-1-active",
            html,
            StringComparison.Ordinal);
        Assert.Equal(0, tracker.RepositoryListRequestCount);
    }

    [Fact]
    public async Task InstructorCanRenderAndCreateCoursesFromTheWeb()
    {
        using var factory = new WebApplicationFactory<App>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["PirateForge:Storage:RootPath"] = Path.Combine(
                                Path.GetTempPath(),
                                "pirate-forge-course-web-tests",
                                Guid.NewGuid().ToString("N")),
                        }));
                builder.ConfigureTestServices(services =>
                {
                    services.AddHttpClient<IdentityApiClient>(
                            client => client.BaseAddress =
                                new Uri("https://api.example.test"))
                        .ConfigurePrimaryHttpMessageHandler(
                            () => new CourseWebApiHandler());
                    services.AddHttpClient<CourseApiClient>(
                            client => client.BaseAddress =
                                new Uri("https://api.example.test"))
                        .ConfigurePrimaryHttpMessageHandler(
                            () => new CourseWebApiHandler());
                });
            });
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost"),
                HandleCookies = true,
            });
        await LoginAsync(client);

        using var page = await client.GetAsync("/courses");
        page.EnsureSuccessStatusCode();
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("Existing Course", html, StringComparison.Ordinal);
        Assert.Contains("Create a course", html, StringComparison.Ordinal);
        var antiforgeryToken = Regex.Match(
            html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")
            .Groups[1]
            .Value;

        using var response = await client.PostAsync(
            "/courses/create",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["Code"] = "CS202",
                    ["Name"] = "Data Structures",
                    ["__RequestVerificationToken"] = antiforgeryToken,
                }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(
            "/courses/22222222-2222-2222-2222-222222222222",
            response.Headers.Location?.OriginalString);

        using var invalidResponse = await client.PostAsync(
            "/courses/create",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["Code"] = "BAD CODE",
                    ["Name"] = "Invalid Course",
                    ["__RequestVerificationToken"] = antiforgeryToken,
                }));
        Assert.Equal(HttpStatusCode.Redirect, invalidResponse.StatusCode);
        Assert.Equal(
            "/courses?error=invalid",
            invalidResponse.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task InstructorCanViewAndImportARosterFromTheWeb()
    {
        using var factory = CreateFactory(() => new RosterWebApiHandler());
        using var client = CreateClient(factory);
        await LoginAsync(client);

        using var page = await client.GetAsync(
            "/courses/11111111-1111-1111-1111-111111111112"
            + "?tab=students&action=import-students");
        page.EnsureSuccessStatusCode();
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("Student Roster", html, StringComparison.Ordinal);
        Assert.Contains("Existing Student", html, StringComparison.Ordinal);
        var antiforgeryToken = GetAntiforgeryToken(html);

        using var content = new MultipartFormDataContent();
        content.Add(
            new StringContent(antiforgeryToken),
            "__RequestVerificationToken");
        content.Add(
            new ByteArrayContent(
                Encoding.UTF8.GetBytes(
                    "email,displayName\nnew.student@example.edu,New Student")),
            "RosterFile",
            "roster.csv");
        using var response = await client.PostAsync(
            "/courses/11111111-1111-1111-1111-111111111112/roster/import",
            content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "text/csv",
            response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            "attachment",
            response.Content.Headers.ContentDisposition?.DispositionType);
        var csv = await response.Content.ReadAsStringAsync();
        Assert.Contains("Created", csv, StringComparison.Ordinal);
        Assert.Contains("/activate?userId=", csv, StringComparison.Ordinal);
        Assert.Contains(
            "\"'=HYPERLINK(\"\"https://example.test\"\")\"",
            csv,
            StringComparison.Ordinal);

        using var invalidContent = new MultipartFormDataContent();
        invalidContent.Add(
            new StringContent(antiforgeryToken),
            "__RequestVerificationToken");
        invalidContent.Add(
            new ByteArrayContent([]),
            "RosterFile",
            "empty.csv");
        using var invalidResponse = await client.PostAsync(
            "/courses/11111111-1111-1111-1111-111111111112/roster/import",
            invalidContent);
        Assert.Equal(HttpStatusCode.Redirect, invalidResponse.StatusCode);
        Assert.Equal(
            "/courses/11111111-1111-1111-1111-111111111112"
            + "?tab=students&action=import-students&error=invalid-roster",
            invalidResponse.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task StudentCanActivateAnAccountFromTheWeb()
    {
        using var factory = CreateFactory(() => new RosterWebApiHandler());
        using var client = CreateClient(factory);
        const string userId = "33333333-3333-3333-3333-333333333333";

        using var page = await client.GetAsync(
            $"/activate?userId={userId}&token=activation-token");
        page.EnsureSuccessStatusCode();
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("Activate your account", html, StringComparison.Ordinal);
        var antiforgeryToken = GetAntiforgeryToken(html);

        using var response = await client.PostAsync(
            "/account/activate",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["UserId"] = userId,
                    ["Token"] = "activation-token",
                    ["Password"] = "Roster-Account-42!",
                    ["ConfirmPassword"] = "Roster-Account-42!",
                    ["__RequestVerificationToken"] = antiforgeryToken,
                }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(
            "/login?activated=true",
            response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task InstructorCanManageTemplatesAndAssignmentsFromTheWeb()
    {
        using var factory = CreateFactory(() => new AssignmentWebApiHandler());
        using var client = CreateClient(factory);
        await LoginAsync(client);

        using var templatesPage = await client.GetAsync("/templates");
        templatesPage.EnsureSuccessStatusCode();
        var templatesHtml = await templatesPage.Content.ReadAsStringAsync();
        Assert.Contains("Repository Templates", templatesHtml, StringComparison.Ordinal);
        Assert.Contains("Project Starter", templatesHtml, StringComparison.Ordinal);
        var templatesToken = GetAntiforgeryToken(templatesHtml);
        using var createTemplateResponse = await client.PostAsync(
            "/templates/create",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["Name"] = "new-template",
                    ["DisplayName"] = "New Template",
                    ["__RequestVerificationToken"] = templatesToken,
                }));
        Assert.Equal(HttpStatusCode.Redirect, createTemplateResponse.StatusCode);
        Assert.Equal(
            "/templates?created=new-template",
            createTemplateResponse.Headers.Location?.OriginalString);

        using var emptyTemplatePage = await client.GetAsync(
            "/repositories/new-template");
        emptyTemplatePage.EnsureSuccessStatusCode();
        Assert.Contains(
            "This repository is empty",
            await emptyTemplatePage.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        using var coursePage = await client.GetAsync(
            "/courses/11111111-1111-1111-1111-111111111112");
        coursePage.EnsureSuccessStatusCode();
        var courseHtml = await coursePage.Content.ReadAsStringAsync();
        Assert.Contains("Assignments", courseHtml, StringComparison.Ordinal);
        Assert.Contains("?tab=students", courseHtml, StringComparison.Ordinal);
        Assert.Contains("?tab=staff", courseHtml, StringComparison.Ordinal);
        Assert.Contains("?tab=activity", courseHtml, StringComparison.Ordinal);
        Assert.Contains("aria-current=\"page\"", courseHtml, StringComparison.Ordinal);
        Assert.Contains("Project One", courseHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Download activity", courseHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("course-staff-table", courseHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"Slug\"", courseHtml, StringComparison.Ordinal);
        Assert.Contains("2 of 3", courseHtml, StringComparison.Ordinal);
        Assert.Contains("Provision missing repositories", courseHtml, StringComparison.Ordinal);
        Assert.Contains("View repositories", courseHtml, StringComparison.Ordinal);

        using var createAssignmentPage = await client.GetAsync(
            "/courses/11111111-1111-1111-1111-111111111112"
            + "?tab=assignments&action=create-assignment");
        createAssignmentPage.EnsureSuccessStatusCode();
        var createAssignmentHtml =
            await createAssignmentPage.Content.ReadAsStringAsync();
        Assert.Contains("name=\"Slug\"", createAssignmentHtml, StringComparison.Ordinal);
        Assert.Contains("Project Starter", createAssignmentHtml, StringComparison.Ordinal);

        using var studentsPage = await client.GetAsync(
            "/courses/11111111-1111-1111-1111-111111111112?tab=students");
        studentsPage.EnsureSuccessStatusCode();
        var studentsHtml = await studentsPage.Content.ReadAsStringAsync();
        Assert.Contains("Student Roster", studentsHtml, StringComparison.Ordinal);
        Assert.Contains(
            "No students are enrolled in this course.",
            studentsHtml,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Project One", studentsHtml, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "name=\"RosterFile\"",
            studentsHtml,
            StringComparison.Ordinal);

        using var importStudentsPage = await client.GetAsync(
            "/courses/11111111-1111-1111-1111-111111111112"
            + "?tab=students&action=import-students");
        importStudentsPage.EnsureSuccessStatusCode();
        Assert.Contains(
            "name=\"RosterFile\"",
            await importStudentsPage.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        using var staffPage = await client.GetAsync(
            "/courses/11111111-1111-1111-1111-111111111112?tab=staff");
        staffPage.EnsureSuccessStatusCode();
        var staffHtml = await staffPage.Content.ReadAsStringAsync();
        Assert.Contains("Course staff", staffHtml, StringComparison.Ordinal);
        Assert.Contains("Course Instructor", staffHtml, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "name=\"StaffSelection\"",
            staffHtml,
            StringComparison.Ordinal);

        using var addStaffPage = await client.GetAsync(
            "/courses/11111111-1111-1111-1111-111111111112"
            + "?tab=staff&action=add-course-staff");
        addStaffPage.EnsureSuccessStatusCode();
        var addStaffHtml = await addStaffPage.Content.ReadAsStringAsync();
        Assert.Contains(
            "No unassigned Instructor or TA accounts are available.",
            addStaffHtml,
            StringComparison.Ordinal);
        var addStaffToken = GetAntiforgeryToken(addStaffHtml);
        using var invalidStaffResponse = await client.PostAsync(
            "/courses/11111111-1111-1111-1111-111111111112/staff",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["StaffSelection"] = "",
                    ["__RequestVerificationToken"] = addStaffToken,
                }));
        Assert.Equal(HttpStatusCode.Redirect, invalidStaffResponse.StatusCode);
        Assert.Equal(
            "/courses/11111111-1111-1111-1111-111111111112"
            + "?tab=staff&action=add-course-staff&error=invalid-staff",
            invalidStaffResponse.Headers.Location?.OriginalString);

        using var addStaffResponse = await client.PostAsync(
            "/courses/11111111-1111-1111-1111-111111111112/staff",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["StaffSelection"] =
                        "22222222-2222-2222-2222-222222222222:TA",
                    ["__RequestVerificationToken"] = addStaffToken,
                }));
        Assert.Equal(HttpStatusCode.Redirect, addStaffResponse.StatusCode);
        Assert.Equal(
            "/courses/11111111-1111-1111-1111-111111111112?tab=staff",
            addStaffResponse.Headers.Location?.OriginalString);

        using var removeStaffResponse = await client.PostAsync(
            "/courses/11111111-1111-1111-1111-111111111112/staff/remove",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["UserId"] = "11111111-1111-1111-1111-111111111111",
                    ["__RequestVerificationToken"] = addStaffToken,
                }));
        Assert.Equal(HttpStatusCode.Redirect, removeStaffResponse.StatusCode);
        Assert.Equal(
            "/courses/11111111-1111-1111-1111-111111111112?tab=staff",
            removeStaffResponse.Headers.Location?.OriginalString);

        using var activityPage = await client.GetAsync(
            "/courses/11111111-1111-1111-1111-111111111112?tab=activity");
        activityPage.EnsureSuccessStatusCode();
        var activityHtml = await activityPage.Content.ReadAsStringAsync();
        Assert.Contains("Download activity", activityHtml, StringComparison.Ordinal);
        Assert.Contains(
            "All assignment repositories",
            activityHtml,
            StringComparison.Ordinal);

        using var invalidTabPage = await client.GetAsync(
            "/courses/11111111-1111-1111-1111-111111111112?tab=unknown");
        invalidTabPage.EnsureSuccessStatusCode();
        var invalidTabHtml = await invalidTabPage.Content.ReadAsStringAsync();
        Assert.Contains("Project One", invalidTabHtml, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Download activity",
            invalidTabHtml,
            StringComparison.Ordinal);

        using var statusPage = await client.GetAsync(
            "/courses/11111111-1111-1111-1111-111111111112/assignments/44444444-4444-4444-4444-444444444444/status");
        statusPage.EnsureSuccessStatusCode();
        var statusHtml = await statusPage.Content.ReadAsStringAsync();
        Assert.Contains("Project One repositories", statusHtml, StringComparison.Ordinal);
        Assert.Contains("Download all repositories", statusHtml, StringComparison.Ordinal);
        Assert.Contains("Active Student", statusHtml, StringComparison.Ordinal);
        Assert.Contains("Pending Student", statusHtml, StringComparison.Ordinal);
        Assert.Contains("Not provisioned", statusHtml, StringComparison.Ordinal);
        Assert.Contains("Add starter", statusHtml, StringComparison.Ordinal);
        Assert.Contains(
            "/repositories/cs101-project-1-active",
            statusHtml,
            StringComparison.Ordinal);
        var statusToken = GetAntiforgeryToken(statusHtml);
        using var bulkDownload = await client.GetAsync(
            "/courses/11111111-1111-1111-1111-111111111112/assignments/44444444-4444-4444-4444-444444444444/download");
        bulkDownload.EnsureSuccessStatusCode();
        Assert.Equal(
            "application/zip",
            bulkDownload.Content.Headers.ContentType?.MediaType);
        var courseToken = GetAntiforgeryToken(courseHtml);
        using var provisionResponse = await client.PostAsync(
            "/courses/11111111-1111-1111-1111-111111111112/assignments/44444444-4444-4444-4444-444444444444/repositories",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = courseToken,
                }));
        Assert.Equal(HttpStatusCode.Redirect, provisionResponse.StatusCode);
        Assert.Equal(
            "/courses/11111111-1111-1111-1111-111111111112?tab=assignments",
            provisionResponse.Headers.Location?.OriginalString);

        using var createAssignmentResponse = await client.PostAsync(
            "/courses/11111111-1111-1111-1111-111111111112/assignments/create",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["Slug"] = "project-2",
                    ["Title"] = "Project Two",
                    ["Description"] = "Second project",
                    ["TemplateRepositoryId"] =
                        "22222222-2222-2222-2222-222222222222",
                    ["DueAt"] = "2026-08-12T17:00",
                    ["__RequestVerificationToken"] = courseToken,
                }));
        Assert.Equal(HttpStatusCode.Redirect, createAssignmentResponse.StatusCode);
        Assert.Equal(
            "/courses/11111111-1111-1111-1111-111111111112?tab=assignments",
            createAssignmentResponse.Headers.Location?.OriginalString);

        using var invalidAssignmentResponse = await client.PostAsync(
            "/courses/11111111-1111-1111-1111-111111111112/assignments/create",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["Slug"] = "invalid",
                    ["Title"] = "Invalid",
                    ["Description"] = "",
                    ["TemplateRepositoryId"] = "not-a-guid",
                    ["DueAt"] = "",
                    ["__RequestVerificationToken"] = courseToken,
                }));
        Assert.Equal(HttpStatusCode.Redirect, invalidAssignmentResponse.StatusCode);
        Assert.Equal(
            "/courses/11111111-1111-1111-1111-111111111112"
            + "?tab=assignments&action=create-assignment&error=invalid-assignment",
            invalidAssignmentResponse.Headers.Location?.OriginalString);

        using var provisioningFailureResponse = await client.PostAsync(
            "/courses/11111111-1111-1111-1111-111111111112/assignments/create",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["Slug"] = "provisioning-failure",
                    ["Title"] = "Provisioning Failure",
                    ["Description"] = "",
                    ["TemplateRepositoryId"] = "",
                    ["DueAt"] = "",
                    ["__RequestVerificationToken"] = courseToken,
                }));
        Assert.Equal(
            "/courses/11111111-1111-1111-1111-111111111112"
            + "?tab=assignments&error=provisioning",
            provisioningFailureResponse.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task CourseTabsHideManagementSectionsFromNonManagers()
    {
        using var factory = CreateFactory(
            () => new AssignmentWebApiHandler(includeCurrentInstructorAsStaff: false));
        using var client = CreateClient(factory);
        await LoginAsync(client);

        using var page = await client.GetAsync(
            "/courses/11111111-1111-1111-1111-111111111112?tab=students");
        page.EnsureSuccessStatusCode();
        var html = await page.Content.ReadAsStringAsync();

        Assert.Contains("Project One", html, StringComparison.Ordinal);
        Assert.Contains("?tab=staff", html, StringComparison.Ordinal);
        Assert.DoesNotContain("?tab=students", html, StringComparison.Ordinal);
        Assert.DoesNotContain("?tab=activity", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Student Roster", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Download activity", html, StringComparison.Ordinal);

        using var activityPage = await client.GetAsync(
            "/courses/11111111-1111-1111-1111-111111111112?tab=activity");
        activityPage.EnsureSuccessStatusCode();
        var activityHtml = await activityPage.Content.ReadAsStringAsync();
        Assert.Contains("Project One", activityHtml, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Download activity",
            activityHtml,
            StringComparison.Ordinal);

        using var assignmentActionPage = await client.GetAsync(
            "/courses/11111111-1111-1111-1111-111111111112"
            + "?tab=assignments&action=create-assignment");
        assignmentActionPage.EnsureSuccessStatusCode();
        Assert.DoesNotContain(
            "name=\"Slug\"",
            await assignmentActionPage.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        using var staffActionPage = await client.GetAsync(
            "/courses/11111111-1111-1111-1111-111111111112"
            + "?tab=staff&action=add-course-staff");
        staffActionPage.EnsureSuccessStatusCode();
        Assert.DoesNotContain(
            "Add course staff",
            await staffActionPage.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AssignmentDownloadPreservesApiAuthorizationResponse()
    {
        using var factory = CreateFactory(
            () => new AssignmentWebApiHandler(forbidArchive: true));
        using var client = CreateClient(factory);
        await LoginAsync(client);

        using var response = await client.GetAsync(
            "/courses/11111111-1111-1111-1111-111111111112/assignments/44444444-4444-4444-4444-444444444444/download");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DownloadLinksUseNativeNavigationAndReturnZipAttachments()
    {
        using var factory = CreateFactory(() => new AssignmentWebApiHandler());
        using var client = CreateClient(factory);
        await LoginAsync(client);

        using var repositoryPage = await client.GetAsync(
            "/repositories/cs101-project-1-active");
        repositoryPage.EnsureSuccessStatusCode();
        var repositoryHtml = await repositoryPage.Content.ReadAsStringAsync();
        Assert.Contains(
            "href=\"/repositories/cs101-project-1-active/download?branch=main\"",
            repositoryHtml,
            StringComparison.Ordinal);
        Assert.Contains(
            "data-enhance-nav=\"false\"",
            repositoryHtml,
            StringComparison.Ordinal);

        using var repositoryDownload = await client.GetAsync(
            "/repositories/cs101-project-1-active/download?branch=main");
        repositoryDownload.EnsureSuccessStatusCode();
        AssertZipAttachment(repositoryDownload, "cs101-project-1-active-main.zip");
        Assert.Equal(
            "repository-archive",
            await repositoryDownload.Content.ReadAsStringAsync());

        using var assignmentPage = await client.GetAsync(
            "/courses/11111111-1111-1111-1111-111111111112/assignments/44444444-4444-4444-4444-444444444444/status");
        assignmentPage.EnsureSuccessStatusCode();
        var assignmentHtml = await assignmentPage.Content.ReadAsStringAsync();
        Assert.Contains(
            "href=\"/courses/11111111-1111-1111-1111-111111111112/assignments/44444444-4444-4444-4444-444444444444/download\"",
            assignmentHtml,
            StringComparison.Ordinal);
        Assert.Contains(
            "data-enhance-nav=\"false\"",
            assignmentHtml,
            StringComparison.Ordinal);

        using var assignmentDownload = await client.GetAsync(
            "/courses/11111111-1111-1111-1111-111111111112/assignments/44444444-4444-4444-4444-444444444444/download");
        assignmentDownload.EnsureSuccessStatusCode();
        AssertZipAttachment(
            assignmentDownload,
            "assignment-44444444-4444-4444-4444-444444444444-repositories.zip");
        Assert.Equal(
            "bulk-archive",
            await assignmentDownload.Content.ReadAsStringAsync());
    }

    private static WebApplicationFactory<App> CreateFactory(
        Func<HttpMessageHandler> handlerFactory) =>
        new WebApplicationFactory<App>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["PirateForge:Storage:RootPath"] = Path.Combine(
                                Path.GetTempPath(),
                                "pirate-forge-course-web-tests",
                                Guid.NewGuid().ToString("N")),
                        }));
                builder.ConfigureTestServices(services =>
                {
                    services.AddHttpClient<IdentityApiClient>(
                            client => client.BaseAddress =
                                new Uri("https://api.example.test"))
                        .ConfigurePrimaryHttpMessageHandler(handlerFactory);
                    services.AddHttpClient<CourseApiClient>(
                            client => client.BaseAddress =
                                new Uri("https://api.example.test"))
                        .ConfigurePrimaryHttpMessageHandler(handlerFactory);
                    services.AddHttpClient<AssignmentApiClient>(
                            client => client.BaseAddress =
                                new Uri("https://api.example.test"))
                        .ConfigurePrimaryHttpMessageHandler(handlerFactory);
                    services.AddHttpClient<RepositoryApiClient>(
                            client => client.BaseAddress =
                                new Uri("https://api.example.test"))
                        .ConfigurePrimaryHttpMessageHandler(handlerFactory);
                });
            });

    private static HttpClient CreateClient(WebApplicationFactory<App> factory) =>
        factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost"),
                HandleCookies = true,
            });

    private static string GetAntiforgeryToken(string html) =>
        Regex.Match(
            html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")
        .Groups[1]
        .Value;

    private static void AssertZipAttachment(
        HttpResponseMessage response,
        string expectedFileName)
    {
        Assert.Equal(
            "application/zip",
            response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            "attachment",
            response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Contains(
            expectedFileName,
            response.Content.Headers.ContentDisposition?.ToString()
                ?? string.Empty,
            StringComparison.Ordinal);
    }

    private static async Task LoginAsync(HttpClient client)
    {
        using var loginPage = await client.GetAsync("/login");
        var html = await loginPage.Content.ReadAsStringAsync();
        var antiforgeryToken = Regex.Match(
            html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")
            .Groups[1]
            .Value;
        using var response = await client.PostAsync(
            "/account/login",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["Email"] = "instructor@example.edu",
                    ["Password"] = "Course-Account-42!",
                    ["__RequestVerificationToken"] = antiforgeryToken,
                }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private sealed class CourseWebApiHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath;
            var requestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            var json = (request.Method.Method, path) switch
            {
                ("POST", "/api/auth/login") =>
                    """
                    {
                      "tokenType": "Bearer",
                      "accessToken": "access-token",
                      "expiresIn": 3600,
                      "refreshToken": "refresh-token"
                    }
                    """,
                ("GET", "/api/auth/me") =>
                    """
                    {
                      "id": "11111111-1111-1111-1111-111111111111",
                      "email": "instructor@example.edu",
                      "displayName": "Course Instructor",
                      "roles": ["Instructor"]
                    }
                    """,
                ("GET", "/api/courses") =>
                    """
                    [{
                      "id": "11111111-1111-1111-1111-111111111112",
                      "code": "CS101",
                      "name": "Existing Course",
                      "createdAt": "2026-08-05T00:00:00Z",
                      "staff": []
                    }]
                    """,
                ("POST", "/api/courses") =>
                    """
                    {
                      "id": "22222222-2222-2222-2222-222222222222",
                      "code": "CS202",
                      "name": "Data Structures",
                      "createdAt": "2026-08-05T00:00:00Z",
                      "staff": []
                    }
                    """,
                _ => throw new InvalidOperationException(
                    $"Unexpected API request: {request.Method} {request.RequestUri}"),
            };

            return new HttpResponseMessage(
                    request.Method == HttpMethod.Post
                    && path == "/api/courses"
                        ? requestBody.Contains(
                            "BAD CODE",
                            StringComparison.Ordinal)
                            ? HttpStatusCode.BadRequest
                            : HttpStatusCode.Created
                        : HttpStatusCode.OK)
            {
                Content = new StringContent(
                        json,
                        Encoding.UTF8,
                        "application/json"),
            };
        }
    }

    private sealed class RepositoryPageRequestTracker
    {
        public int RepositoryListRequestCount { get; set; }
    }

    private sealed class RepositoryPageApiHandler(
        RepositoryPageRequestTracker tracker)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath;
            var json = (request.Method.Method, path) switch
            {
                ("POST", "/api/auth/login") =>
                    """
                    {
                      "tokenType": "Bearer",
                      "accessToken": "access-token",
                      "expiresIn": 3600,
                      "refreshToken": "refresh-token"
                    }
                    """,
                ("GET", "/api/auth/me") =>
                    """
                    {
                      "id": "11111111-1111-1111-1111-111111111111",
                      "email": "instructor@example.edu",
                      "displayName": "Course Instructor",
                      "roles": ["Instructor"]
                    }
                    """,
                ("GET", "/api/courses") =>
                    """
                    [{
                      "id": "11111111-1111-1111-1111-111111111112",
                      "code": "CS101",
                      "name": "Existing Course",
                      "createdAt": "2026-08-05T00:00:00Z",
                      "staff": []
                    }]
                    """,
                ("GET", "/api/courses/11111111-1111-1111-1111-111111111112/assignments") =>
                    """
                    [{
                      "id": "44444444-4444-4444-4444-444444444444",
                      "courseId": "11111111-1111-1111-1111-111111111112",
                      "slug": "project-1",
                      "title": "Project One",
                      "description": "First project",
                      "template": null,
                      "dueAt": null,
                      "createdByUserId": "11111111-1111-1111-1111-111111111111",
                      "createdAt": "2026-08-05T00:00:00Z",
                      "studentCount": 1,
                      "provisionedRepositoryCount": 1
                    }]
                    """,
                ("GET", "/api/repositories") => TrackRepositoryListRequest(),
                _ => throw new InvalidOperationException(
                    $"Unexpected API request: {request.Method} {request.RequestUri}"),
            };

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        json,
                        Encoding.UTF8,
                        "application/json"),
                });
        }

        private string TrackRepositoryListRequest()
        {
            tracker.RepositoryListRequestCount++;
            return
                """
                [{
                  "id": "66666666-6666-6666-6666-666666666666",
                  "name": "cs101-project-1-active",
                  "displayName": "Project One - Active Student",
                  "ownerUserId": "33333333-3333-3333-3333-333333333333",
                  "createdAt": "2026-08-05T00:00:00Z"
                }]
                """;
        }
    }

    private sealed class RosterWebApiHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath;
            var (status, json) = (request.Method.Method, path) switch
            {
                ("POST", "/api/auth/login") => (
                    HttpStatusCode.OK,
                    """
                    {
                      "tokenType": "Bearer",
                      "accessToken": "access-token",
                      "expiresIn": 3600,
                      "refreshToken": "refresh-token"
                    }
                    """),
                ("GET", "/api/auth/me") => (
                    HttpStatusCode.OK,
                    """
                    {
                      "id": "11111111-1111-1111-1111-111111111111",
                      "email": "instructor@example.edu",
                      "displayName": "Course Instructor",
                      "roles": ["Instructor"]
                    }
                    """),
                ("GET", "/api/courses/11111111-1111-1111-1111-111111111112") => (
                    HttpStatusCode.OK,
                    """
                    {
                      "id": "11111111-1111-1111-1111-111111111112",
                      "code": "CS101",
                      "name": "Existing Course",
                      "createdAt": "2026-08-05T00:00:00Z",
                      "staff": [{
                        "userId": "11111111-1111-1111-1111-111111111111",
                        "email": "instructor@example.edu",
                        "displayName": "Course Instructor",
                        "role": "Instructor",
                        "addedAt": "2026-08-05T00:00:00Z"
                      }]
                    }
                    """),
                ("GET", "/api/courses/staff-candidates") => (
                    HttpStatusCode.OK,
                    "[]"),
                ("GET", "/api/courses/11111111-1111-1111-1111-111111111112/audit-events") => (
                    HttpStatusCode.OK,
                    """
                    [{
                      "id": "77777777-7777-7777-7777-777777777777",
                      "courseId": "11111111-1111-1111-1111-111111111112",
                      "assignmentId": "44444444-4444-4444-4444-444444444444",
                      "repositoryId": null,
                      "repositoryName": null,
                      "actorUserId": "11111111-1111-1111-1111-111111111111",
                      "actorEmail": "instructor@example.edu",
                      "actorDisplayName": "Course Instructor",
                      "action": "AssignmentArchiveDownloaded",
                      "createdAt": "2026-08-05T00:00:00Z"
                    }]
                    """),
                ("GET", "/api/courses/11111111-1111-1111-1111-111111111112/students") => (
                    HttpStatusCode.OK,
                    """
                    [{
                      "userId": "33333333-3333-3333-3333-333333333333",
                      "email": "student@example.edu",
                      "displayName": "Existing Student",
                      "enrolledAt": "2026-08-05T00:00:00Z"
                    }]
                    """),
                ("GET", "/api/courses/11111111-1111-1111-1111-111111111112/assignments") => (
                    HttpStatusCode.OK,
                    "[]"),
                ("GET", "/api/repository-templates") => (
                    HttpStatusCode.OK,
                    "[]"),
                ("POST", "/api/courses/11111111-1111-1111-1111-111111111112/roster/import") => (
                    HttpStatusCode.OK,
                    """
                    {
                      "rows": [{
                        "rowNumber": 2,
                        "email": "new.student@example.edu",
                        "displayName": "=HYPERLINK(\"https://example.test\")",
                        "status": "Created",
                        "userId": "44444444-4444-4444-4444-444444444444",
                        "activationToken": "new-activation-token",
                        "message": null
                      }]
                    }
                    """),
                ("POST", "/api/auth/activate") => (
                    HttpStatusCode.NoContent,
                    string.Empty),
                _ => throw new InvalidOperationException(
                    $"Unexpected API request: {request.Method} {request.RequestUri}"),
            };

            return Task.FromResult(
                new HttpResponseMessage(status)
                {
                    Content = new StringContent(
                        json,
                        Encoding.UTF8,
                        "application/json"),
                });
        }
    }

    private sealed class AssignmentWebApiHandler(
        bool forbidArchive = false,
        bool includeCurrentInstructorAsStaff = true)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath;
            var (status, json) = (request.Method.Method, path) switch
            {
                ("POST", "/api/auth/login") => (
                    HttpStatusCode.OK,
                    """
                    {
                      "tokenType": "Bearer",
                      "accessToken": "access-token",
                      "expiresIn": 3600,
                      "refreshToken": "refresh-token"
                    }
                    """),
                ("GET", "/api/auth/me") => (
                    HttpStatusCode.OK,
                    """
                    {
                      "id": "11111111-1111-1111-1111-111111111111",
                      "email": "instructor@example.edu",
                      "displayName": "Course Instructor",
                      "roles": ["Instructor"]
                    }
                    """),
                ("GET", "/api/repository-templates") => (
                    HttpStatusCode.OK,
                    """
                    [{
                      "id": "22222222-2222-2222-2222-222222222222",
                      "name": "project-starter",
                      "displayName": "Project Starter",
                      "ownerUserId": "11111111-1111-1111-1111-111111111111",
                      "createdAt": "2026-08-05T00:00:00Z",
                      "purpose": "Template"
                    }]
                    """),
                ("GET", "/api/courses/11111111-1111-1111-1111-111111111112/assignments/44444444-4444-4444-4444-444444444444/status") => (
                    HttpStatusCode.OK,
                    """
                    {
                     "assignmentId": "44444444-4444-4444-4444-444444444444",
                     "courseId": "11111111-1111-1111-1111-111111111112",
                     "slug": "project-1",
                     "title": "Project One",
                     "totalStudents": 2,
                     "provisionedRepositories": 1,
                     "students": [{
                       "studentUserId": "33333333-3333-3333-3333-333333333333",
                       "studentEmail": "active@example.edu",
                       "studentDisplayName": "Active Student",
                       "state": "Active",
                       "repositoryName": "cs101-project-1-active",
                       "repositoryDisplayName": "Project One - Active Student",
                       "branch": "main",
                       "latestCommit": {
                         "id": "0123456789012345678901234567890123456789",
                         "subject": "Add starter",
                         "authorName": "Template Author",
                         "authorEmail": "template@example.edu",
                         "authoredAt": "2026-08-05T00:00:00Z"
                       }
                     }, {
                       "studentUserId": "55555555-5555-5555-5555-555555555555",
                       "studentEmail": "pending@example.edu",
                       "studentDisplayName": "Pending Student",
                       "state": "NotProvisioned",
                       "repositoryName": null,
                       "repositoryDisplayName": null,
                       "branch": null,
                       "latestCommit": null
                     }]
                    }
                    """),
                ("GET", "/api/courses/11111111-1111-1111-1111-111111111112/assignments/44444444-4444-4444-4444-444444444444/archive") => (
                    forbidArchive ? HttpStatusCode.Forbidden : HttpStatusCode.OK,
                    "bulk-archive"),
                ("POST", "/api/repository-templates") => (
                    HttpStatusCode.Created,
                    """
                    {
                      "id": "33333333-3333-3333-3333-333333333333",
                      "name": "new-template",
                      "displayName": "New Template",
                      "ownerUserId": "11111111-1111-1111-1111-111111111111",
                      "createdAt": "2026-08-05T00:00:00Z",
                      "purpose": "Template"
                    }
                    """),
                ("GET", "/api/repositories/new-template/branches") => (
                    HttpStatusCode.OK,
                    "[]"),
                ("GET", "/api/repositories/cs101-project-1-active/branches") => (
                    HttpStatusCode.OK,
                    """
                    [{
                      "name": "main",
                      "commitId": "0123456789012345678901234567890123456789",
                      "committedAt": "2026-08-05T00:00:00Z"
                    }]
                    """),
                ("GET", "/api/repositories/cs101-project-1-active/tree") => (
                    HttpStatusCode.OK,
                    """
                    [{
                      "name": "README.md",
                      "path": "README.md",
                      "kind": "File",
                      "size": 18
                    }]
                    """),
                ("GET", "/api/repositories/cs101-project-1-active/commits") => (
                    HttpStatusCode.OK,
                    """
                    [{
                      "id": "0123456789012345678901234567890123456789",
                      "subject": "Add starter",
                      "authorName": "Template Author",
                      "authorEmail": "template@example.edu",
                      "authoredAt": "2026-08-05T00:00:00Z"
                    }]
                    """),
                ("GET", "/api/repositories/cs101-project-1-active/archive") => (
                    HttpStatusCode.OK,
                    "repository-archive"),
                ("GET", "/api/courses/11111111-1111-1111-1111-111111111112") => (
                    HttpStatusCode.OK,
                    includeCurrentInstructorAsStaff
                    ? """
                    {
                      "id": "11111111-1111-1111-1111-111111111112",
                      "code": "CS101",
                      "name": "Existing Course",
                      "createdAt": "2026-08-05T00:00:00Z",
                      "staff": [{
                        "userId": "11111111-1111-1111-1111-111111111111",
                        "email": "instructor@example.edu",
                        "displayName": "Course Instructor",
                        "role": "Instructor",
                        "addedAt": "2026-08-05T00:00:00Z"
                      }]
                    }
                    """
                    : """
                    {
                      "id": "11111111-1111-1111-1111-111111111112",
                      "code": "CS101",
                      "name": "Existing Course",
                      "createdAt": "2026-08-05T00:00:00Z",
                      "staff": []
                    }
                    """),
                ("GET", "/api/courses/staff-candidates") => (
                    HttpStatusCode.OK,
                    "[]"),
                ("POST", "/api/courses/11111111-1111-1111-1111-111111111112/staff") => (
                    HttpStatusCode.NoContent,
                    string.Empty),
                ("DELETE", "/api/courses/11111111-1111-1111-1111-111111111112/staff/11111111-1111-1111-1111-111111111111") => (
                    HttpStatusCode.NoContent,
                    string.Empty),
                ("GET", "/api/courses/11111111-1111-1111-1111-111111111112/audit-events") => (
                    HttpStatusCode.OK,
                    """
                    [{
                      "id": "77777777-7777-7777-7777-777777777777",
                      "courseId": "11111111-1111-1111-1111-111111111112",
                      "assignmentId": "44444444-4444-4444-4444-444444444444",
                      "repositoryId": null,
                      "repositoryName": null,
                      "actorUserId": "11111111-1111-1111-1111-111111111111",
                      "actorEmail": "instructor@example.edu",
                      "actorDisplayName": "Course Instructor",
                      "action": "AssignmentArchiveDownloaded",
                      "createdAt": "2026-08-05T00:00:00Z"
                    }]
                    """),
                ("GET", "/api/courses/11111111-1111-1111-1111-111111111112/students") => (
                    HttpStatusCode.OK,
                    "[]"),
                ("GET", "/api/courses/11111111-1111-1111-1111-111111111112/assignments") => (
                    HttpStatusCode.OK,
                    """
                    [{
                      "id": "44444444-4444-4444-4444-444444444444",
                      "courseId": "11111111-1111-1111-1111-111111111112",
                      "slug": "project-1",
                      "title": "Project One",
                      "description": "First project",
                      "template": {
                        "id": "22222222-2222-2222-2222-222222222222",
                        "name": "project-starter",
                        "displayName": "Project Starter"
                      },
                      "dueAt": "2026-08-10T17:00:00Z",
                      "createdByUserId": "11111111-1111-1111-1111-111111111111",
                      "createdAt": "2026-08-05T00:00:00Z",
                      "studentCount": 3,
                      "provisionedRepositoryCount": 2
                    }]
                    """),
                ("POST", "/api/courses/11111111-1111-1111-1111-111111111112/assignments/44444444-4444-4444-4444-444444444444/repositories") => (
                    HttpStatusCode.OK,
                    """
                    {
                     "assignmentId": "44444444-4444-4444-4444-444444444444",
                     "totalStudents": 3,
                     "createdRepositories": 1,
                     "existingRepositories": 2,
                     "repositories": []
                    }
                    """),
                ("POST", "/api/courses/11111111-1111-1111-1111-111111111112/assignments") => (
                    request.Content?.ReadAsStringAsync(cancellationToken)
                        .GetAwaiter()
                        .GetResult()
                        .Contains(
                            "provisioning-failure",
                            StringComparison.Ordinal) is true
                        ? HttpStatusCode.InternalServerError
                        : HttpStatusCode.Created,
                    """
                    {
                      "id": "55555555-5555-5555-5555-555555555555",
                      "courseId": "11111111-1111-1111-1111-111111111112",
                      "slug": "project-2",
                      "title": "Project Two",
                      "description": "Second project",
                      "template": null,
                      "dueAt": "2026-08-12T17:00:00Z",
                      "createdByUserId": "11111111-1111-1111-1111-111111111111",
                      "createdAt": "2026-08-05T00:00:00Z"
                    }
                    """),
                _ => throw new InvalidOperationException(
                    $"Unexpected API request: {request.Method} {request.RequestUri}"),
            };

            return Task.FromResult(
                new HttpResponseMessage(status)
                {
                    Content = new StringContent(
                        json,
                        Encoding.UTF8,
                        "application/json"),
                });
        }
    }
}
