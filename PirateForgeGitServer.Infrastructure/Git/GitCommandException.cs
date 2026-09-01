namespace PirateForgeGitServer.Infrastructure.Git;

public sealed class GitCommandException(string message) : Exception(message);
