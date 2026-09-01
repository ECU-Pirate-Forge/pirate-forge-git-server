using System.Globalization;
using System.Text;

namespace PirateForgeGitServer.Infrastructure.Git;

public static class GitPacketLine
{
    public static async Task WriteServiceHeaderAsync(
        string serviceName,
        Stream output,
        CancellationToken cancellationToken)
    {
        var payload = Encoding.ASCII.GetBytes($"# service={serviceName}\n");
        var length = (payload.Length + 4).ToString("x4", CultureInfo.InvariantCulture);
        await output.WriteAsync(
            Encoding.ASCII.GetBytes(length),
            cancellationToken);
        await output.WriteAsync(payload, cancellationToken);
        await output.WriteAsync("0000"u8.ToArray(), cancellationToken);
    }
}
