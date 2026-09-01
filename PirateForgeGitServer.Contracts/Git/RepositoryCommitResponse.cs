namespace PirateForgeGitServer.Contracts.Git;

public sealed record RepositoryCommitResponse(
    string Id,
    string Subject,
    string AuthorName,
    string AuthorEmail,
    DateTimeOffset AuthoredAt);
