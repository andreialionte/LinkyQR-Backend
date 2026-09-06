# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

RUN apt-get update && apt-get install -y \
    libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Context is repo root, so prefix with Linky/
COPY Linky/Linky.csproj ./
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore "Linky.csproj" -r linux-x64 --disable-parallel --verbosity normal

# Copy full project source
COPY Linky/ ./

RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish "Linky.csproj" -c Release -o /app/publish \
    --no-restore --self-contained false --verbosity normal

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Linky.dll"]
