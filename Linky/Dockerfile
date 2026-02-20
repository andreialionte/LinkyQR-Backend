# Debug version with maximum verbosity
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy everything 
COPY . ./

# Add maximum verbosity to see what's happening
RUN dotnet publish -c Release -o /app/publish --self-contained false \
    --verbosity diagnostic \
    --logger "console;verbosity=diagnostic" \
    /flp:logfile=build.log;verbosity=diagnostic

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Linky.dll"]