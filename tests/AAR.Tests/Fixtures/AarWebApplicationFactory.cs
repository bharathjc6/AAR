// AAR.Tests - Fixtures/AarWebApplicationFactory.cs
// Custom WebApplicationFactory for API integration testing with local services

using System.Text.Json;
using System.Text.Json.Serialization;
using AAR.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace AAR.Tests.Fixtures;

// Type alias to disambiguate between AAR.Api.Program and AAR.Worker.Program
using ApiProgram = AAR.Api.Program;

/// <summary>
/// Custom WebApplicationFactory that configures the API for testing
/// with real Ollama and Qdrant services (no mocks).
/// </summary>
public class AarWebApplicationFactory : WebApplicationFactory<ApiProgram>
{
    private readonly string _databaseName = $"AarTest_{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureTestServices(services =>
        {
            // Remove ALL database context registrations to avoid multiple provider error
            var descriptorsToRemove = services
                .Where(d => 
                    d.ServiceType == typeof(DbContextOptions<AarDbContext>) ||
                    d.ServiceType == typeof(DbContextOptions) ||
                    d.ServiceType == typeof(AarDbContext) ||
                    d.ServiceType.FullName?.Contains("DbContext") == true)
                .ToList();
            
            foreach (var descriptor in descriptorsToRemove)
            {
                services.Remove(descriptor);
            }

            // Add in-memory database with transaction warning suppressed
            services.AddDbContext<AarDbContext>(options =>
            {
                options.UseInMemoryDatabase(_databaseName);
                options.EnableSensitiveDataLogging();
                options.ConfigureWarnings(w => 
                    w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning));
            });

            // NOTE: No longer removing IOpenAiService, IEmbeddingService, IVectorStore
            // Tests will use real Ollama and Qdrant services configured via appsettings

            // Suppress excessive logging during tests
            services.AddLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Warning);
                logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
                logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
            });
        });

        builder.ConfigureAppConfiguration((context, config) =>
        {
            // Override configuration for testing with local services
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["KeyVault:UseKeyVault"] = "false",
                ["KeyVault:UseMockKeyVault"] = "true",
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={_databaseName};Mode=Memory",
                ["MassTransit:RegisterConsumers"] = "false",
                ["Processing:MaxFileSizeKb"] = "200",
                ["Processing:DirectSendThresholdKb"] = "10",
                // Use local Ollama and Qdrant
                ["AI:Provider"] = "Local",
                ["AI:Local:OllamaUrl"] = "http://127.0.0.1:11434",
                ["AI:Local:LLMModel"] = "qwen2.5-coder:7b",
                ["AI:Local:EmbeddingModel"] = "bge-large:latest",
                ["AI:Local:TimeoutSeconds"] = "120",
                ["AI:VectorDb:Type"] = "Qdrant",
                ["AI:VectorDb:Url"] = "http://localhost:6333",
                ["AI:VectorDb:CollectionPrefix"] = "aar_test",
                ["AI:VectorDb:Dimension"] = "1024",
                ["Embedding:UseMock"] = "false",
                // Relax upload constraints for testing
                ["ResumableUpload:MinPartSizeBytes"] = "1024", // 1KB min for testing
                ["ResumableUpload:MaxParts"] = "100",
            });
        });
    }

    private const string TestApiKey = "aar_test_key_12345";
    
    /// <summary>
    /// Creates an HttpClient with a test API key header
    /// </summary>
    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApiKey);
        return client;
    }

    /// <summary>
    /// Ensures the database is created and seeded with test data
    /// </summary>
    public async Task InitializeDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AarDbContext>();
        await dbContext.Database.EnsureCreatedAsync();
        
        // Seed a test API key if not exists
        if (!await dbContext.ApiKeys.AnyAsync())
        {
            var testApiKey = AAR.Domain.Entities.ApiKey.CreateFromPlainText(
                TestApiKey, 
                "Test API Key",
                expiresAt: null,
                scopes: "read,write,admin");
            
            dbContext.ApiKeys.Add(testApiKey);
            await dbContext.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Gets JSON serializer options matching the API configuration
    /// </summary>
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
}

/// <summary>
/// xUnit collection fixture for sharing WebApplicationFactory across tests
/// </summary>
[CollectionDefinition("ApiTests")]
public class ApiTestCollection : ICollectionFixture<AarWebApplicationFactory>
{
}
