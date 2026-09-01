using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PirateForgeGitServer.Domain.Courses;
using PirateForgeGitServer.Domain.Identity;
using PirateForgeGitServer.Infrastructure.Identity;
using PirateForgeGitServer.Infrastructure.Persistence;

namespace PirateForgeGitServer.Infrastructure.Courses;

public sealed class CourseService(
    IDbContextFactory<PirateForgeDbContext> dbContextFactory,
    UserManager<PirateForgeUser> userManager,
    TimeProvider timeProvider)
{
    public async Task<Course?> CreateAsync(
        string code,
        string name,
        Guid creatorUserId,
        CancellationToken cancellationToken)
    {
        var course = Course.Create(
            code,
            name,
            creatorUserId,
            timeProvider.GetUtcNow());
        await using var context = await dbContextFactory.CreateDbContextAsync(
            cancellationToken);
        if (await context.Courses.AnyAsync(
                value => value.Code == course.Code,
                cancellationToken))
        {
            return null;
        }

        context.Courses.Add(course);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return course;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is Microsoft.Data.Sqlite.SqliteException
            {
                SqliteErrorCode: 19,
            })
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<Course>> ListAccessibleAsync(
        Guid userId,
        bool isAdministrator,
        CancellationToken cancellationToken)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(
            cancellationToken);
        IQueryable<Course> query = context.Courses
            .AsNoTracking()
            .Include(course => course.Memberships);
        if (!isAdministrator)
        {
            query = query.Where(course =>
                course.Memberships.Any(membership =>
                    membership.UserId == userId));
        }

        return await query
            .OrderBy(course => course.Code)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<Course?> FindAccessibleAsync(
        Guid courseId,
        Guid userId,
        bool isAdministrator,
        CancellationToken cancellationToken)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(
            cancellationToken);
        return await context.Courses
            .AsNoTracking()
            .Include(course => course.Memberships)
            .SingleOrDefaultAsync(
                course => course.Id == courseId
                    && (isAdministrator
                        || course.Memberships.Any(membership =>
                            membership.UserId == userId)),
                cancellationToken);
    }

    public async Task<CourseStaffChangeResult> AddStaffAsync(
        Guid courseId,
        Guid userId,
        string role,
        Guid actingUserId,
        bool isAdministrator,
        CancellationToken cancellationToken)
    {
        if (role is not (
            ApplicationRoles.Instructor
            or ApplicationRoles.TeachingAssistant))
        {
            return CourseStaffChangeResult.InvalidRole;
        }

        await using var context = await dbContextFactory.CreateDbContextAsync(
            cancellationToken);
        var course = await context.Courses
            .Include(value => value.Memberships)
            .SingleOrDefaultAsync(
                value => value.Id == courseId,
                cancellationToken);
        if (course is null)
        {
            return CourseStaffChangeResult.NotFound;
        }

        if (!CanManage(course, actingUserId, isAdministrator))
        {
            return CourseStaffChangeResult.Forbidden;
        }

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsActive)
        {
            return CourseStaffChangeResult.UserNotFound;
        }

        if (!await userManager.IsInRoleAsync(user, role))
        {
            return CourseStaffChangeResult.RoleMismatch;
        }

        try
        {
            course.AddStaff(userId, role, timeProvider.GetUtcNow());
        }
        catch (InvalidOperationException)
        {
            return CourseStaffChangeResult.Conflict;
        }

        await context.SaveChangesAsync(cancellationToken);
        return CourseStaffChangeResult.Success;
    }

    public async Task<CourseStaffChangeResult> RemoveStaffAsync(
        Guid courseId,
        Guid userId,
        Guid actingUserId,
        bool isAdministrator,
        CancellationToken cancellationToken)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(
            cancellationToken);
        var course = await context.Courses
            .Include(value => value.Memberships)
            .SingleOrDefaultAsync(
                value => value.Id == courseId,
                cancellationToken);
        if (course is null)
        {
            return CourseStaffChangeResult.NotFound;
        }

        if (!CanManage(course, actingUserId, isAdministrator))
        {
            return CourseStaffChangeResult.Forbidden;
        }

        try
        {
            course.RemoveStaff(userId);
        }
        catch (InvalidOperationException)
        {
            return CourseStaffChangeResult.Conflict;
        }

        await context.SaveChangesAsync(cancellationToken);
        return CourseStaffChangeResult.Success;
    }

    private static bool CanManage(
        Course course,
        Guid actingUserId,
        bool isAdministrator) =>
        isAdministrator
        || course.Memberships.Any(membership =>
            membership.UserId == actingUserId
            && membership.Role == ApplicationRoles.Instructor);
}

public enum CourseStaffChangeResult
{
    Success,
    NotFound,
    UserNotFound,
    Forbidden,
    InvalidRole,
    RoleMismatch,
    Conflict,
}
