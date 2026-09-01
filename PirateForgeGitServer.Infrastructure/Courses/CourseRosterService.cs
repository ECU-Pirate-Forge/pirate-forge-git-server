using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PirateForgeGitServer.Application.Courses;
using PirateForgeGitServer.Domain.Courses;
using PirateForgeGitServer.Domain.Identity;
using PirateForgeGitServer.Infrastructure.Identity;
using PirateForgeGitServer.Infrastructure.Persistence;

namespace PirateForgeGitServer.Infrastructure.Courses;

public sealed class CourseRosterService(
    IDbContextFactory<PirateForgeDbContext> dbContextFactory,
    UserManager<PirateForgeUser> userManager,
    TimeProvider timeProvider)
{
    public const int MaximumCsvCharacters = 1024 * 1024;

    public async Task<CourseRosterListResult> ListAsync(
        Guid courseId,
        Guid actingUserId,
        bool isAdministrator,
        CancellationToken cancellationToken)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(
            cancellationToken);
        var course = await context.Courses
            .AsNoTracking()
            .Include(value => value.Memberships)
            .SingleOrDefaultAsync(value => value.Id == courseId, cancellationToken);
        var access = GetAccess(course, actingUserId, isAdministrator);
        if (access != CourseRosterAccess.Success)
        {
            return new(access, []);
        }

        var memberships = course!.Memberships
            .Where(value => value.Role == ApplicationRoles.Student)
            .ToDictionary(value => value.UserId);
        var studentIds = memberships.Keys.ToArray();
        var students = await context.Users
            .AsNoTracking()
            .Where(user => studentIds.Contains(user.Id))
            .OrderBy(user => user.DisplayName)
            .ThenBy(user => user.Email)
            .Select(user => new CourseRosterStudentRecord(
                user.Id,
                user.Email ?? string.Empty,
                user.DisplayName,
                memberships[user.Id].AddedAt))
            .ToArrayAsync(cancellationToken);
        return new(CourseRosterAccess.Success, students);
    }

    public async Task<CourseRosterImportResult> ImportAsync(
        Guid courseId,
        string? csv,
        Guid actingUserId,
        bool isAdministrator,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return CourseRosterImportResult.Invalid("A roster CSV is required.");
        }

        if (csv.Length > MaximumCsvCharacters)
        {
            return CourseRosterImportResult.Invalid(
                $"The roster cannot exceed {MaximumCsvCharacters} characters.");
        }

        CourseRosterParseResult parsed;
        try
        {
            parsed = CourseRosterCsvParser.Parse(csv);
        }
        catch (FormatException exception)
        {
            return CourseRosterImportResult.Invalid(exception.Message);
        }

        await using var context = await dbContextFactory.CreateDbContextAsync(
            cancellationToken);
        var course = await context.Courses
            .Include(value => value.Memberships)
            .SingleOrDefaultAsync(value => value.Id == courseId, cancellationToken);
        var access = GetAccess(course, actingUserId, isAdministrator);
        if (access != CourseRosterAccess.Success)
        {
            return new(access, [], null);
        }

        var rows = new List<CourseRosterImportRowRecord>(
            parsed.Students.Count + parsed.Errors.Count);
        var createdUsers = new List<PirateForgeUser>();
        try
        {
            foreach (var student in parsed.Students)
            {
                var user = await userManager.FindByEmailAsync(student.Email);
                if (user is not null)
                {
                    var roles = await userManager.GetRolesAsync(user);
                    if (!user.IsActive
                        || roles.Count != 1
                        || roles[0] != ApplicationRoles.Student)
                    {
                        rows.Add(CourseRosterImportRowRecord.Failed(
                            student,
                            "RoleConflict",
                            "An account with this email exists but is not an active Student."));
                        continue;
                    }

                    bool enrolled;
                    try
                    {
                        enrolled = course!.EnrollStudent(
                            user.Id,
                            timeProvider.GetUtcNow());
                    }
                    catch (InvalidOperationException)
                    {
                        rows.Add(CourseRosterImportRowRecord.Failed(
                            student,
                            "RoleConflict",
                            "The student already has a staff membership in this course."));
                        continue;
                    }

                    var activationToken = await GenerateActivationTokenIfNeededAsync(user);
                    rows.Add(new(
                        student.RowNumber,
                        student.Email,
                        user.DisplayName,
                        enrolled ? "Enrolled" : "AlreadyEnrolled",
                        user.Id,
                        activationToken,
                        null));
                    continue;
                }

                user = new PirateForgeUser
                {
                    Id = Guid.NewGuid(),
                    Email = student.Email,
                    UserName = student.Email,
                    DisplayName = student.DisplayName,
                    EmailConfirmed = false,
                    IsActive = true,
                };
                var createResult = await userManager.CreateAsync(user);
                if (!createResult.Succeeded)
                {
                    rows.Add(CourseRosterImportRowRecord.Failed(
                        student,
                        "Invalid",
                        string.Join(
                            " ",
                            createResult.Errors.Select(error => error.Description))));
                    continue;
                }

                var roleResult = await userManager.AddToRoleAsync(
                    user,
                    ApplicationRoles.Student);
                if (!roleResult.Succeeded)
                {
                    await userManager.DeleteAsync(user);
                    rows.Add(CourseRosterImportRowRecord.Failed(
                        student,
                        "Invalid",
                        string.Join(
                            " ",
                            roleResult.Errors.Select(error => error.Description))));
                    continue;
                }

                createdUsers.Add(user);
                course!.EnrollStudent(user.Id, timeProvider.GetUtcNow());
                rows.Add(new(
                    student.RowNumber,
                    student.Email,
                    student.DisplayName,
                    "Created",
                    user.Id,
                    await GenerateActivationTokenIfNeededAsync(user),
                    null));
            }

            rows.AddRange(parsed.Errors.Select(error => new CourseRosterImportRowRecord(
                error.RowNumber,
                string.Empty,
                string.Empty,
                "Invalid",
                null,
                null,
                error.Message)));

            await context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            foreach (var createdUser in createdUsers)
            {
                await userManager.DeleteAsync(createdUser);
            }

            throw;
        }

        return new(
            CourseRosterAccess.Success,
            rows.OrderBy(row => row.RowNumber).ToArray(),
            null);
    }

    private async Task<string?> GenerateActivationTokenIfNeededAsync(
        PirateForgeUser user) =>
        await userManager.HasPasswordAsync(user)
            ? null
            : await userManager.GenerateUserTokenAsync(
                user,
                TokenOptions.DefaultProvider,
                AccountTokenPurposes.Activation);

    private static CourseRosterAccess GetAccess(
        Course? course,
        Guid actingUserId,
        bool isAdministrator)
    {
        if (course is null)
        {
            return CourseRosterAccess.NotFound;
        }

        if (isAdministrator
            || course.Memberships.Any(membership =>
                membership.UserId == actingUserId
                && membership.Role == ApplicationRoles.Instructor))
        {
            return CourseRosterAccess.Success;
        }

        return course.Memberships.Any(membership =>
            membership.UserId == actingUserId)
            ? CourseRosterAccess.Forbidden
            : CourseRosterAccess.NotFound;
    }
}

public enum CourseRosterAccess
{
    Success,
    NotFound,
    Forbidden,
    Invalid,
}

public sealed record CourseRosterListResult(
    CourseRosterAccess Access,
    IReadOnlyList<CourseRosterStudentRecord> Students);

public sealed record CourseRosterStudentRecord(
    Guid UserId,
    string Email,
    string DisplayName,
    DateTimeOffset EnrolledAt);

public sealed record CourseRosterImportResult(
    CourseRosterAccess Access,
    IReadOnlyList<CourseRosterImportRowRecord> Rows,
    string? Error)
{
    public static CourseRosterImportResult Invalid(string error) =>
        new(CourseRosterAccess.Invalid, [], error);
}

public sealed record CourseRosterImportRowRecord(
    int RowNumber,
    string Email,
    string DisplayName,
    string Status,
    Guid? UserId,
    string? ActivationToken,
    string? Message)
{
    public static CourseRosterImportRowRecord Failed(
        CourseRosterStudent student,
        string status,
        string message) =>
        new(
            student.RowNumber,
            student.Email,
            student.DisplayName,
            status,
            null,
            null,
            message);
}
