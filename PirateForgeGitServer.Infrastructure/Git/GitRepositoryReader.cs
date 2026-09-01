using System.Globalization;
using System.Text;

namespace PirateForgeGitServer.Infrastructure.Git;

public static class GitRepositoryReader
{
    public const long MaximumViewableFileSize = 1024 * 1024;

    public static async Task<IReadOnlyList<GitBranchInfo>> ListBranchesAsync(
        string repositoryPath,
        CancellationToken cancellationToken)
    {
        var output = await GitProcessRunner.ExecuteTextAsync(
            [
                $"--git-dir={repositoryPath}",
                "for-each-ref",
                "--format=%(refname:short)%00%(objectname)%00%(committerdate:iso-strict)",
                "refs/heads",
            ],
            cancellationToken);

        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseBranch)
            .OrderBy(branch => branch.Name, StringComparer.Ordinal)
            .ToArray();
    }

    public static async Task<IReadOnlyList<GitTreeEntryInfo>> ListTreeAsync(
        string repositoryPath,
        string branch,
        string? path,
        CancellationToken cancellationToken)
    {
        var treeish = BuildTreeish(branch, path);
        var output = await GitProcessRunner.ExecuteTextAsync(
            [
                $"--git-dir={repositoryPath}",
                "ls-tree",
                "-z",
                "-l",
                treeish,
            ],
            cancellationToken);

        return output.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(entry => ParseTreeEntry(entry, NormalizePath(path)))
            .OrderBy(entry => entry.Kind is "directory" ? 0 : 1)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string NormalizeBranch(string branch)
    {
        var normalized = branch.Trim();
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.StartsWith('-')
            || normalized.Contains("..", StringComparison.Ordinal)
            || normalized.Contains("//", StringComparison.Ordinal)
            || normalized.Contains("@{", StringComparison.Ordinal)
            || normalized.IndexOfAny(['\\', '~', '^', ':', '?', '*', '[']) >= 0
            || normalized.EndsWith('/')
            || normalized.EndsWith('.')
            || normalized.EndsWith(".lock", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The branch name is invalid.", nameof(branch));
        }

        return normalized;
    }

    public static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var normalized = path.Replace('\\', '/').Trim('/');
        if (normalized.Split('/').Any(segment =>
                segment is "" or "." or ".."))
        {
            throw new ArgumentException("The repository path is invalid.", nameof(path));
        }

        return normalized;
    }

    public static async Task<GitFileInfo> ReadFileAsync(
        string repositoryPath,
        string branch,
        string path,
        CancellationToken cancellationToken)
    {
        var normalizedPath = NormalizePath(path);
        if (string.IsNullOrEmpty(normalizedPath))
        {
            throw new ArgumentException("A file path is required.", nameof(path));
        }

        var objectName = BuildTreeish(branch, normalizedPath);
        var sizeOutput = await GitProcessRunner.ExecuteTextAsync(
            [
                $"--git-dir={repositoryPath}",
                "cat-file",
                "-s",
                objectName,
            ],
            cancellationToken);
        if (!long.TryParse(
                sizeOutput.Trim(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var size)
            || size > MaximumViewableFileSize)
        {
            throw new GitFileNotViewableException(
                "The file is too large to display.");
        }

        var bytes = await GitProcessRunner.ExecuteBytesAsync(
            [
                $"--git-dir={repositoryPath}",
                "cat-file",
                "blob",
                objectName,
            ],
            cancellationToken);
        if (bytes.Contains((byte)0))
        {
            throw new GitFileNotViewableException(
                "Binary files cannot be displayed.");
        }

        try
        {
            var content = new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true).GetString(bytes);
            return new GitFileInfo(
                normalizedPath,
                GetLanguage(normalizedPath),
                content,
                size);
        }
        catch (DecoderFallbackException exception)
        {
            throw new GitFileNotViewableException(
                "The file is not valid UTF-8.",
                exception);
        }
    }

    public static async Task<IReadOnlyList<GitCommitInfo>> ListCommitsAsync(
        string repositoryPath,
        string branch,
        CancellationToken cancellationToken) =>
        await ReadCommitsAsync(
            repositoryPath,
            branch,
            maximumCount: 100,
            cancellationToken);

    public static async Task<GitCommitInfo?> ReadLatestCommitAsync(
        string repositoryPath,
        string branch,
        CancellationToken cancellationToken)
    {
        var commits = await ReadCommitsAsync(
            repositoryPath,
            branch,
            maximumCount: 1,
            cancellationToken);
        return commits.Count == 0 ? null : commits[0];
    }

    private static async Task<IReadOnlyList<GitCommitInfo>> ReadCommitsAsync(
        string repositoryPath,
        string branch,
        int maximumCount,
        CancellationToken cancellationToken)
    {
        var normalizedBranch = NormalizeBranch(branch);
        var output = await GitProcessRunner.ExecuteTextAsync(
            [
                $"--git-dir={repositoryPath}",
                "log",
                "-z",
                $"--max-count={maximumCount}",
                "--format=%H%x00%s%x00%an%x00%ae%x00%aI",
                $"refs/heads/{normalizedBranch}",
            ],
            cancellationToken);
        var fields = output.Split(
            '\0',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (fields.Length % 5 != 0)
        {
            throw new GitCommandException("Git returned an invalid commit record.");
        }

        var commits = new List<GitCommitInfo>(fields.Length / 5);
        for (var index = 0; index < fields.Length; index += 5)
        {
            if (!DateTimeOffset.TryParse(
                    fields[index + 4],
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var authoredAt))
            {
                throw new GitCommandException(
                    "Git returned an invalid commit date.");
            }

            commits.Add(new GitCommitInfo(
                fields[index],
                fields[index + 1],
                fields[index + 2],
                fields[index + 3],
                authoredAt));
        }

        return commits;
    }

    public static Task<byte[]> CreateArchiveAsync(
        string repositoryPath,
        string branch,
        CancellationToken cancellationToken)
    {
        var normalizedBranch = NormalizeBranch(branch);
        var treeish = LooksLikeCommitish(normalizedBranch)
            ? normalizedBranch
            : $"refs/heads/{normalizedBranch}";
        return GitProcessRunner.ExecuteBytesAsync(
            [
                $"--git-dir={repositoryPath}",
                "archive",
                "--format=zip",
                treeish,
            ],
            cancellationToken);
    }

    private static string BuildTreeish(string branch, string? path)
    {
        var normalizedBranch = NormalizeBranch(branch);
        var normalizedPath = NormalizePath(path);
        return string.IsNullOrEmpty(normalizedPath)
            ? $"refs/heads/{normalizedBranch}"
            : $"refs/heads/{normalizedBranch}:{normalizedPath}";
    }

    private static bool LooksLikeCommitish(string value)
    {
        if (value.Length is < 7 or > 40)
        {
            return false;
        }

        return value.All(Uri.IsHexDigit);
    }

    private static string GetLanguage(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".cs" => "csharp",
            ".json" => "json",
            ".md" => "markdown",
            ".razor" => "razor",
            ".xml" or ".csproj" or ".props" or ".targets" => "xml",
            ".yml" or ".yaml" => "yaml",
            _ => "text",
        };

    private static GitBranchInfo ParseBranch(string line)
    {
        var fields = line.TrimEnd('\r').Split('\0');
        if (fields.Length != 3
            || !DateTimeOffset.TryParse(
                fields[2],
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var committedAt))
        {
            throw new GitCommandException("Git returned an invalid branch record.");
        }

        return new GitBranchInfo(fields[0], fields[1], committedAt);
    }

    private static GitTreeEntryInfo ParseTreeEntry(
        string record,
        string parentPath)
    {
        var separator = record.IndexOf('\t');
        if (separator < 0)
        {
            throw new GitCommandException("Git returned an invalid tree record.");
        }

        var metadata = record[..separator]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var name = record[(separator + 1)..];
        if (metadata.Length != 4)
        {
            throw new GitCommandException("Git returned an invalid tree record.");
        }

        var kind = metadata[1] == "tree" ? "directory" : "file";
        long? size = long.TryParse(
            metadata[3],
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var parsedSize)
                ? parsedSize
                : null;
        var fullPath = string.IsNullOrEmpty(parentPath)
            ? name
            : $"{parentPath}/{name}";
        return new GitTreeEntryInfo(name, fullPath, kind, size);
    }
}

public sealed record GitBranchInfo(
    string Name,
    string CommitId,
    DateTimeOffset CommittedAt);

public sealed record GitTreeEntryInfo(
    string Name,
    string Path,
    string Kind,
    long? Size);

public sealed record GitFileInfo(
    string Path,
    string Language,
    string Content,
    long Size);

public sealed record GitCommitInfo(
    string Id,
    string Subject,
    string AuthorName,
    string AuthorEmail,
    DateTimeOffset AuthoredAt);

public sealed class GitFileNotViewableException : Exception
{
    public GitFileNotViewableException(string message)
        : base(message)
    {
    }

    public GitFileNotViewableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
