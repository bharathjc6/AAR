# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files
COPY AAR.sln ./
COPY src/AAR.Shared/AAR.Shared.csproj src/AAR.Shared/
COPY src/AAR.Domain/AAR.Domain.csproj src/AAR.Domain/
COPY src/AAR.Application/AAR.Application.csproj src/AAR.Application/
COPY src/AAR.Infrastructure/AAR.Infrastructure.csproj src/AAR.Infrastructure/
COPY src/AAR.Api/AAR.Api.csproj src/AAR.Api/
COPY src/AAR.Worker/AAR.Worker.csproj src/AAR.Worker/
COPY tests/AAR.Tests/AAR.Tests.csproj tests/AAR.Tests/

# Restore dependencies
RUN dotnet restore

# Copy source code
COPY . .

# Build
RUN dotnet build -c Release --no-restore

# Run tests
FROM build AS test
RUN dotnet test tests/AAR.Tests/AAR.Tests.csproj -c Release --no-build --logger "trx;LogFileName=test-results.trx"

# Publish API
FROM build AS publish-api
RUN dotnet publish src/AAR.Api/AAR.Api.csproj -c Release -o /app/publish --no-build

# Publish Worker
FROM build AS publish-worker
RUN dotnet publish src/AAR.Worker/AAR.Worker.csproj -c Release -o /app/publish --no-build

# API Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
WORKDIR /app

# Create non-root user
RUN adduser --disabled-password --gecos '' appuser

# Copy published files
COPY --from=publish-api /app/publish .

# Create directories for storage and logs
RUN mkdir -p /app/storage /app/logs && chown -R appuser:appuser /app

# Switch to non-root user
USER appuser

# Expose port
EXPOSE 8080

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

# Entry point
ENTRYPOINT ["dotnet", "AAR.Api.dll"]

# Worker Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS worker
WORKDIR /app

# Create non-root user
RUN adduser --disabled-password --gecos '' appuser

# Copy published files
COPY --from=publish-worker /app/publish .

# Create directories for storage and logs
RUN mkdir -p /app/storage /app/logs && chown -R appuser:appuser /app

# Switch to non-root user
USER appuser

# Entry point
ENTRYPOINT ["dotnet", "AAR.Worker.dll"]
