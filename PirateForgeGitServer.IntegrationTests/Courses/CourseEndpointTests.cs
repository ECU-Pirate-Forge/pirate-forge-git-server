using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IO.Compression;
using System.Text.Json;

namespace PirateForgeGitServer.IntegrationTests.Courses;

public sealed class CourseEndpointTests(ApiFactory factory)
    : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task AdministratorCanCreateAndListACourse()
    {
        using var client = factory.CreateClient();
        await AuthenticateAsync(
            client,
            ApiFactory.AdministratorEmail,
            ApiFactory.AdministratorPassword);
        var code = $"ADM{Guid.NewGuid():N}"[..20];

        using var createResponse = await client.PostAsJsonAsync(
            "/api/courses",
            new { Code = code, Name = "Administrative Course" });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code.ToUpperInvariant(), created.GetProperty("code").GetString());
        var staff = Assert.Single(
            created.GetProperty("staff").EnumerateArray());
        Assert.Equal("Instructor", staff.GetProperty("role").GetString());

        var courses = await client.GetFromJsonAsync<JsonElement>("/api/courses");
        Assert.Contains(
            courses.EnumerateArray(),
            course => course.GetProperty("id").GetGuid()
                == created.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task InstructorCanAddATeachingAssistantWhoCanViewTheCourse()
    {
        using var client = factory.CreateClient();
        var instructor = await CreateActivatedUserAsync(
            client,
            "Instructor",
            "Course Instructor");
        var teachingAssistant = await CreateActivatedUserAsync(
            client,
            "TA",
            "Course TA");
        await AuthenticateAsync(client, instructor.Email, instructor.Password);

        var candidates = await client.GetFromJsonAsync<JsonElement>(
            "/api/courses/staff-candidates");
        Assert.Contains(
            candidates.EnumerateArray(),
            candidate => candidate.GetProperty("userId").GetGuid()
                == teachingAssistant.Id
                && candidate.GetProperty("role").GetString() == "TA");

        using var createResponse = await client.PostAsJsonAsync(
            "/api/courses",
            new
            {
                Code = $"INS{Guid.NewGuid():N}"[..20],
                Name = "Instructor Course",
            });
        createResponse.EnsureSuccessStatusCode();
        var course = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var courseId = course.GetProperty("id").GetGuid();

        using var addResponse = await client.PostAsJsonAsync(
            $"/api/courses/{courseId}/staff",
            new { UserId = teachingAssistant.Id, Role = "TA" });
        Assert.Equal(HttpStatusCode.Created, addResponse.StatusCode);

        await AuthenticateAsync(
            client,
            teachingAssistant.Email,
            teachingAssistant.Password);
        using var detailResponse = await client.GetAsync(
            $"/api/courses/{courseId}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
    }

    [Fact]
    public async Task StudentCannotCreateCourses()
    {
        using var client = factory.CreateClient();
        var student = await CreateActivatedUserAsync(
            client,
            "Student",
            "Course Student");
        await AuthenticateAsync(client, student.Email, student.Password);

        using var response = await client.PostAsJsonAsync(
            "/api/courses",
            new { Code = "FORBIDDEN", Name = "Forbidden Course" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UnassignedInstructorCannotViewAnotherInstructorsCourse()
    {
        using var client = factory.CreateClient();
        var owner = await CreateActivatedUserAsync(
            client,
            "Instructor",
            "Course Owner");
        var unassigned = await CreateActivatedUserAsync(
            client,
            "Instructor",
            "Unassigned Instructor");
        await AuthenticateAsync(client, owner.Email, owner.Password);
        using var createResponse = await client.PostAsJsonAsync(
            "/api/courses",
            new
            {
                Code = $"ISO{Guid.NewGuid():N}"[..20],
                Name = "Isolated Course",
            });
        createResponse.EnsureSuccessStatusCode();
        var course = await createResponse.Content.ReadFromJsonAsync<JsonElement>();

        await AuthenticateAsync(client, unassigned.Email, unassigned.Password);
        using var detailResponse = await client.GetAsync(
            $"/api/courses/{course.GetProperty("id").GetGuid()}");
        var courses = await client.GetFromJsonAsync<JsonElement>("/api/courses");

        Assert.Equal(HttpStatusCode.NotFound, detailResponse.StatusCode);
        Assert.DoesNotContain(
            courses.EnumerateArray(),
            item => item.GetProperty("id").GetGuid()
                == course.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task InstructorCanImportStudentsAndActivateANewAccount()
    {
        using var client = factory.CreateClient();
        var instructor = await CreateActivatedUserAsync(
            client,
            "Instructor",
            "Roster Instructor");
        await AuthenticateAsync(client, instructor.Email, instructor.Password);
        var courseId = await CreateCourseAsync(client);
        var firstEmail = $"student-{Guid.NewGuid():N}@example.edu";
        var secondEmail = $"student-{Guid.NewGuid():N}@example.edu";

        using var importResponse = await client.PostAsJsonAsync(
            $"/api/courses/{courseId}/roster/import",
            new
            {
                Csv = $"email,displayName\n{firstEmail},First Student\n{secondEmail},Second Student",
            });

        Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
        var import = await importResponse.Content.ReadFromJsonAsync<JsonElement>();
        var rows = import.GetProperty("rows").EnumerateArray().ToArray();
        Assert.All(
            rows,
            row => Assert.Equal("Created", row.GetProperty("status").GetString()));
        var first = rows[0];
        Assert.False(string.IsNullOrWhiteSpace(
            first.GetProperty("activationToken").GetString()));

        using var rosterResponse = await client.GetAsync(
            $"/api/courses/{courseId}/students");
        rosterResponse.EnsureSuccessStatusCode();
        var roster = await rosterResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, roster.GetArrayLength());

        client.DefaultRequestHeaders.Authorization = null;
        using var activateResponse = await client.PostAsJsonAsync(
            "/api/auth/activate",
            new
            {
                UserId = first.GetProperty("userId").GetGuid(),
                Token = first.GetProperty("activationToken").GetString(),
                Password = "Roster-Account-42!",
            });
        Assert.Equal(HttpStatusCode.NoContent, activateResponse.StatusCode);

        await AuthenticateAsync(client, instructor.Email, instructor.Password);
        var course = await client.GetFromJsonAsync<JsonElement>(
            $"/api/courses/{courseId}");
        Assert.DoesNotContain(
            course.GetProperty("staff").EnumerateArray(),
            staff => staff.GetProperty("role").GetString() == "Student");
    }

    [Fact]
    public async Task RosterImportReusesStudentsAndReportsRoleConflicts()
    {
        using var client = factory.CreateClient();
        var instructor = await CreateActivatedUserAsync(
            client,
            "Instructor",
            "Import Owner");
        var existingStudent = await CreateActivatedUserAsync(
            client,
            "Student",
            "Existing Student");
        var conflictingInstructor = await CreateActivatedUserAsync(
            client,
            "Instructor",
            "Conflicting Instructor");
        await AuthenticateAsync(client, instructor.Email, instructor.Password);
        var courseId = await CreateCourseAsync(client);
        var csv = $"email,displayName\n{existingStudent.Email},Existing Student\n{conflictingInstructor.Email},Wrong Role";

        using var firstResponse = await client.PostAsJsonAsync(
            $"/api/courses/{courseId}/roster/import",
            new { Csv = csv });
        firstResponse.EnsureSuccessStatusCode();
        var first = await firstResponse.Content.ReadFromJsonAsync<JsonElement>();
        var firstRows = first.GetProperty("rows").EnumerateArray().ToArray();

        Assert.Equal("Enrolled", firstRows[0].GetProperty("status").GetString());
        Assert.Equal(existingStudent.Id, firstRows[0].GetProperty("userId").GetGuid());
        Assert.Equal(
            "RoleConflict",
            firstRows[1].GetProperty("status").GetString());

        using var secondResponse = await client.PostAsJsonAsync(
            $"/api/courses/{courseId}/roster/import",
            new { Csv = csv });
        secondResponse.EnsureSuccessStatusCode();
        var second = await secondResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            "AlreadyEnrolled",
            second.GetProperty("rows")[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task StudentsAndTeachingAssistantsCannotAccessTheRoster()
    {
        using var client = factory.CreateClient();
        var instructor = await CreateActivatedUserAsync(
            client,
            "Instructor",
            "Roster Owner");
        var teachingAssistant = await CreateActivatedUserAsync(
            client,
            "TA",
            "Roster TA");
        var student = await CreateActivatedUserAsync(
            client,
            "Student",
            "Roster Student");
        await AuthenticateAsync(client, instructor.Email, instructor.Password);
        var courseId = await CreateCourseAsync(client);
        using var addStaffResponse = await client.PostAsJsonAsync(
            $"/api/courses/{courseId}/staff",
            new { UserId = teachingAssistant.Id, Role = "TA" });
        addStaffResponse.EnsureSuccessStatusCode();
        using var importResponse = await client.PostAsJsonAsync(
            $"/api/courses/{courseId}/roster/import",
            new
            {
                Csv = $"email,displayName\n{student.Email},Roster Student",
            });
        importResponse.EnsureSuccessStatusCode();

        foreach (var account in new[] { teachingAssistant, student })
        {
            await AuthenticateAsync(client, account.Email, account.Password);
            using var listResponse = await client.GetAsync(
                $"/api/courses/{courseId}/students");
            using var deniedImportResponse = await client.PostAsJsonAsync(
                $"/api/courses/{courseId}/roster/import",
                new { Csv = "email,displayName" });

            Assert.Equal(HttpStatusCode.Forbidden, listResponse.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, deniedImportResponse.StatusCode);
        }
    }

    [Fact]
    public async Task RosterImportReturnsRowErrorsAndRejectsMissingCsv()
    {
        using var client = factory.CreateClient();
        var instructor = await CreateActivatedUserAsync(
            client,
            "Instructor",
            "Validation Instructor");
        await AuthenticateAsync(client, instructor.Email, instructor.Password);
        var courseId = await CreateCourseAsync(client);
        var validEmail = $"valid-{Guid.NewGuid():N}@example.edu";

        using var importResponse = await client.PostAsJsonAsync(
            $"/api/courses/{courseId}/roster/import",
            new
            {
                Csv = $"email,displayName\nmalformed\n{validEmail},Valid Student",
            });
        importResponse.EnsureSuccessStatusCode();
        var import = await importResponse.Content.ReadFromJsonAsync<JsonElement>();
        var rows = import.GetProperty("rows").EnumerateArray().ToArray();
        Assert.Equal("Invalid", rows[0].GetProperty("status").GetString());
        Assert.Equal("Created", rows[1].GetProperty("status").GetString());

        using var missingResponse = await client.PostAsJsonAsync(
            $"/api/courses/{courseId}/roster/import",
            new { Csv = (string?)null });
        Assert.Equal(HttpStatusCode.BadRequest, missingResponse.StatusCode);
    }

    [Fact]
    public async Task InstructorCanCreateATemplateBackedAssignment()
    {
        using var client = factory.CreateClient();
        var instructor = await CreateActivatedUserAsync(
            client,
            "Instructor",
            "Assignment Instructor");
        await AuthenticateAsync(client, instructor.Email, instructor.Password);
        var courseId = await CreateCourseAsync(client);
        var templateName = $"template-{Guid.NewGuid():N}";

        using var templateResponse = await client.PostAsJsonAsync(
            "/api/repository-templates",
            new
            {
                Name = templateName,
                DisplayName = "Project Starter",
            });
        Assert.Equal(HttpStatusCode.Created, templateResponse.StatusCode);
        var template = await templateResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Template", template.GetProperty("purpose").GetString());
        Assert.True(Directory.Exists(Path.Combine(
            factory.StorageRoot,
            "repositories",
            $"{templateName}.git")));

        var dueAt = DateTimeOffset.UtcNow.AddDays(7);
        using var assignmentResponse = await client.PostAsJsonAsync(
            $"/api/courses/{courseId}/assignments",
            new
            {
                Slug = "project-1",
                Title = "Project One",
                Description = "Build the first project.",
                TemplateRepositoryId = template.GetProperty("id").GetGuid(),
                DueAt = dueAt,
            });
        Assert.Equal(HttpStatusCode.Created, assignmentResponse.StatusCode);
        var assignment =
            await assignmentResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("project-1", assignment.GetProperty("slug").GetString());
        Assert.Equal(
            templateName,
            assignment.GetProperty("template").GetProperty("name").GetString());

        var assignments = await client.GetFromJsonAsync<JsonElement>(
            $"/api/courses/{courseId}/assignments");
        Assert.Contains(
            assignments.EnumerateArray(),
            item => item.GetProperty("id").GetGuid()
                == assignment.GetProperty("id").GetGuid());

        using var duplicateResponse = await client.PostAsJsonAsync(
            $"/api/courses/{courseId}/assignments",
            new
            {
                Slug = "project-1",
                Title = "Duplicate Project",
                Description = "",
                TemplateRepositoryId = (Guid?)null,
                DueAt = (DateTimeOffset?)null,
            });
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);

        using var invalidResponse = await client.PostAsJsonAsync(
            $"/api/courses/{courseId}/assignments",
            new
            {
                Slug = "invalid-project",
                Title = "Invalid Project",
                Description = (string?)null,
                TemplateRepositoryId = (Guid?)null,
                DueAt = (DateTimeOffset?)null,
            });
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
    }

    [Fact]
    public async Task AssignmentCreationSupportsBlankRepositoriesAndIsolatesTemplates()
    {
        using var client = factory.CreateClient();
        var owner = await CreateActivatedUserAsync(
            client,
            "Instructor",
            "Template Owner");
        var otherInstructor = await CreateActivatedUserAsync(
            client,
            "Instructor",
            "Other Instructor");
        await AuthenticateAsync(client, owner.Email, owner.Password);
        using var templateResponse = await client.PostAsJsonAsync(
            "/api/repository-templates",
            new
            {
                Name = $"private-template-{Guid.NewGuid():N}",
                DisplayName = "Private Template",
            });
        templateResponse.EnsureSuccessStatusCode();
        var template = await templateResponse.Content.ReadFromJsonAsync<JsonElement>();

        await AuthenticateAsync(
            client,
            otherInstructor.Email,
            otherInstructor.Password);
        var courseId = await CreateCourseAsync(client);
        using var blankResponse = await client.PostAsJsonAsync(
            $"/api/courses/{courseId}/assignments",
            new
            {
                Slug = "blank-project",
                Title = "Blank Project",
                Description = "",
                TemplateRepositoryId = (Guid?)null,
                DueAt = (DateTimeOffset?)null,
            });
        Assert.Equal(HttpStatusCode.Created, blankResponse.StatusCode);

        using var inaccessibleTemplateResponse = await client.PostAsJsonAsync(
            $"/api/courses/{courseId}/assignments",
            new
            {
                Slug = "stolen-template",
                Title = "Inaccessible Template",
                Description = "",
                TemplateRepositoryId = template.GetProperty("id").GetGuid(),
                DueAt = (DateTimeOffset?)null,
            });
        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            inaccessibleTemplateResponse.StatusCode);
    }

    [Fact]
    public async Task TeachingAssistantCanReadButCannotCreateAssignments()
    {
        using var client = factory.CreateClient();
        var instructor = await CreateActivatedUserAsync(
            client,
            "Instructor",
            "Assignment Owner");
        var teachingAssistant = await CreateActivatedUserAsync(
            client,
            "TA",
            "Assignment TA");
        await AuthenticateAsync(client, instructor.Email, instructor.Password);
        var courseId = await CreateCourseAsync(client);
        using var assignmentResponse = await client.PostAsJsonAsync(
            $"/api/courses/{courseId}/assignments",
            new
            {
                Slug = "visible-project",
                Title = "Visible Project",
                Description = "",
                TemplateRepositoryId = (Guid?)null,
                DueAt = (DateTimeOffset?)null,
            });
        assignmentResponse.EnsureSuccessStatusCode();
        using var staffResponse = await client.PostAsJsonAsync(
            $"/api/courses/{courseId}/staff",
            new { UserId = teachingAssistant.Id, Role = "TA" });
        staffResponse.EnsureSuccessStatusCode();

        await AuthenticateAsync(
            client,
            teachingAssistant.Email,
            teachingAssistant.Password);
        using var listResponse = await client.GetAsync(
            $"/api/courses/{courseId}/assignments");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        using var createResponse = await client.PostAsJsonAsync(
            $"/api/courses/{courseId}/assignments",
            new
            {
                Slug = "forbidden-project",
                Title = "Forbidden Project",
                Description = "",
                TemplateRepositoryId = (Guid?)null,
                DueAt = (DateTimeOffset?)null,
            });
        Assert.Equal(HttpStatusCode.Forbidden, createResponse.StatusCode);
    }

    [Fact]
    public async Task InstructorProvisioningCreatesOneTemplateBackedRepositoryPerStudentIdempotently()
    {
        using var client = factory.CreateClient();
        var instructor = await CreateActivatedUserAsync(
            client,
            "Instructor",
            "Provisioning Instructor");
        var firstStudent = await CreateActivatedUserAsync(
            client,
            "Student",
            "First Provisioned Student");
        var secondStudent = await CreateActivatedUserAsync(
            client,
            "Student",
            "Second Provisioned Student");
        var lateStudent = await CreateActivatedUserAsync(
            client,
            "Student",
            "Late Provisioned Student");
        await AuthenticateAsync(client, instructor.Email, instructor.Password);
        var courseId = await CreateCourseAsync(client);
        using var importResponse = await client.PostAsJsonAsync(
            $"/api/courses/{courseId}/roster/import",
            new
            {
                Csv = $"email,displayName\n{firstStudent.Email},First Student\n{secondStudent.Email},Second Student",
            });
        importResponse.EnsureSuccessStatusCode();

        var templateName = $"provisioning-template-{Guid.NewGuid():N}";
        using var templateResponse = await client.PostAsJsonAsync(
            "/api/repository-templates",
            new { Name = templateName, DisplayName = "Provisioning Starter" });
        templateResponse.EnsureSuccessStatusCode();
        var template = await templateResponse.Content.ReadFromJsonAsync<JsonElement>();
        await AddTemplateCommitAsync(templateName);

        using var assignmentResponse = await client.PostAsJsonAsync(
            $"/api/courses/{courseId}/assignments",
            new
            {
                Slug = "project-one",
                Title = "Project One",
                Description = "",
                TemplateRepositoryId = template.GetProperty("id").GetGuid(),
                DueAt = (DateTimeOffset?)null,
            });
        assignmentResponse.EnsureSuccessStatusCode();
        var assignment =
            await assignmentResponse.Content.ReadFromJsonAsync<JsonElement>();
        var assignmentId = assignment.GetProperty("id").GetGuid();
        var damagedRepositoryPath = Directory.EnumerateDirectories(
                Path.Combine(factory.StorageRoot, "repositories"),
                $"*-{firstStudent.Id:N}.git")
            .Single();
        foreach (var file in Directory.EnumerateFiles(
                     damagedRepositoryPath,
                     "*",
                     SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(damagedRepositoryPath, recursive: true);
        Directory.CreateDirectory(damagedRepositoryPath);
        using var lateImportResponse = await client.PostAsJsonAsync(
            $"/api/courses/{courseId}/roster/import",
            new
            {
                Csv = $"email,displayName\n{lateStudent.Email},Late Student",
            });
        lateImportResponse.EnsureSuccessStatusCode();

        using var firstProvisionResponse = await client.PostAsync(
            $"/api/courses/{courseId}/assignments/{assignmentId}/repositories",
            content: null);
        Assert.Equal(HttpStatusCode.OK, firstProvisionResponse.StatusCode);
        var firstProvision =
            await firstProvisionResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, firstProvision.GetProperty("createdRepositories").GetInt32());
        Assert.Equal(2, firstProvision.GetProperty("existingRepositories").GetInt32());
        var repositories = firstProvision.GetProperty("repositories")
            .EnumerateArray()
            .ToArray();
        Assert.Equal(3, repositories.Length);

        using var secondProvisionResponse = await client.PostAsync(
            $"/api/courses/{courseId}/assignments/{assignmentId}/repositories",
            content: null);
        secondProvisionResponse.EnsureSuccessStatusCode();
        var secondProvision =
            await secondProvisionResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, secondProvision.GetProperty("createdRepositories").GetInt32());
        Assert.Equal(3, secondProvision.GetProperty("existingRepositories").GetInt32());

        await AuthenticateAsync(
            client,
            firstStudent.Email,
            firstStudent.Password);
        var accessibleRepositories = await client.GetFromJsonAsync<JsonElement>(
            "/api/repositories");
        var firstRepository = Assert.Single(
            repositories,
            item => item.GetProperty("studentUserId").GetGuid() == firstStudent.Id);
        var secondRepository = Assert.Single(
            repositories,
            item => item.GetProperty("studentUserId").GetGuid() == secondStudent.Id);
        Assert.Contains(
            accessibleRepositories.EnumerateArray(),
            item => item.GetProperty("name").GetString()
                == firstRepository.GetProperty("repositoryName").GetString());
        Assert.DoesNotContain(
            accessibleRepositories.EnumerateArray(),
            item => item.GetProperty("name").GetString()
                == secondRepository.GetProperty("repositoryName").GetString());

        var branches = await client.GetFromJsonAsync<JsonElement>(
            $"/api/repositories/{firstRepository.GetProperty("repositoryName").GetString()}/branches");
        Assert.Equal("main", Assert.Single(branches.EnumerateArray())
            .GetProperty("name")
            .GetString());
    }

    [Fact]
    public async Task CourseStaffCanAccessProvisionedRepositoriesButTeachingAssistantsCannotPush()
    {
        using var client = factory.CreateClient();
        var instructor = await CreateActivatedUserAsync(
            client,
            "Instructor",
            "Repository Instructor");
        var teachingAssistant = await CreateActivatedUserAsync(
            client,
            "TA",
            "Repository TA");
        var student = await CreateActivatedUserAsync(
            client,
            "Student",
            "Repository Student");
        await AuthenticateAsync(client, instructor.Email, instructor.Password);
        var courseId = await CreateCourseAsync(client);
        using var staffResponse = await client.PostAsJsonAsync(
            $"/api/courses/{courseId}/staff",
            new { UserId = teachingAssistant.Id, Role = "TA" });
        staffResponse.EnsureSuccessStatusCode();
        using var importResponse = await client.PostAsJsonAsync(
            $"/api/courses/{courseId}/roster/import",
            new
            {
                Csv = $"email,displayName\n{student.Email},Repository Student",
            });
        importResponse.EnsureSuccessStatusCode();
        using var assignmentResponse = await client.PostAsJsonAsync(
            $"/api/courses/{courseId}/assignments",
            new
            {
                Slug = "private-project",
                Title = "Private Project",
                Description = "",
                TemplateRepositoryId = (Guid?)null,
                DueAt = (DateTimeOffset?)null,
            });
        assignmentResponse.EnsureSuccessStatusCode();
        var assignment =
            await assignmentResponse.Content.ReadFromJsonAsync<JsonElement>();
        using var provisionResponse = await client.PostAsync(
            $"/api/courses/{courseId}/assignments/{assignment.GetProperty("id").GetGuid()}/repositories",
            content: null);
        provisionResponse.EnsureSuccessStatusCode();
        var provision =
            await provisionResponse.Content.ReadFromJsonAsync<JsonElement>();
        var repositoryName = provision.GetProperty("repositories")[0]
            .GetProperty("repositoryName")
            .GetString();

        var instructorRepositories = await client.GetFromJsonAsync<JsonElement>(
            "/api/repositories");
        Assert.Contains(
            instructorRepositories.EnumerateArray(),
            item => item.GetProperty("name").GetString() == repositoryName);

        await AuthenticateAsync(
            client,
            teachingAssistant.Email,
            teachingAssistant.Password);
        var assistantRepositories = await client.GetFromJsonAsync<JsonElement>(
            "/api/repositories");
        Assert.Contains(
            assistantRepositories.EnumerateArray(),
            item => item.GetProperty("name").GetString() == repositoryName);
        using var tokenResponse = await client.PostAsJsonAsync(
            "/api/personal-access-tokens",
            new { Name = "TA Git", ExpiresAt = (DateTimeOffset?)null });
        tokenResponse.EnsureSuccessStatusCode();
        var token = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes(
                    $"{teachingAssistant.Email}:{token.GetProperty("token").GetString()}")));

        using var readResponse = await client.GetAsync(
            $"/{repositoryName}.git/info/refs?service=git-upload-pack");
        using var writeResponse = await client.GetAsync(
            $"/{repositoryName}.git/info/refs?service=git-receive-pack");

        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, writeResponse.StatusCode);
    }

    [Fact]
    public async Task AssignmentCreationAutomaticallyProvisionsEnrolledStudents()
    {
        using var client = factory.CreateClient();
        var instructor = await CreateActivatedUserAsync(
            client,
            "Instructor",
            "Automatic Provisioning Instructor");
        var student = await CreateActivatedUserAsync(
            client,
            "Student",
            "Automatic Provisioning Student");
        await AuthenticateAsync(client, instructor.Email, instructor.Password);
        var courseId = await CreateCourseAsync(client);
        using var importResponse = await client.PostAsJsonAsync(
            $"/api/courses/{courseId}/roster/import",
            new
            {
                Csv = $"email,displayName\n{student.Email},Automatic Student",
            });
        importResponse.EnsureSuccessStatusCode();

        using var assignmentResponse = await client.PostAsJsonAsync(
            $"/api/courses/{courseId}/assignments",
            new
            {
                Slug = "automatic-project",
                Title = "Automatic Project",
                Description = "",
                TemplateRepositoryId = (Guid?)null,
                DueAt = (DateTimeOffset?)null,
            });
        assignmentResponse.EnsureSuccessStatusCode();
        var createdAssignment =
            await assignmentResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, createdAssignment.GetProperty("studentCount").GetInt32());
        Assert.Equal(
            1,
            createdAssignment.GetProperty("provisionedRepositoryCount").GetInt32());

        await AuthenticateAsync(client, student.Email, student.Password);
        var repositories = await client.GetFromJsonAsync<JsonElement>(
            "/api/repositories");
        var repository = Assert.Single(repositories.EnumerateArray());
        Assert.EndsWith(
            student.Id.ToString("N"),
            repository.GetProperty("name").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CourseStaffCanViewEveryStudentRepositoryStatusForAnAssignment()
    {
        using var client = factory.CreateClient();
        var instructor = await CreateActivatedUserAsync(
            client,
            "Instructor",
            "Dashboard Instructor");
        var teachingAssistant = await CreateActivatedUserAsync(
            client,
            "TA",
            "Dashboard TA");
        var activeStudent = await CreateActivatedUserAsync(
            client,
            "Student",
            "Active Dashboard Student");
        var pendingStudent = await CreateActivatedUserAsync(
            client,
            "Student",
            "Pending Dashboard Student");
        var unrelatedInstructor = await CreateActivatedUserAsync(
            client,
            "Instructor",
            "Unrelated Dashboard Instructor");
        await AuthenticateAsync(client, instructor.Email, instructor.Password);
        var courseId = await CreateCourseAsync(client);
        using var staffResponse = await client.PostAsJsonAsync(
            $"/api/courses/{courseId}/staff",
            new { UserId = teachingAssistant.Id, Role = "TA" });
        staffResponse.EnsureSuccessStatusCode();
        using var initialImportResponse = await client.PostAsJsonAsync(
            $"/api/courses/{courseId}/roster/import",
            new
            {
                Csv = $"email,displayName\n{activeStudent.Email},Active Student",
            });
        initialImportResponse.EnsureSuccessStatusCode();
        var templateName = $"dashboard-template-{Guid.NewGuid():N}";
        using var templateResponse = await client.PostAsJsonAsync(
            "/api/repository-templates",
            new { Name = templateName, DisplayName = "Dashboard Starter" });
        templateResponse.EnsureSuccessStatusCode();
        var template = await templateResponse.Content.ReadFromJsonAsync<JsonElement>();
        await AddTemplateCommitAsync(templateName);
        using var assignmentResponse = await client.PostAsJsonAsync(
            $"/api/courses/{courseId}/assignments",
            new
            {
                Slug = "dashboard-project",
                Title = "Dashboard Project",
                Description = "",
                TemplateRepositoryId = template.GetProperty("id").GetGuid(),
                DueAt = (DateTimeOffset?)null,
            });
        assignmentResponse.EnsureSuccessStatusCode();
        var assignment =
            await assignmentResponse.Content.ReadFromJsonAsync<JsonElement>();
        var assignmentId = assignment.GetProperty("id").GetGuid();
        using var lateImportResponse = await client.PostAsJsonAsync(
            $"/api/courses/{courseId}/roster/import",
            new
            {
                Csv = $"email,displayName\n{pendingStudent.Email},Pending Student",
            });
        lateImportResponse.EnsureSuccessStatusCode();

        using var instructorResponse = await client.GetAsync(
            $"/api/courses/{courseId}/assignments/{assignmentId}/status");
        instructorResponse.EnsureSuccessStatusCode();
        var dashboard =
            await instructorResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, dashboard.GetProperty("totalStudents").GetInt32());
        Assert.Equal(
            1,
            dashboard.GetProperty("provisionedRepositories").GetInt32());
        var students = dashboard.GetProperty("students").EnumerateArray().ToArray();
        var active = Assert.Single(
            students,
            item => item.GetProperty("studentUserId").GetGuid()
                == activeStudent.Id);
        Assert.Equal("Active", active.GetProperty("state").GetString());
        Assert.Equal("main", active.GetProperty("branch").GetString());
        Assert.Equal(
            "Add starter",
            active.GetProperty("latestCommit").GetProperty("subject").GetString());
        var activeRepositoryName =
            active.GetProperty("repositoryName").GetString();
        var pending = Assert.Single(
            students,
            item => item.GetProperty("studentUserId").GetGuid()
                == pendingStudent.Id);
        Assert.Equal("NotProvisioned", pending.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, pending.GetProperty("repositoryName").ValueKind);

        using var bulkDownloadResponse = await client.GetAsync(
            $"/api/courses/{courseId}/assignments/{assignmentId}/archive");
        bulkDownloadResponse.EnsureSuccessStatusCode();
        Assert.Equal(
            "application/zip",
            bulkDownloadResponse.Content.Headers.ContentType?.MediaType);
        await using (var archiveStream =
                     await bulkDownloadResponse.Content.ReadAsStreamAsync())
        using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read))
        {
            Assert.Contains(
                archive.Entries,
                entry => entry.FullName == $"{activeRepositoryName}/README.md");
        }

        using var individualDownloadResponse = await client.GetAsync(
            $"/api/repositories/{activeRepositoryName}/archive?branch=main");
        individualDownloadResponse.EnsureSuccessStatusCode();
        var auditEvents = await client.GetFromJsonAsync<JsonElement>(
            $"/api/courses/{courseId}/audit-events");
        Assert.Contains(
            auditEvents.EnumerateArray(),
            item => item.GetProperty("action").GetString()
                == "AssignmentArchiveDownloaded"
                && item.GetProperty("assignmentId").GetGuid() == assignmentId);
        Assert.Contains(
            auditEvents.EnumerateArray(),
            item => item.GetProperty("action").GetString()
                == "RepositoryArchiveDownloaded"
                && item.GetProperty("repositoryName").GetString()
                    == activeRepositoryName);

        using var blankAssignmentResponse = await client.PostAsJsonAsync(
            $"/api/courses/{courseId}/assignments",
            new
            {
                Slug = "empty-dashboard-project",
                Title = "Empty Dashboard Project",
                Description = "",
                TemplateRepositoryId = (Guid?)null,
                DueAt = (DateTimeOffset?)null,
            });
        blankAssignmentResponse.EnsureSuccessStatusCode();
        var blankAssignment =
            await blankAssignmentResponse.Content.ReadFromJsonAsync<JsonElement>();
        var blankAssignmentId = blankAssignment.GetProperty("id").GetGuid();
        var blankDashboard = await client.GetFromJsonAsync<JsonElement>(
            $"/api/courses/{courseId}/assignments/{blankAssignmentId}/status");
        Assert.All(
            blankDashboard.GetProperty("students").EnumerateArray(),
            item => Assert.Equal("Empty", item.GetProperty("state").GetString()));
        var damagedRepositoryName = blankDashboard.GetProperty("students")[0]
            .GetProperty("repositoryName")
            .GetString();
        var damagedRepositoryPath = Path.Combine(
            factory.StorageRoot,
            "repositories",
            $"{damagedRepositoryName}.git");
        Directory.Delete(damagedRepositoryPath, recursive: true);
        var damagedDashboard = await client.GetFromJsonAsync<JsonElement>(
            $"/api/courses/{courseId}/assignments/{blankAssignmentId}/status");
        var unavailable = Assert.Single(
            damagedDashboard.GetProperty("students").EnumerateArray(),
            item => item.GetProperty("repositoryName").GetString()
                == damagedRepositoryName);
        Assert.Equal("Unavailable", unavailable.GetProperty("state").GetString());

        await AuthenticateAsync(
            client,
            teachingAssistant.Email,
            teachingAssistant.Password);
        using var assistantResponse = await client.GetAsync(
            $"/api/courses/{courseId}/assignments/{assignmentId}/status");
        Assert.Equal(HttpStatusCode.OK, assistantResponse.StatusCode);
        using var assistantDownloadResponse = await client.GetAsync(
            $"/api/courses/{courseId}/assignments/{assignmentId}/archive");
        Assert.Equal(HttpStatusCode.OK, assistantDownloadResponse.StatusCode);

        await AuthenticateAsync(
            client,
            activeStudent.Email,
            activeStudent.Password);
        using var studentResponse = await client.GetAsync(
            $"/api/courses/{courseId}/assignments/{assignmentId}/status");
        Assert.Equal(HttpStatusCode.Forbidden, studentResponse.StatusCode);
        using var studentDownloadResponse = await client.GetAsync(
            $"/api/courses/{courseId}/assignments/{assignmentId}/archive");
        Assert.Equal(HttpStatusCode.Forbidden, studentDownloadResponse.StatusCode);

        await AuthenticateAsync(
            client,
            unrelatedInstructor.Email,
            unrelatedInstructor.Password);
        using var unrelatedResponse = await client.GetAsync(
            $"/api/courses/{courseId}/assignments/{assignmentId}/status");
        Assert.Equal(HttpStatusCode.NotFound, unrelatedResponse.StatusCode);

        await AuthenticateAsync(
            client,
            ApiFactory.AdministratorEmail,
            ApiFactory.AdministratorPassword);
        using var administratorResponse = await client.GetAsync(
            $"/api/courses/{courseId}/assignments/{assignmentId}/status");
        Assert.Equal(HttpStatusCode.OK, administratorResponse.StatusCode);
    }

    private static async Task<Guid> CreateCourseAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/courses",
            new
            {
                Code = $"ROS{Guid.NewGuid():N}"[..20],
                Name = "Roster Course",
            });
        response.EnsureSuccessStatusCode();
        var course = await response.Content.ReadFromJsonAsync<JsonElement>();
        return course.GetProperty("id").GetGuid();
    }

    private static async Task<TestAccount> CreateActivatedUserAsync(
        HttpClient client,
        string role,
        string displayName)
    {
        await AuthenticateAsync(
            client,
            ApiFactory.AdministratorEmail,
            ApiFactory.AdministratorPassword);
        var email = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@example.edu";
        const string password = "Course-Account-42!";
        using var createResponse = await client.PostAsJsonAsync(
            "/api/admin/users",
            new { Email = email, DisplayName = displayName, Role = role });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();
        using var tokenResponse = await client.PostAsync(
            $"/api/admin/users/{id}/activation-token",
            content: null);
        tokenResponse.EnsureSuccessStatusCode();
        var token = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = null;
        using var activateResponse = await client.PostAsJsonAsync(
            "/api/auth/activate",
            new
            {
                UserId = id,
                Token = token.GetProperty("token").GetString(),
                Password = password,
            });
        activateResponse.EnsureSuccessStatusCode();
        return new TestAccount(id, email, password);
    }

    private static async Task AuthenticateAsync(
        HttpClient client,
        string email,
        string password)
    {
        client.DefaultRequestHeaders.Authorization = null;
        using var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { Email = email, Password = password });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            payload.GetProperty("accessToken").GetString());
    }

    private async Task AddTemplateCommitAsync(string templateName)
    {
        var workingDirectory = Path.Combine(
            Path.GetTempPath(),
            $"pirate-forge-template-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workingDirectory);
        try
        {
            RunGit(workingDirectory, "init", "-b", "main", "--quiet");
            RunGit(workingDirectory, "config", "user.name", "Template Author");
            RunGit(
                workingDirectory,
                "config",
                "user.email",
                "template@example.edu");
            await File.WriteAllTextAsync(
                Path.Combine(workingDirectory, "README.md"),
                "# Starter");
            RunGit(workingDirectory, "add", "README.md");
            RunGit(workingDirectory, "commit", "-m", "Add starter", "--quiet");
            RunGit(
                workingDirectory,
                "push",
                Path.Combine(
                    factory.StorageRoot,
                    "repositories",
                    $"{templateName}.git"),
                "main",
                "--quiet");
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(
                         workingDirectory,
                         "*",
                         SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    private static void RunGit(string workingDirectory, params string[] arguments)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("Git did not start.");
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(
            process.ExitCode == 0,
            $"git {string.Join(' ', arguments)} failed: {error}");
    }

    private sealed record TestAccount(
        Guid Id,
        string Email,
        string Password);
}
