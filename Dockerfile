FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
# Restore layer: project files only, so source edits don't invalidate the package cache.
COPY src/DungeonForge.Shared/DungeonForge.Shared.csproj src/DungeonForge.Shared/
COPY src/DungeonForge.Domain/DungeonForge.Domain.csproj src/DungeonForge.Domain/
COPY src/DungeonForge.Application/DungeonForge.Application.csproj src/DungeonForge.Application/
COPY src/DungeonForge.Infrastructure/DungeonForge.Infrastructure.csproj src/DungeonForge.Infrastructure/
COPY src/DungeonForge.Api/DungeonForge.Api.csproj src/DungeonForge.Api/
RUN dotnet restore src/DungeonForge.Api/DungeonForge.Api.csproj
COPY src/DungeonForge.Shared/ src/DungeonForge.Shared/
COPY src/DungeonForge.Domain/ src/DungeonForge.Domain/
COPY src/DungeonForge.Application/ src/DungeonForge.Application/
COPY src/DungeonForge.Infrastructure/ src/DungeonForge.Infrastructure/
COPY src/DungeonForge.Api/ src/DungeonForge.Api/
RUN dotnet publish src/DungeonForge.Api/DungeonForge.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "DungeonForge.Api.dll"]
