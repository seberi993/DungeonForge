FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/DungeonForge.Api/DungeonForge.Api.csproj src/DungeonForge.Api/
RUN dotnet restore src/DungeonForge.Api/DungeonForge.Api.csproj
COPY src/Shared/ src/Shared/
COPY src/DungeonForge.Api/ src/DungeonForge.Api/
RUN dotnet publish src/DungeonForge.Api/DungeonForge.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "DungeonForge.Api.dll"]
