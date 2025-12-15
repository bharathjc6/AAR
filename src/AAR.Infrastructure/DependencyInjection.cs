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
using AAR.Infrastructure.Services.AI;
using AAR.Infrastructure.Services.Chunking;
using AAR.Infrastructure.Services.Embedding;
using AAR.Infrastructure.Services.Memory;
using AAR.Infrastructure.Services.Resilience;
using AAR.Infrastructure.Services.Retrieval;
using AAR.Infrastructure.Services.Routing;
using AAR.Infrastructure.Services.Telemetry;
using AAR.Infrastructure.Services.Validation;
using AAR.Infrastructure.Services.VectorStore;
using AAR.Infrastructure.Services.Watchdog;
using AAR.Shared.Tokenization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.Registry;

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
                    sqlOptions => sqlOptions.EnableRetryOnFailure())
                .ConfigureWarnings(w => w.Ignore(
                    Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));
        }
        else
        {
            // SQLite for local development
            services.AddDbContext<AarDbContext>(options =>
                options.UseSqlite(connectionString)
                .ConfigureWarnings(w => w.Ignore(
                    Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));
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

        // RAG processing configuration
        services.Configure<RagProcessingOptions>(configuration.GetSection(RagProcessingOptions.SectionName));
        services.Configure<MemoryManagementOptions>(configuration.GetSection(MemoryManagementOptions.SectionName));
        services.Configure<ConcurrencyOptions>(configuration.GetSection(ConcurrencyOptions.SectionName));
        services.Configure<JobApprovalOptions>(configuration.GetSection(JobApprovalOptions.SectionName));

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
        services.AddScoped<IGitService, GitService>();
        services.AddScoped<ICodeMetricsService, CodeMetricsService>();
        services.AddScoped<IPdfService, PdfService>();

        // Tokenization services
        services.Configure<TokenizerOptions>(configuration.GetSection(TokenizerOptions.SectionName));
        services.AddSingleton<ITokenizerFactory, TokenizerFactory>();

        // Chunking services
        services.Configure<ChunkerOptions>(configuration.GetSection(ChunkerOptions.SectionName));
        services.AddScoped<IChunker, SemanticChunker>();

        // ===================================================================
        // AI PROVIDER CONFIGURATION (Local/Azure)
        // ===================================================================
        services.Configure<AIProviderOptions>(configuration.GetSection(AIProviderOptions.SectionName));
        
        var aiProvider = configuration.GetValue<string>("AI:Provider") ?? "Local";
        var useLocalAI = aiProvider.Equals("Local", StringComparison.OrdinalIgnoreCase);

        // Configure HttpClientFactory for Ollama/Qdrant
        services.AddHttpClient("Ollama");
        services.AddHttpClient("Qdrant");

        // Register LLM Provider
        if (useLocalAI)
        {
            services.AddSingleton<ILLMProvider, OllamaLLMProvider>();
        }
        else
        {
            services.AddSingleton<ILLMProvider, AzureOpenAILLMProvider>();
        }

        // Register Embedding Provider
        if (useLocalAI)
        {
            services.AddSingleton<IEmbeddingProvider, OllamaEmbeddingProvider>();
        }
        else
        {
            services.AddSingleton<IEmbeddingProvider, AzureOpenAIEmbeddingProvider>();
        }

        // Adapt providers to existing interfaces for backward compatibility
        services.AddScoped<IOpenAiService>(sp =>
        {
            var llmProvider = sp.GetRequiredService<ILLMProvider>();
            var promptProvider = sp.GetRequiredService<IPromptTemplateProvider>();
            var logger = sp.GetRequiredService<ILogger<LLMProviderAdapter>>();
            return new LLMProviderAdapter(llmProvider, promptProvider, logger);
        });

        services.AddSingleton<IEmbeddingService>(sp =>
        {
            var embeddingProvider = sp.GetRequiredService<IEmbeddingProvider>();
            var rawService = new EmbeddingProviderAdapter(
                embeddingProvider,
                sp.GetRequiredService<ILogger<EmbeddingProviderAdapter>>());

            // Wrap with resilience decorator
            var pipelineProvider = sp.GetRequiredService<ResiliencePipelineProvider<string>>();
            var options = sp.GetRequiredService<IOptions<EmbeddingProcessingOptions>>();
            var metrics = sp.GetRequiredService<IMetricsService>();
            var logger = sp.GetRequiredService<ILogger<ResilientEmbeddingService>>();
            return new ResilientEmbeddingService(rawService, pipelineProvider, options, metrics, logger);
        });

        // Vector Store Configuration
        var vectorDbType = configuration.GetValue<string>("AI:VectorDb:Type") ?? "Qdrant";
        
        if (vectorDbType.Equals("Qdrant", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IVectorStore, QdrantVectorStore>();
        }
        else if (vectorDbType.Equals("InMemory", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IVectorStore, InMemoryVectorStore>();
        }
        else
        {
            // Default to Qdrant for production
            services.AddSingleton<IVectorStore, QdrantVectorStore>();
        }

        // Retrieval orchestrator
        services.Configure<ModelRouterOptions>(configuration.GetSection(ModelRouterOptions.SectionName));
        services.AddScoped<IRetrievalOrchestrator, RetrievalOrchestrator>();

        // RAG routing and memory management services
        services.AddScoped<IRagRiskFilter, RagRiskFilter>();
        services.AddScoped<IFileAnalysisRouter, FileAnalysisRouter>();
        services.AddSingleton<IMemoryMonitor, MemoryMonitor>();
        services.AddSingleton<IConcurrencyLimiter, ConcurrencyLimiter>();
        services.AddSingleton<ITempFileChunkWriter, TempFileChunkWriter>();

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

        // Job progress reporting (SignalR streaming)
        services.AddScoped<IJobProgressService, JobProgressService>();

        // Batch processing watchdog for stuck detection
        services.Configure<WatchdogOptions>(configuration.GetSection("Watchdog"));
        services.AddSingleton<IBatchProcessingWatchdog, BatchProcessingWatchdog>();
        services.AddHostedService(sp => (BatchProcessingWatchdog)sp.GetRequiredService<IBatchProcessingWatchdog>());

        // NOTE: InMemoryJobQueueService removed - MassTransit handles all queue operations
        // For Azure Service Bus in prod, configure MassTransit with Azure transport

        // Resilience policies (Polly)
        services.AddResiliencePolicies();

        return services;
    }
}
