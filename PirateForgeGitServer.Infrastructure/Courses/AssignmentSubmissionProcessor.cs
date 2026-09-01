using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PirateForgeGitServer.Domain.Courses;
using PirateForgeGitServer.Domain.Git;
using PirateForgeGitServer.Infrastructure.Git;
using PirateForgeGitServer.Infrastructure.Persistence;
using PirateForgeGitServer.Infrastructure.Storage;

namespace PirateForgeGitServer.Infrastructure.Courses;

public sealed class AssignmentSubmissionProcessor(
    ILogger<AssignmentSubmissionProcessor> logger,
    IServiceScopeFactory scopeFactory,
    StoragePaths storagePaths,
    AssignmentSubmissionQueue queue,
    IOptions<AssignmentGradingOptions> gradingOptions)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessQueuedSubmissionAsync(item, stoppingToken);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "The grading pipeline failed for repository '{RepositoryName}'.",
                    item.RepositoryName);
            }
        }
    }

    private async Task ProcessQueuedSubmissionAsync(
        AssignmentSubmissionQueueItem item,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PirateForgeDbContext>();
        var repository = await dbContext.GitRepositories
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value =>
                    value.Name == item.RepositoryName
                    && value.Purpose == RepositoryPurpose.StudentAssignment,
                cancellationToken);
        if (repository is null)
        {
            return;
        }

        var assignmentRepository = await dbContext.AssignmentRepositories
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.RepositoryId == repository.Id,
                cancellationToken);
        if (assignmentRepository is null)
        {
            return;
        }

        var assignment = await dbContext.Assignments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.Id == assignmentRepository.AssignmentId,
                cancellationToken);
        if (assignment is null)
        {
            return;
        }

        if (!assignment.AutoGradeEnabled && !assignment.AskUnderstandingQuestions)
        {
            return;
        }

        var run = AssignmentSubmissionRun.Create(
            assignment.Id,
            assignmentRepository.StudentUserId,
            repository.Id,
            DateTimeOffset.UtcNow);

        dbContext.AssignmentSubmissionRuns.Add(run);
        await dbContext.SaveChangesAsync(cancellationToken);

        var repositoryPath = Path.Combine(
            storagePaths.RepositoriesPath,
            $"{repository.Name}.git");
        var workingDirectory = await CloneRepositoryAsync(
            repositoryPath,
            cancellationToken);

        try
        {
            var commitSha = (await GitProcessRunner.ExecuteTextAsync(
                    [
                        "-C",
                        repositoryPath,
                        "rev-parse",
                        "HEAD",
                    ],
                    cancellationToken))
                .Trim();
            run.Start(commitSha);
            await dbContext.SaveChangesAsync(cancellationToken);

            var gradeResult = assignment.AutoGradeEnabled
                ? await RunAutomaticGradeAsync(
                    assignment,
                    workingDirectory,
                    cancellationToken)
                : new GradeResult(
                    assignment.MaxScore,
                    "Automatic grading is disabled for this assignment.",
                    "Automatic grading is disabled.");

            var reviewQuestions = new List<AssignmentReviewQuestion>();
            string reviewSummary = string.Empty;
            if (assignment.AskUnderstandingQuestions)
            {
                var reviewResult = await GenerateReviewQuestionsAsync(
                    assignment,
                    workingDirectory,
                    run,
                    cancellationToken);
                reviewQuestions = reviewResult.Questions;
                reviewSummary = reviewResult.Summary;
            }

            foreach (var question in reviewQuestions)
            {
                run.AddQuestion(question);
            }

            run.Complete(
                gradeResult.Score,
                assignment.MaxScore,
                gradeResult.Summary,
                reviewSummary,
                gradeResult.Log);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "An error occurred while grading repository '{RepositoryName}'.",
                repository.Name);
            run.Fail(exception.Message, exception.ToString());
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            if (Directory.Exists(workingDirectory))
            {
                Directory.Delete(workingDirectory, recursive: true);
            }
        }
    }

    private static async Task<string> CloneRepositoryAsync(
        string repositoryPath,
        CancellationToken cancellationToken)
    {
        var rootDirectory = Path.Combine(
            Path.GetTempPath(),
            "pirateforge-grading",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootDirectory);
        var workingDirectory = Path.Combine(rootDirectory, "workspace");
        await GitProcessRunner.ExecuteTextAsync(
            [
                "clone",
                "--quiet",
                repositoryPath,
                workingDirectory,
            ],
            cancellationToken);
        return workingDirectory;
    }

    private async Task<GradeResult> RunAutomaticGradeAsync(
        Assignment assignment,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(workingDirectory))
        {
            return new GradeResult(
                0,
                "The repository working tree could not be created for grading.",
                "Directory does not exist.");
        }

        if (!string.IsNullOrWhiteSpace(assignment.GradingCommand))
        {
            try
            {
                var commandParts = SplitCommandLine(assignment.GradingCommand);
                if (commandParts.Length == 0)
                {
                    return new GradeResult(
                        0,
                        "The assignment grading command is empty.",
                        "The configured grading command was blank.");
                }

                var commandResult = await ExecuteCommandAsync(
                    commandParts[0],
                    commandParts.Skip(1),
                    workingDirectory,
                    cancellationToken);
                var passed = commandResult.ExitCode == 0;
                return new GradeResult(
                    passed ? assignment.MaxScore : Math.Max(0, assignment.MaxScore / 2),
                    passed
                        ? "The assignment-specific grading command passed."
                        : "The assignment-specific grading command failed.",
                    commandResult.Output);
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "The assignment-specific grading command could not be executed for assignment '{AssignmentId}'.",
                    assignment.Id);
                return new GradeResult(
                    0,
                    "The assignment-specific grading command could not be executed.",
                    exception.ToString());
            }
        }

        var solutionFiles = Directory
            .EnumerateFiles(workingDirectory, "*.sln", SearchOption.AllDirectories)
            .OrderBy(file => file)
            .ToArray();
        var projectFiles = Directory
            .EnumerateFiles(workingDirectory, "*.csproj", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(
                workingDirectory,
                "*.fsproj",
                SearchOption.AllDirectories))
            .OrderBy(file => file)
            .ToArray();
        var testProjectFiles = projectFiles
            .Where(file => Path.GetFileNameWithoutExtension(file)
                .Contains("Test", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var packageJsonFile = Path.Combine(workingDirectory, "package.json");
        var pythonFiles = Directory
            .EnumerateFiles(workingDirectory, "*.py", SearchOption.AllDirectories)
            .ToArray();

        ProcessResult result;
        if (solutionFiles.Length > 0)
        {
            var target = solutionFiles[0];
            var arguments = testProjectFiles.Length > 0
                ? new[] { "test", target, "--nologo" }
                : new[] { "build", target, "--nologo" };
            result = await ExecuteCommandAsync(
                "dotnet",
                arguments,
                workingDirectory,
                cancellationToken);
            var passed = result.ExitCode == 0;
            return new GradeResult(
                passed ? assignment.MaxScore : Math.Max(0, assignment.MaxScore / 2),
                passed
                    ? "The repository passed the automated build validation."
                    : "The repository failed the automated build validation.",
                result.Output);
        }

        if (projectFiles.Length > 0)
        {
            var target = testProjectFiles.Length > 0
                ? testProjectFiles[0]
                : projectFiles[0];
            var arguments = testProjectFiles.Length > 0
                ? new[] { "test", target, "--nologo" }
                : new[] { "build", target, "--nologo" };
            result = await ExecuteCommandAsync(
                "dotnet",
                arguments,
                workingDirectory,
                cancellationToken);
            var passed = result.ExitCode == 0;
            return new GradeResult(
                passed ? assignment.MaxScore : Math.Max(0, assignment.MaxScore / 2),
                passed
                    ? "The project passed the automated build validation."
                    : "The project failed the automated build validation.",
                result.Output);
        }

        if (File.Exists(packageJsonFile))
        {
            result = await ExecuteCommandAsync(
                "npm",
                ["test", "--", "--ci"],
                workingDirectory,
                cancellationToken);
            var passed = result.ExitCode == 0;
            return new GradeResult(
                passed ? assignment.MaxScore : Math.Max(0, assignment.MaxScore / 2),
                passed
                    ? "The Node.js project passed the automated test script."
                    : "The Node.js project failed its automated test script.",
                result.Output);
        }

        if (pythonFiles.Length > 0)
        {
            result = await ExecuteCommandAsync(
                "python",
                ["-m", "pytest", "-q"],
                workingDirectory,
                cancellationToken);
            if (result.ExitCode != 0)
            {
                result = await ExecuteCommandAsync(
                    "python",
                    ["-m", "compileall", "."],
                    workingDirectory,
                    cancellationToken);
            }

            var passed = result.ExitCode == 0;
            return new GradeResult(
                passed ? assignment.MaxScore : Math.Max(0, assignment.MaxScore / 2),
                passed
                    ? "The Python project passed the validation step."
                    : "The Python project failed the validation step.",
                result.Output);
        }

        return new GradeResult(
            0,
            "No supported build or test automation was found in the repository.",
            "No .sln, .csproj, package.json, or Python test targets were detected.");
    }

    private async Task<ReviewResult> GenerateReviewQuestionsAsync(
        Assignment assignment,
        string workingDirectory,
        AssignmentSubmissionRun run,
        CancellationToken cancellationToken)
    {
        var interestingFiles = GetInterestingFiles(workingDirectory);
        var prompt = BuildReviewPrompt(assignment, interestingFiles, workingDirectory);
        var generatedQuestions = await TryGenerateLocalQuestionsAsync(
            prompt,
            assignment.LocalAiModel,
            cancellationToken);
        if (generatedQuestions.Count == 0)
        {
            generatedQuestions = BuildHeuristicQuestions(interestingFiles);
        }

        if (generatedQuestions.Count == 0)
        {
            return new ReviewResult(
                "No code-understanding questions were generated for this submission.",
                []);
        }

        var questions = generatedQuestions
            .Take(gradingOptions.Value.DefaultQuestionCount)
            .Select((question, index) =>
            {
                var focusArea = interestingFiles.Count > index
                    ? interestingFiles[index]
                    : "Core implementation";
                return AssignmentReviewQuestion.Create(run.Id, question, focusArea);
            })
            .ToList();

        return new ReviewResult(
            $"Generated {questions.Count} code-understanding questions for the student.",
            questions);
    }

    private async Task<IReadOnlyList<string>> TryGenerateLocalQuestionsAsync(
        string prompt,
        string? modelOverride,
        CancellationToken cancellationToken)
    {
        var options = gradingOptions.Value;
        if (!options.Enabled
            && string.IsNullOrWhiteSpace(modelOverride))
        {
            return [];
        }

        try
        {
            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(20),
            };
            var effectiveModel = string.IsNullOrWhiteSpace(modelOverride)
                ? options.LocalModel
                : modelOverride;
            if (string.IsNullOrWhiteSpace(options.LocalEndpoint)
                && string.IsNullOrWhiteSpace(modelOverride))
            {
                return [];
            }

            var payload = new
            {
                model = string.IsNullOrWhiteSpace(effectiveModel)
                    ? "llama3.2"
                    : effectiveModel,
                prompt,
                stream = false,
                options = new { temperature = 0.2 },
            };

            var endpoint = string.IsNullOrWhiteSpace(options.LocalEndpoint)
                ? "http://localhost:11434"
                : options.LocalEndpoint;
            var response = await client.PostAsJsonAsync(
                $"{endpoint.TrimEnd('/')}/api/generate",
                payload,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(content))
            {
                return [];
            }

            using var document = JsonDocument.Parse(content);
            var responseText = document.RootElement.TryGetProperty(
                "response",
                out var responseValue)
                ? responseValue.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(responseText))
            {
                return [];
            }

            return SplitQuestions(responseText);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "The local grading model was unavailable, so the fallback prompt questions were used.");
            return [];
        }
    }

    private static IReadOnlyList<string> BuildHeuristicQuestions(IReadOnlyList<string> interestingFiles)
    {
        var questions = new List<string>
        {
            "Which file or module is primarily responsible for the main behavior in this submission?",
            "What is the most important path through the code when a user or system interacts with this project?",
            "Where would you add or change logic to fix a bug, and why would that be the right place?",
            "How does the code validate or handle errors, edge cases, or invalid input in the critical workflow?",
        };

        if (interestingFiles.Count > 0)
        {
            var focus = interestingFiles[0];
            questions[0] =
                $"Looking at {focus}, what is the primary responsibility of this module or file in the project?";
        }

        return questions;
    }

    private static string[] SplitCommandLine(string command)
    {
        var arguments = new List<string>();
        var current = new StringBuilder();
        var inSingleQuotes = false;
        var inDoubleQuotes = false;

        for (var index = 0; index < command.Length; index++)
        {
            var character = command[index];
            switch (character)
            {
                case '\'':
                    if (!inDoubleQuotes)
                    {
                        inSingleQuotes = !inSingleQuotes;
                    }
                    else
                    {
                        current.Append(character);
                    }
                    break;
                case '"':
                    if (!inSingleQuotes)
                    {
                        inDoubleQuotes = !inDoubleQuotes;
                    }
                    else
                    {
                        current.Append(character);
                    }
                    break;
                case ' ' when !inSingleQuotes && !inDoubleQuotes:
                    if (current.Length > 0)
                    {
                        arguments.Add(current.ToString());
                        current.Clear();
                    }
                    break;
                case '\t' when !inSingleQuotes && !inDoubleQuotes:
                    if (current.Length > 0)
                    {
                        arguments.Add(current.ToString());
                        current.Clear();
                    }
                    break;
                case '\r' when !inSingleQuotes && !inDoubleQuotes:
                    if (current.Length > 0)
                    {
                        arguments.Add(current.ToString());
                        current.Clear();
                    }
                    break;
                case '\n' when !inSingleQuotes && !inDoubleQuotes:
                    if (current.Length > 0)
                    {
                        arguments.Add(current.ToString());
                        current.Clear();
                    }
                    break;
                default:
                    current.Append(character);
                    break;
            }
        }

        if (current.Length > 0)
        {
            arguments.Add(current.ToString());
        }

        return arguments.ToArray();
    }

    private static IReadOnlyList<string> SplitQuestions(string source)
    {
        var segments = source
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(segment => segment.Trim('-', '*', ' ', '\t'))
            .Where(segment => segment.Length > 10)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToArray();
        return segments;
    }

    private static string BuildReviewPrompt(
        Assignment assignment,
        IReadOnlyList<string> interestingFiles,
        string workingDirectory)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You are an instructor reviewing a student submission.");
        builder.AppendLine("Generate up to three short but specific questions to evaluate the student's understanding of the work they submitted.");
        builder.AppendLine("Use only the repository state and the assignment context described below.");
        builder.AppendLine();
        builder.AppendLine($"Assignment title: {assignment.Title}");
        builder.AppendLine($"Description: {assignment.Description}");
        if (!string.IsNullOrWhiteSpace(assignment.ReviewPrompt))
        {
            builder.AppendLine($"Instructor guidance: {assignment.ReviewPrompt}");
        }
        if (!string.IsNullOrWhiteSpace(assignment.GradingCommand))
        {
            builder.AppendLine($"Grading command: {assignment.GradingCommand}");
        }

        if (interestingFiles.Count > 0)
        {
            builder.AppendLine("Relevant files and snippets:");
            foreach (var file in interestingFiles.Take(5))
            {
                var snippet = ReadFileSnippet(workingDirectory, file);
                builder.AppendLine($"- {file}");
                if (!string.IsNullOrWhiteSpace(snippet))
                {
                    builder.AppendLine("```text");
                    builder.AppendLine(snippet);
                    builder.AppendLine("```");
                }
            }
        }

        builder.AppendLine();
        builder.AppendLine("Return only the questions, one per line, without numbering or explanations.");
        return builder.ToString();
    }

    private static string ReadFileSnippet(string workingDirectory, string relativePath)
    {
        var filePath = Path.Combine(
            workingDirectory,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(filePath))
        {
            return string.Empty;
        }

        var lines = File.ReadAllLines(filePath);
        var snippet = string.Join(
            Environment.NewLine,
            lines.Take(40));
        if (snippet.Length > 1200)
        {
            snippet = snippet.Substring(0, 1200) + "\n...";
        }

        return snippet.Trim();
    }

    private static IReadOnlyList<string> GetInterestingFiles(string workingDirectory)
    {
        var candidates = Directory
            .EnumerateFiles(
                workingDirectory,
                "*.*",
                SearchOption.AllDirectories)
            .Where(file =>
                !file.Contains("bin", StringComparison.OrdinalIgnoreCase)
                && !file.Contains("obj", StringComparison.OrdinalIgnoreCase)
                && !file.Contains(".git", StringComparison.OrdinalIgnoreCase))
            .Where(file =>
                file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".py", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .Take(25)
            .Select(file => Path.GetRelativePath(workingDirectory, file).Replace('\\', '/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return candidates.Length > 0 ? candidates : ["Core implementation"]; 
    }

    private static async Task<ProcessResult> ExecuteCommandAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var output = (await standardOutputTask) + (await standardErrorTask);
        return new ProcessResult(process.ExitCode, output.Trim());
    }

    private sealed record GradeResult(int Score, string Summary, string Log);

    private sealed record ReviewResult(string Summary, List<AssignmentReviewQuestion> Questions);

    private sealed record ProcessResult(int ExitCode, string Output);
}
