using System.Text.RegularExpressions;
using PirateForgeGitServer.Domain.Identity;

namespace PirateForgeGitServer.Domain.Courses;

public sealed partial class Course
{
    private readonly List<CourseMembership> _memberships = [];

    private Course()
    {
    }

    public Guid Id { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyCollection<CourseMembership> Memberships => _memberships;

    public static Course Create(
        string code,
        string name,
        Guid creatorUserId,
        DateTimeOffset createdAt)
    {
        var normalizedCode = code.Trim().ToUpperInvariant();
        if (!CourseCodePattern().IsMatch(normalizedCode))
        {
            throw new ArgumentException(
                "Course codes may contain letters, numbers, periods, underscores, and hyphens, and must start with a letter or number.",
                nameof(code));
        }

        var normalizedName = name.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName)
            || normalizedName.Length > 200)
        {
            throw new ArgumentException(
                "A course name between 1 and 200 characters is required.",
                nameof(name));
        }

        var course = new Course
        {
            Id = Guid.NewGuid(),
            Code = normalizedCode,
            Name = normalizedName,
            CreatedAt = createdAt,
        };
        course.AddStaff(
            creatorUserId,
            ApplicationRoles.Instructor,
            createdAt);
        return course;
    }

    public void AddStaff(
        Guid userId,
        string role,
        DateTimeOffset addedAt)
    {
        if (role is not (
            ApplicationRoles.Instructor
            or ApplicationRoles.TeachingAssistant))
        {
            throw new ArgumentException(
                "Course staff must be an Instructor or TA.",
                nameof(role));
        }

        if (_memberships.Any(membership => membership.UserId == userId))
        {
            throw new InvalidOperationException(
                "The user is already assigned to this course.");
        }

        _memberships.Add(CourseMembership.Create(Id, userId, role, addedAt));
    }

    public void RemoveStaff(Guid userId)
    {
        var membership = _memberships.SingleOrDefault(
            value => value.UserId == userId)
            ?? throw new InvalidOperationException(
                "The user is not assigned to this course.");
        if (membership.Role == ApplicationRoles.Instructor
            && _memberships.Count(value =>
                value.Role == ApplicationRoles.Instructor) == 1)
        {
            throw new InvalidOperationException(
                "A course must have at least one instructor.");
        }

        _memberships.Remove(membership);
    }

    public bool EnrollStudent(Guid userId, DateTimeOffset addedAt)
    {
        var membership = _memberships.SingleOrDefault(
            value => value.UserId == userId);
        if (membership is not null)
        {
            if (membership.Role != ApplicationRoles.Student)
            {
                throw new InvalidOperationException(
                    "Course staff cannot also be enrolled as a student.");
            }

            return false;
        }

        _memberships.Add(CourseMembership.Create(
            Id,
            userId,
            ApplicationRoles.Student,
            addedAt));
        return true;
    }

    [GeneratedRegex(
        @"^[A-Z0-9][A-Z0-9._-]{0,29}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex CourseCodePattern();
}
