using System.Security.Claims;
using PirateForgeGitServer.Contracts.Git;
using PirateForgeGitServer.Domain.Git;
using PirateForgeGitServer.Domain.Identity;
using PirateForgeGitServer.Infrastructure.Git;
using PirateForgeGitServer.Infrastructure.Courses;

namespace PirateForgeGitServer.ApiService.Git;

internal static class RepositoryEndpoints
{
    public static IEndpointRouteBuilder MapRepositoryEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/repositories",
                async (
                    ClaimsPrincipal principal,
                    GitRepositoryAccessService accessService,
                    CancellationToken cancellationToken) =>
                {
                    var repositories = await accessService.ListAccessibleAsync(
                        principal,
                        cancellationToken);
                    return TypedResults.Ok(repositories.Select(ToResponse));
                })
            .WithTags("Repositories")
            .RequireAuthorization();

        endpoints.MapPost(
                "/api/repositories",
                async Task<IResult> (
                    CreateRepositoryRequest request,
                    ClaimsPrincipal principal,
                    GitRepositoryService repositoryService,
                    CancellationToken cancellationToken) =>
                {
                    GitRepository? repository;
                    try
                    {
                        var ownerUserId = Guid.Parse(
                            principal.FindFirstValue(ClaimTypes.NameIdentifier)
                            ?? throw new InvalidOperationException(
                                "The user identifier is missing."));
                        repository = await repositoryService.CreateAsync(
                            request.Name,
                            request.DisplayName,
                            ownerUserId,
                            cancellationToken);
                    }
                    catch (ArgumentException exception)
                    {
                        return TypedResults.ValidationProblem(
                            new Dictionary<string, string[]>
                            {
                                ["Repository"] = [exception.Message],
                            });
                    }

                    if (repository is null)
                    {
                        return TypedResults.Conflict();
                    }

                    return TypedResults.Created(
                        $"/api/repositories/{repository.Name}",
                        ToResponse(repository));
                })
            .WithTags("Repositories")
            .RequireAuthorization(AuthorizationPolicies.ManageCourses);

        endpoints.MapGet(
                "/api/repository-templates",
                async (
                    ClaimsPrincipal principal,
                    GitRepositoryAccessService accessService,
                    CancellationToken cancellationToken) =>
                {
                    var repositories = await accessService.ListAccessibleAsync(
                        principal,
                        cancellationToken);
                    return TypedResults.Ok(repositories
                        .Where(repository =>
                            repository.Purpose == RepositoryPurpose.Template)
                        .Select(ToTemplateResponse));
                })
            .WithTags("Repository Templates")
            .RequireAuthorization(AuthorizationPolicies.ManageCourses);

        endpoints.MapPost(
                "/api/repository-templates",
                async Task<IResult> (
                    CreateRepositoryRequest request,
                    ClaimsPrincipal principal,
                    GitRepositoryService repositoryService,
                    CancellationToken cancellationToken) =>
                {
                    GitRepository? repository;
                    try
                    {
                        repository = await repositoryService.CreateAsync(
                            request.Name,
                            request.DisplayName,
                            GetUserId(principal),
                            cancellationToken,
                            RepositoryPurpose.Template);
                    }
                    catch (ArgumentException exception)
                    {
                        return TypedResults.ValidationProblem(
                            new Dictionary<string, string[]>
                            {
                                ["Repository"] = [exception.Message],
                            });
                    }

                    return repository is null
                        ? TypedResults.Conflict()
                        : TypedResults.Created(
                            $"/api/repository-templates/{repository.Id}",
                            ToTemplateResponse(repository));
                })
            .WithTags("Repository Templates")
            .RequireAuthorization(AuthorizationPolicies.ManageCourses);

        endpoints.MapGet(
                "/api/repositories/{repositoryName}/branches",
                async Task<IResult> (
                    string repositoryName,
                    ClaimsPrincipal principal,
                    GitRepositoryAccessService accessService,
                    GitRepositoryService repositoryService,
                    CancellationToken cancellationToken) =>
                {
                    var repository = await accessService.FindAccessibleAsync(
                        repositoryName,
                        principal,
                        cancellationToken);
                    if (repository is null)
                    {
                        return TypedResults.NotFound();
                    }

                    var branches = await GitRepositoryReader.ListBranchesAsync(
                        repositoryService.GetRepositoryPath(repository.Name),
                        cancellationToken);
                    return TypedResults.Ok(branches.Select(branch =>
                        new RepositoryBranchResponse(
                            branch.Name,
                            branch.CommitId,
                            branch.CommittedAt)));
                })
            .WithTags("Repositories")
            .RequireAuthorization();

        endpoints.MapGet(
                "/api/repositories/{repositoryName}/tree",
                async Task<IResult> (
                    string repositoryName,
                    string branch,
                    string? path,
                    ClaimsPrincipal principal,
                    GitRepositoryAccessService accessService,
                    GitRepositoryService repositoryService,
                    CancellationToken cancellationToken) =>
                {
                    var repository = await accessService.FindAccessibleAsync(
                        repositoryName,
                        principal,
                        cancellationToken);
                    if (repository is null)
                    {
                        return TypedResults.NotFound();
                    }

                    try
                    {
                        var entries = await GitRepositoryReader.ListTreeAsync(
                            repositoryService.GetRepositoryPath(repository.Name),
                            branch,
                            path,
                            cancellationToken);
                        return TypedResults.Ok(entries.Select(entry =>
                            new RepositoryTreeEntryResponse(
                                entry.Name,
                                entry.Path,
                                entry.Kind,
                                entry.Size)));
                    }
                    catch (ArgumentException)
                    {
                        return TypedResults.BadRequest();
                    }
                    catch (GitCommandException)
                    {
                        return TypedResults.NotFound();
                    }
                })
            .WithTags("Repositories")
            .RequireAuthorization();

        endpoints.MapGet(
                "/api/repositories/{repositoryName}/file",
                async Task<IResult> (
                    string repositoryName,
                    string branch,
                    string path,
                    ClaimsPrincipal principal,
                    GitRepositoryAccessService accessService,
                    GitRepositoryService repositoryService,
                    CancellationToken cancellationToken) =>
                {
                    var repository = await accessService.FindAccessibleAsync(
                        repositoryName,
                        principal,
                        cancellationToken);
                    if (repository is null)
                    {
                        return TypedResults.NotFound();
                    }

                    try
                    {
                        var file = await GitRepositoryReader.ReadFileAsync(
                            repositoryService.GetRepositoryPath(repository.Name),
                            branch,
                            path,
                            cancellationToken);
                        return TypedResults.Ok(new RepositoryFileResponse(
                            file.Path,
                            file.Language,
                            file.Content,
                            file.Size));
                    }
                    catch (ArgumentException)
                    {
                        return TypedResults.BadRequest();
                    }
                    catch (GitFileNotViewableException exception)
                    {
                        return TypedResults.Problem(
                            exception.Message,
                            statusCode: StatusCodes.Status415UnsupportedMediaType);
                    }
                    catch (GitCommandException)
                    {
                        return TypedResults.NotFound();
                    }
                })
            .WithTags("Repositories")
            .RequireAuthorization();

        endpoints.MapGet(
                "/api/repositories/{repositoryName}/commits",
                async Task<IResult> (
                    string repositoryName,
                    string branch,
                    ClaimsPrincipal principal,
                    GitRepositoryAccessService accessService,
                    GitRepositoryService repositoryService,
                    CancellationToken cancellationToken) =>
                {
                    var repository = await accessService.FindAccessibleAsync(
                        repositoryName,
                        principal,
                        cancellationToken);
                    if (repository is null)
                    {
                        return TypedResults.NotFound();
                    }

                    try
                    {
                        var commits = await GitRepositoryReader.ListCommitsAsync(
                            repositoryService.GetRepositoryPath(repository.Name),
                            branch,
                            cancellationToken);
                        return TypedResults.Ok(commits.Select(commit =>
                            new RepositoryCommitResponse(
                                commit.Id,
                                commit.Subject,
                                commit.AuthorName,
                                commit.AuthorEmail,
                                commit.AuthoredAt)));
                    }
                    catch (ArgumentException)
                    {
                        return TypedResults.BadRequest();
                    }
                    catch (GitCommandException)
                    {
                        return TypedResults.NotFound();
                    }
                })
            .WithTags("Repositories")
            .RequireAuthorization();

        endpoints.MapGet(
                "/api/repositories/{repositoryName}/archive",
                async Task<IResult> (
                    string repositoryName,
                    string branch,
                    ClaimsPrincipal principal,
                    GitRepositoryAccessService accessService,
                    GitRepositoryService repositoryService,
                    AssignmentArchiveService archiveService,
                    CancellationToken cancellationToken) =>
                {
                    var repository = await accessService.FindAccessibleAsync(
                        repositoryName,
                        principal,
                        cancellationToken);
                    if (repository is null)
                    {
                        return TypedResults.NotFound();
                    }

                    try
                    {
                        var archive = await GitRepositoryReader.CreateArchiveAsync(
                            repositoryService.GetRepositoryPath(repository.Name),
                            branch,
                            cancellationToken);
                        await archiveService.RecordRepositoryDownloadAsync(
                            repository.Id,
                            GetUserId(principal),
                            cancellationToken);
                        return TypedResults.File(
                            archive,
                            "application/zip",
                            $"{repository.Name}-{branch}.zip");
                    }
                    catch (ArgumentException)
                    {
                        return TypedResults.BadRequest();
                    }
                    catch (GitCommandException)
                    {
                        return TypedResults.NotFound();
                    }
                })
            .WithTags("Repositories")
            .RequireAuthorization();

        return endpoints;
    }

    private static RepositoryResponse ToResponse(GitRepository repository) =>
        new(
            repository.Id,
            repository.Name,
            repository.DisplayName,
            repository.OwnerUserId,
            repository.CreatedAt);

    private static RepositoryTemplateResponse ToTemplateResponse(
        GitRepository repository) =>
        new(
            repository.Id,
            repository.Name,
            repository.DisplayName,
            repository.OwnerUserId,
            repository.CreatedAt,
            repository.Purpose.ToString());

    private static Guid GetUserId(ClaimsPrincipal principal) =>
        Guid.Parse(
            principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException(
                "The user identifier is missing."));
}
