# Robust Dockerfile for .NET 10 with timeout handling
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Configure NuGet with timeout and source
RUN dotnet nuget update source nuget.org --source https://api.nuget.org/v3/index.json
ENV DOTNET_HTTP_TIMEOUT=300
ENV NUGET_HTTP_CACHE_PATH=/tmp/nuget-http-cache

# Copy project files first for better layer caching
COPY *.csproj ./
COPY *.sln ./

# Restore with explicit timeout and retry
RUN timeout 600 dotnet restore --verbosity minimal --disable-parallel || \
    timeout 600 dotnet restore --verbosity minimal --no-dependencies || \
    dotnet restore --verbosity minimal

# Copy source and build
COPY . ./
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Linky.dll"]