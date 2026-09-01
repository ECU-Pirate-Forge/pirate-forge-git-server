using Microsoft.EntityFrameworkCore;
using PirateForgeGitServer.Domain.Git;
using PirateForgeGitServer.Infrastructure.Persistence;
using PirateForgeGitServer.Infrastructure.Storage;

namespace PirateForgeGitServer.Infrastructure.Git;

public sealed class GitRepositoryService(
    PirateForgeDbContext dbContext,
    StoragePaths storagePaths,
    TimeProvider timeProvider)
{
    private static readonly SemaphoreSlim CreationLock = new(1, 1);

    public async Task<GitRepository?> CreateAsync(
        string name,
        string displayName,
        Guid ownerUserId,
        CancellationToken cancellationToken,
        RepositoryPurpose purpose = RepositoryPurpose.Standard)
    {
        var repository = GitRepository.Create(
            name,
            displayName,
            ownerUserId,
            timeProvider.GetUtcNow(),
            purpose);
        var repositoryPath = GetRepositoryPath(repository.Name);

        await CreationLock.WaitAsync(cancellationToken);
        try
        {
            if (await dbContext.GitRepositories.AnyAsync(
                    candidate => candidate.Name == repository.Name,
                    cancellationToken)
                || Directory.Exists(repositoryPath))
            {
                return null;
            }

            await GitProcessRunner.InitializeBareRepositoryAsync(
                repositoryPath,
                cancellationToken);
            dbContext.GitRepositories.Add(repository);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                if (Directory.Exists(repositoryPath))
                {
                    Directory.Delete(repositoryPath, recursive: true);
                }

                throw;
            }

            return repository;
        }
        finally
        {
            CreationLock.Release();
        }
    }

    public string GetRepositoryPath(string repositoryName) =>
        Path.Combine(storagePaths.RepositoriesPath, $"{repositoryName}.git");
}
