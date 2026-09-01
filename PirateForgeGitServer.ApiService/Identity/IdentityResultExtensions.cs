using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace PirateForgeGitServer.ApiService.Identity;

internal static class IdentityResultExtensions
{
    public static ValidationProblem ToValidationProblem(this IdentityResult result) =>
        TypedResults.ValidationProblem(
            result.Errors
                .GroupBy(error => error.Code, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .Select(error => error.Description)
                        .ToArray(),
                    StringComparer.Ordinal));
}
