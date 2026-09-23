using PirateForgeGitServer.Contracts.Courses;

namespace PirateForgeGitServer.Web.Components.Repositories;

public sealed record CourseRepositoryRow(
    CourseResponse Course,
    AssignmentResponse Assignment);
