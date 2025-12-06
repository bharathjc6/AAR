// =============================================================================
// AAR.Infrastructure - DependencyInjection.cs
// Extension methods for registering infrastructure services
// =============================================================================

using AAR.Application.Interfaces;
using AAR.Domain.Interfaces;
using AAR.Infrastructure.Persistence;
using AAR.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AAR.Infrastructure;

/// <summary>
/// Extension methods for configuring infrastructure layer services
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Adds infrastructure layer services to the DI container
    /// </summary>
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services, 
        IConfiguration configuration)
    {
        // Database configuration
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? Environment.GetEnvironmentVariable("CONNECTION_STRING")
            ?? "Data Source=aar.db";

        // Use SQLite for local development, SQL Server for production
        var useSqlServer = configuration.GetValue<bool>("UseSqlServer") 
            || Environment.GetEnvironmentVariable("USE_SQL_SERVER") == "true";

        if (useSqlServer && !connectionString.Contains(".db"))
        {
            // TODO: Configure Azure SQL connection string via CONNECTION_STRING environment variable
            services.AddDbContext<AarDbContext>(options =>
                options.UseSqlServer(connectionString, 
                    sqlOptions => sqlOptions.EnableRetryOnFailure()));
        }
        else
        {
            // SQLite for local development
            services.AddDbContext<AarDbContext>(options =>
                options.UseSqlite(connectionString));
        }

        // Unit of Work
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Configure storage options
        services.Configure<FileSystemStorageOptions>(options =>
        {
            options.BasePath = configuration["Storage:BasePath"] 
                ?? Path.Combine(Path.GetTempPath(), "aar-storage");
        });

        services.Configure<AzureBlobStorageOptions>(options =>
        {
            options.ConnectionString = configuration["Azure:StorageConnectionString"]
                ?? Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING");
        });

        services.Configure<AzureQueueStorageOptions>(options =>
        {
            options.ConnectionString = configuration["Azure:StorageConnectionString"]
                ?? Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING");
        });

        services.Configure<AzureOpenAiOptions>(options =>
        {
            options.Endpoint = configuration["Azure:OpenAI:Endpoint"]
                ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
            options.ApiKey = configuration["Azure:OpenAI:ApiKey"]
                ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY");
            options.DeploymentName = configuration["Azure:OpenAI:DeploymentName"]
                ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT")
                ?? "gpt-4";
        });

        // Storage services - use local by default, Azure when configured
        var useAzureStorage = !string.IsNullOrEmpty(
            Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING"));
        
        if (useAzureStorage)
        {
            services.AddSingleton<IBlobStorageService, AzureBlobStorage>();
            services.AddSingleton<IQueueService, AzureQueueService>();
        }
        else
        {
            services.AddSingleton<IBlobStorageService, FileSystemBlobStorage>();
            services.AddSingleton<IQueueService, InMemoryQueueService>();
        }

        // Other services
        services.AddSingleton<IPromptTemplateProvider, PromptTemplateProvider>();
        services.AddScoped<IOpenAiService, AzureOpenAiService>();
        services.AddScoped<IGitService, GitService>();
        services.AddScoped<ICodeMetricsService, CodeMetricsService>();
        services.AddScoped<IPdfService, PdfService>();

        return services;
    }
}
