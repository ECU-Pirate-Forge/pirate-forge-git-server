using PirateForgeGitServer.Domain.Identity;

namespace PirateForgeGitServer.Application.Identity;

public sealed record GeneratedPersonalAccessToken(
    string Token,
    PersonalAccessToken Record);
