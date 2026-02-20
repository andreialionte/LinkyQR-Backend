# Optimized Dockerfile for LinkyQR

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project file for dependency resolution
COPY ["Linky.csproj", "./"]

# Restore dependencies (cached layer if csproj hasn't changed)
RUN dotnet restore "Linky.csproj" --verbosity minimal

# Copy source code
COPY . .

# Build the application  
RUN dotnet build "Linky.csproj" -c Release -o /app/build --no-restore

# Publish stage
FROM build AS publish
RUN dotnet publish "Linky.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Final stage
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Linky.dll"]