// =============================================================================
// AAR.Infrastructure - DependencyInjection.cs
// Extension methods for registering infrastructure services
// =============================================================================

using AAR.Application.Configuration;
using AAR.Application.Interfaces;
using AAR.Application.Messaging;
using AAR.Domain.Interfaces;
using AAR.Infrastructure.Messaging;
using AAR.Infrastructure.Persistence;
using AAR.Infrastructure.Repositories;
using AAR.Infrastructure.Services;
using AAR.Infrastructure.Services.Chunking;
using AAR.Infrastructure.Services.Embedding;
using AAR.Infrastructure.Services.Resilience;
using AAR.Infrastructure.Services.Retrieval;
using AAR.Infrastructure.Services.Telemetry;
using AAR.Infrastructure.Services.Validation;
using AAR.Infrastructure.Services.VectorStore;
using AAR.Shared.Tokenization;
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

        // Repositories
        services.AddScoped<IApiKeyRepository, ApiKeyRepository>();
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<IReportRepository, ReportRepository>();
        services.AddScoped<IFileRecordRepository, FileRecordRepository>();
        services.AddScoped<IReviewFindingRepository, ReviewFindingRepository>();
        services.AddScoped<IChunkRepository, ChunkRepository>();
        services.AddScoped<IJobCheckpointRepository, JobCheckpointRepository>();
        services.AddScoped<IUploadSessionRepository, UploadSessionRepository>();
        services.AddScoped<IOrganizationQuotaRepository, OrganizationQuotaRepository>();

        // Scaling configuration
        services.Configure<ScaleLimitsOptions>(configuration.GetSection(ScaleLimitsOptions.SectionName));
        services.Configure<EmbeddingProcessingOptions>(configuration.GetSection(EmbeddingProcessingOptions.SectionName));
        services.Configure<WorkerProcessingOptions>(configuration.GetSection(WorkerProcessingOptions.SectionName));
        services.Configure<StoragePolicyOptions>(configuration.GetSection(StoragePolicyOptions.SectionName));
        services.Configure<ResumableUploadOptions>(configuration.GetSection(ResumableUploadOptions.SectionName));

        // Configure storage options
        services.Configure<FileSystemStorageOptions>(options =>
        {
            var basePath = configuration["Storage:BasePath"];
            options.BasePath = string.IsNullOrWhiteSpace(basePath)
                ? Path.Combine(Path.GetTempPath(), "aar-storage")
                : basePath;
        });

        services.Configure<AzureBlobStorageOptions>(options =>
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
        }
        else
        {
            services.AddSingleton<IBlobStorageService, FileSystemBlobStorage>();
        }

        // MassTransit messaging (includes IMessageBus registration)
        services.AddMassTransitMessaging(configuration);

        // Other services
        services.AddSingleton<IPromptTemplateProvider, PromptTemplateProvider>();
        services.AddScoped<IOpenAiService, AzureOpenAiService>();
        services.AddScoped<IGitService, GitService>();
        services.AddScoped<ICodeMetricsService, CodeMetricsService>();
        services.AddScoped<IPdfService, PdfService>();

        // Tokenization services
        services.Configure<TokenizerOptions>(configuration.GetSection(TokenizerOptions.SectionName));
        services.AddSingleton<ITokenizerFactory, TokenizerFactory>();

        // Chunking services
        services.Configure<ChunkerOptions>(configuration.GetSection(ChunkerOptions.SectionName));
        services.AddScoped<IChunker, SemanticChunker>();

        // Embedding services
        services.Configure<EmbeddingOptions>(configuration.GetSection(EmbeddingOptions.SectionName));
        var useMockEmbedding = configuration.GetValue<bool>("Embedding:UseMock") 
            || Environment.GetEnvironmentVariable("USE_MOCK_EMBEDDING") == "true";
        
        if (useMockEmbedding)
        {
            services.AddSingleton<IEmbeddingService, MockEmbeddingService>();
        }
        else
        {
            services.AddSingleton<IEmbeddingService, AzureOpenAiEmbeddingService>();
        }

        // Vector store services
        services.Configure<VectorStoreOptions>(configuration.GetSection(VectorStoreOptions.SectionName));
        var vectorStoreType = configuration.GetValue<string>("VectorStore:Type") ?? "InMemory";
        
        if (vectorStoreType.Equals("InMemory", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IVectorStore, InMemoryVectorStore>();
        }
        else
        {
            // Default to InMemory for now, CosmosVectorStore can be added later
            services.AddSingleton<IVectorStore, InMemoryVectorStore>();
        }

        // Retrieval orchestrator
        services.Configure<ModelRouterOptions>(configuration.GetSection(ModelRouterOptions.SectionName));
        services.AddScoped<IRetrievalOrchestrator, RetrievalOrchestrator>();

        // Schema validation
        services.Configure<SchemaValidationOptions>(configuration.GetSection(SchemaValidationOptions.SectionName));
        services.AddSingleton<ISchemaValidator, SchemaValidator>();

        // Telemetry
        services.AddSingleton<IAnalysisTelemetry, AnalysisTelemetry>();
        services.AddSingleton<IMetricsService, InMemoryMetricsService>();

        // Security services
        services.AddScoped<ISecureFileService, SecureFileService>();
        services.AddScoped<IVirusScanService, MockVirusScanService>();

        // Preflight & upload services
        services.AddScoped<IPreflightService, PreflightService>();
        services.AddScoped<IUploadSessionService, UploadSessionService>();

        // Streaming extraction
        services.AddSingleton<IStreamingExtractor, StreamingZipExtractor>();

        // Job queue (in-memory for dev, swap for Azure Service Bus in prod)
        services.AddSingleton<IJobQueueService, InMemoryJobQueueService>();

        // Resilience policies (Polly)
        services.AddResiliencePolicies();

        return services;
    }
}
