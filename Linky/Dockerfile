# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj from Linky/ subdirectory (build context is repo root)
COPY Linky/Linky.csproj ./
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore "Linky.csproj" -r linux-x64 --disable-parallel --verbosity normal

# Copy project source from Linky/ subdirectory
COPY Linky/ ./

# Build and publish with cache mount
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish "Linky.csproj" -c Release -o /app/publish \
    --no-restore --self-contained false --verbosity normal

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Linky.dll"]