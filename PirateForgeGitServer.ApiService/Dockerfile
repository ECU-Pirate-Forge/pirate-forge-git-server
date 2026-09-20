FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["PirateForgeGitServer.ApiService/PirateForgeGitServer.ApiService.csproj", "PirateForgeGitServer.ApiService/"]
COPY ["PirateForgeGitServer.Application/PirateForgeGitServer.Application.csproj", "PirateForgeGitServer.Application/"]
COPY ["PirateForgeGitServer.Contracts/PirateForgeGitServer.Contracts.csproj", "PirateForgeGitServer.Contracts/"]
COPY ["PirateForgeGitServer.Domain/PirateForgeGitServer.Domain.csproj", "PirateForgeGitServer.Domain/"]
COPY ["PirateForgeGitServer.Infrastructure/PirateForgeGitServer.Infrastructure.csproj", "PirateForgeGitServer.Infrastructure/"]
COPY ["PirateForgeGitServer.ServiceDefaults/PirateForgeGitServer.ServiceDefaults.csproj", "PirateForgeGitServer.ServiceDefaults/"]
RUN dotnet restore "PirateForgeGitServer.ApiService/PirateForgeGitServer.ApiService.csproj"

COPY . .
RUN dotnet publish "PirateForgeGitServer.ApiService/PirateForgeGitServer.ApiService.csproj" \
    --configuration "$BUILD_CONFIGURATION" \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS final
USER root
RUN apt-get update \
    && apt-get install --yes --no-install-recommends git ca-certificates \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "PirateForgeGitServer.ApiService.dll"]
