# syntax=docker/dockerfile:1

# Build stage: .NET 10 SDK image (Requirement 25.1).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj files first so restore can be cached in its own layer.
# The `tests/` directory is excluded via .dockerignore (Requirement 25.5),
# so we restore against the Web project, which transitively resolves the
# Core project via ProjectReference.
COPY src/RehearsalForecast.Web/RehearsalForecast.Web.csproj src/RehearsalForecast.Web/
COPY src/RehearsalForecast.Core/RehearsalForecast.Core.csproj src/RehearsalForecast.Core/
RUN dotnet restore src/RehearsalForecast.Web/RehearsalForecast.Web.csproj

# Copy the remaining source and publish the Web project.
COPY . .
RUN dotnet publish src/RehearsalForecast.Web/RehearsalForecast.Web.csproj \
    -c Release \
    -o /out \
    /p:UseAppHost=false

# Runtime stage: ASP.NET 10 runtime image (Requirement 25.1).
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .

# Cloud Run injects the PORT environment variable (Requirement 25.3);
# default to 8080 for local runs. No secrets or environment-specific
# configuration is baked into the image (Requirement 25.4).
ENV ASPNETCORE_URLS=http://+:${PORT:-8080}
EXPOSE 8080

# Run as the non-root `app` user shipped in the aspnet:10.0 base image
# (Requirement 25.2).
USER app

ENTRYPOINT ["dotnet", "RehearsalForecast.Web.dll"]
