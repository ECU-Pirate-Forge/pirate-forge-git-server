using System.Diagnostics;

namespace PirateForgeGitServer.Infrastructure.Git;

public sealed class GitProcessRunner
{
    public static async Task InitializeBareRepositoryAsync(
        string repositoryPath,
        CancellationToken cancellationToken)
    {
        var startInfo = CreateStartInfo();
        startInfo.ArgumentList.Add("init");
        startInfo.ArgumentList.Add("--bare");
        startInfo.ArgumentList.Add("--initial-branch=main");
        startInfo.ArgumentList.Add(repositoryPath);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new GitCommandException("Git did not start.");
        }

        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            throw new GitCommandException(
                $"Git could not initialize the repository: {await standardError}");
        }
    }

    public static async Task CloneBareRepositoryAsync(
        string sourceRepositoryPath,
        string destinationRepositoryPath,
        CancellationToken cancellationToken)
    {
        var startInfo = CreateStartInfo();
        startInfo.ArgumentList.Add("clone");
        startInfo.ArgumentList.Add("--bare");
        startInfo.ArgumentList.Add("--no-local");
        startInfo.ArgumentList.Add(sourceRepositoryPath);
        startInfo.ArgumentList.Add(destinationRepositoryPath);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new GitCommandException("Git did not start.");
        }

        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            throw new GitCommandException(
                $"Git could not clone the repository template: {await standardError}");
        }
    }

    public static async Task ExecuteServiceAsync(
        GitServiceType service,
        string repositoryPath,
        bool advertiseRefs,
        Stream input,
        Stream output,
        CancellationToken cancellationToken)
    {
        var startInfo = CreateStartInfo();
        startInfo.ArgumentList.Add(
            service is GitServiceType.UploadPack
                ? "upload-pack"
                : "receive-pack");
        startInfo.ArgumentList.Add("--stateless-rpc");
        if (advertiseRefs)
        {
            startInfo.ArgumentList.Add("--advertise-refs");
        }

        startInfo.ArgumentList.Add(repositoryPath);
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new GitCommandException("Git did not start.");
        }

        try
        {
            var copyInput = CopyInputAsync(
                input,
                process.StandardInput.BaseStream,
                cancellationToken);
            var copyOutput = process.StandardOutput.BaseStream.CopyToAsync(
                output,
                cancellationToken);
            var standardError = process.StandardError.ReadToEndAsync(cancellationToken);

            await Task.WhenAll(
                copyInput,
                copyOutput,
                standardError,
                process.WaitForExitAsync(cancellationToken));
            if (process.ExitCode != 0)
            {
                throw new GitCommandException(
                    $"Git service execution failed: {await standardError}");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }
    }

    public static async Task<string> ExecuteTextAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = CreateStartInfo();
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new GitCommandException("Git did not start.");
        }

        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await Task.WhenAll(
            output,
            error,
            process.WaitForExitAsync(cancellationToken));
        if (process.ExitCode != 0)
        {
            throw new GitCommandException($"Git command failed: {await error}");
        }

        return await output;
    }

    public static async Task<byte[]> ExecuteBytesAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = CreateStartInfo();
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new GitCommandException("Git did not start.");
        }

        await using var output = new MemoryStream();
        var copyOutput = process.StandardOutput.BaseStream.CopyToAsync(
            output,
            cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await Task.WhenAll(
            copyOutput,
            error,
            process.WaitForExitAsync(cancellationToken));
        if (process.ExitCode != 0)
        {
            throw new GitCommandException($"Git command failed: {await error}");
        }

        return output.ToArray();
    }

    private static async Task CopyInputAsync(
        Stream input,
        Stream processInput,
        CancellationToken cancellationToken)
    {
        try
        {
            await input.CopyToAsync(processInput, cancellationToken);
        }
        finally
        {
            await processInput.DisposeAsync();
        }
    }

    internal static ProcessStartInfo CreateStartInfo() =>
        new()
        {
            FileName = "git",
            RedirectStandardError = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
}
