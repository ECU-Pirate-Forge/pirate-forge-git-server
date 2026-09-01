namespace PirateForgeGitServer.Web.Assignments;

public sealed class CreateRepositoryTemplateForm
{
    public string Name { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
}

public sealed class CreateAssignmentForm
{
    public string Slug { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string TemplateRepositoryId { get; set; } = string.Empty;

    public string DueAt { get; set; } = string.Empty;

    public bool IsGroupAssignment { get; set; }

    public string MinTeamSize { get; set; } = string.Empty;

    public string MaxTeamSize { get; set; } = string.Empty;

    public bool RequireTeamSelection { get; set; } = true;

    public bool AllowTeamChanges { get; set; }
}

public sealed class ProvisionAssignmentRepositoriesForm
{
}
