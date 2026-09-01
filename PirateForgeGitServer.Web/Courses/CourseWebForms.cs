namespace PirateForgeGitServer.Web.Courses;

public sealed class CreateCourseForm
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

public sealed class AddCourseStaffForm
{
    public string StaffSelection { get; set; } = string.Empty;
}

public sealed class RemoveCourseStaffForm
{
    public Guid UserId { get; set; }
}

public sealed class ImportCourseRosterForm
{
    public IFormFile? RosterFile { get; set; }
}

public sealed class ActivateAccountForm
{
    public Guid UserId { get; set; }

    public string Token { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string ConfirmPassword { get; set; } = string.Empty;
}
