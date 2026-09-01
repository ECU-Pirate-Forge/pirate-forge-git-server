using System.Threading.Channels;

namespace PirateForgeGitServer.Infrastructure.Courses;

public sealed record AssignmentSubmissionQueueItem(string RepositoryName);

public sealed class AssignmentSubmissionQueue
{
    private readonly Channel<AssignmentSubmissionQueueItem> _channel =
        Channel.CreateUnbounded<AssignmentSubmissionQueueItem>();

    public ValueTask QueueAsync(
        string repositoryName,
        CancellationToken cancellationToken = default)
    {
        return _channel.Writer.WriteAsync(
            new AssignmentSubmissionQueueItem(repositoryName),
            cancellationToken);
    }

    public IAsyncEnumerable<AssignmentSubmissionQueueItem> ReadAllAsync(
        CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}
