using System.IO.Compression;
using Microsoft.AspNetCore.Mvc;
using PirateForgeGitServer.Infrastructure.Courses;
using PirateForgeGitServer.Infrastructure.Git;

namespace PirateForgeGitServer.ApiService.Git;

internal static class GitSmartHttpEndpoints
{
    public static IEndpointRouteBuilder MapGitSmartHttpEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/{repositoryName}.git/info/refs",
                async Task<IResult> (
                    string repositoryName,
                    string service,
                    HttpContext context,
                    GitRepositoryAccessService accessService,
                    GitRepositoryService repositoryService) =>
                {
                    if (!TryGetService(service, out var serviceType))
                    {
                        return TypedResults.BadRequest();
                    }

                    var repository = await accessService.FindAccessibleAsync(
                        repositoryName,
                        context.User,
                        context.RequestAborted,
                        write: serviceType == GitServiceType.ReceivePack);
                    if (repository is null)
                    {
                        return TypedResults.NotFound();
                    }

                    context.Response.ContentType =
                        $"application/x-{service}-advertisement";
                    context.Response.Headers.CacheControl = "no-cache";
                    await GitPacketLine.WriteServiceHeaderAsync(
                        service,
                        context.Response.Body,
                        context.RequestAborted);
                    await GitProcessRunner.ExecuteServiceAsync(
                        serviceType,
                        repositoryService.GetRepositoryPath(repository.Name),
                        advertiseRefs: true,
                        Stream.Null,
                        context.Response.Body,
                        context.RequestAborted);
                    return TypedResults.Empty;
                })
            .WithTags("Git")
            .RequireAuthorization();

        MapServiceEndpoint(
            endpoints,
            "git-upload-pack",
            GitServiceType.UploadPack);
        MapServiceEndpoint(
            endpoints,
            "git-receive-pack",
            GitServiceType.ReceivePack);

        return endpoints;
    }

    private static void MapServiceEndpoint(
        IEndpointRouteBuilder endpoints,
        string serviceName,
        GitServiceType serviceType)
    {
        endpoints.MapPost(
                $"/{{repositoryName}}.git/{serviceName}",
                async Task<IResult> (
                    string repositoryName,
                    HttpContext context,
                    GitRepositoryAccessService accessService,
                    GitRepositoryService repositoryService,
                    AssignmentSubmissionQueue assignmentSubmissionQueue) =>
                {
                    var repository = await accessService.FindAccessibleAsync(
                        repositoryName,
                        context.User,
                        context.RequestAborted,
                        write: serviceType == GitServiceType.ReceivePack);
                    if (repository is null)
                    {
                        return TypedResults.NotFound();
                    }

                    context.Response.ContentType =
                        $"application/x-{serviceName}-result";
                    context.Response.Headers.CacheControl = "no-cache";
                    Stream input = context.Request.Body;
                    GZipStream? decompressedInput = null;
                    if (string.Equals(
                            context.Request.Headers.ContentEncoding,
                            "gzip",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        decompressedInput = new GZipStream(
                            input,
                            CompressionMode.Decompress,
                            leaveOpen: true);
                        input = decompressedInput;
                    }

                    try
                    {
                        await GitProcessRunner.ExecuteServiceAsync(
                            serviceType,
                            repositoryService.GetRepositoryPath(repository.Name),
                            advertiseRefs: false,
                            input,
                            context.Response.Body,
                            context.RequestAborted);
                        if (serviceType == GitServiceType.ReceivePack)
                        {
                            await assignmentSubmissionQueue.QueueAsync(
                                repository.Name,
                                context.RequestAborted);
                        }
                    }
                    finally
                    {
                        if (decompressedInput is not null)
                        {
                            await decompressedInput.DisposeAsync();
                        }
                    }

                    return TypedResults.Empty;
                })
            .WithTags("Git")
            .WithMetadata(new DisableRequestSizeLimitAttribute())
            .RequireAuthorization();
    }

    private static bool TryGetService(
        string serviceName,
        out GitServiceType serviceType)
    {
        serviceType = serviceName switch
        {
            "git-upload-pack" => GitServiceType.UploadPack,
            "git-receive-pack" => GitServiceType.ReceivePack,
            _ => default,
        };
        return serviceName is "git-upload-pack" or "git-receive-pack";
    }
}
