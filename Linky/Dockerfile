# syntax=docker/dockerfile:1

# Native AOT publishes a fully self-contained native executable, so the final
# image doesn't need the .NET runtime at all - just the native OS dependencies
# (openssl/icu/etc.) that runtime-deps provides. Much smaller than dotnet/aspnet.
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0 AS base
WORKDIR /app
EXPOSE 5000

RUN apt-get update && apt-get install -y \
    libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Native AOT (ILCompiler) needs a native linker + zlib headers to produce the
# final executable. See https://aka.ms/nativeaot-prerequisites.
RUN apt-get update && apt-get install -y \
    clang \
    zlib1g-dev \
    && rm -rf /var/lib/apt/lists/*

# Context is this folder (Linky/), so no prefix needed
COPY Linky.csproj ./
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore "Linky.csproj" -r linux-x64 --disable-parallel --verbosity normal

# Copy full project source
COPY . ./

# PublishAot requires a self-contained, RID-specific publish - it can't produce
# a framework-dependent output. -r must match the restore RID above.
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish "Linky.csproj" -c Release -r linux-x64 -o /app/publish \
    --no-restore --self-contained true --verbosity normal

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
# Native AOT output is a native executable named after the assembly (no .dll,
# no "dotnet" launcher needed).
ENTRYPOINT ["./Linky"]
