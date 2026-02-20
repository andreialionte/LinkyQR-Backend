# Fixed logger syntax
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy everything 
COPY . ./

# Publish with normal verbosity
RUN dotnet publish -c Release -o /app/publish --self-contained false --verbosity normal

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Linky.dll"]