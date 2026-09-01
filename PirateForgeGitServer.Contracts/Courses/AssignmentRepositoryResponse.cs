namespace PirateForgeGitServer.Contracts.Courses;

public sealed record AssignmentRepositoryResponse(
    Guid StudentUserId,
    string StudentEmail,
    string StudentDisplayName,
    string RepositoryName,
    string RepositoryDisplayName,
    DateTimeOffset ProvisionedAt);

public sealed record AssignmentProvisioningResponse(
    Guid AssignmentId,
    int TotalStudents,
    int CreatedRepositories,
    int ExistingRepositories,
    IReadOnlyList<AssignmentRepositoryResponse> Repositories);
