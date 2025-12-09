using AAR.Shared.KeyVault;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Xunit;

namespace AAR.Tests.Shared.KeyVault;

/// <summary>
/// Unit tests for MockKeyVaultSecretProvider.
/// </summary>
public class MockKeyVaultSecretProviderTests
{
    private readonly KeyVaultOptions _options;
    private readonly ILogger<MockKeyVaultSecretProvider> _logger;

    public MockKeyVaultSecretProviderTests()
    {
        _options = new KeyVaultOptions
        {
            UseMockKeyVault = true,
            UseKeyVault = false,
            LocalSecretsPath = "test-secrets.json",
            SecretPrefix = ""
        };
        _logger = NullLogger<MockKeyVaultSecretProvider>.Instance;
    }

    [Fact]
    public void ProviderName_ShouldReturnMockKeyVault()
    {
        // Arrange
        var configuration = BuildConfiguration(new Dictionary<string, string?>());
        var provider = new MockKeyVaultSecretProvider(configuration, _logger, _options);

        // Act
        var name = provider.ProviderName;

        // Assert
        name.Should().Be("MockKeyVault");
    }

    [Fact]
    public void IsAvailable_ShouldReturnTrue()
    {
        // Arrange
        var configuration = BuildConfiguration(new Dictionary<string, string?>());
        var provider = new MockKeyVaultSecretProvider(configuration, _logger, _options);

        // Act
        var isAvailable = provider.IsAvailable;

        // Assert
        isAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task GetSecretAsync_WithExistingKey_ShouldReturnValue()
    {
        // Arrange
        var secrets = new Dictionary<string, string?>
        {
            { "Azure:OpenAI:ApiKey", "test-api-key" }
        };
        var configuration = BuildConfiguration(secrets);
        var provider = new MockKeyVaultSecretProvider(configuration, _logger, _options);

        // Act
        var result = await provider.GetSecretAsync("Azure:OpenAI:ApiKey");

        // Assert
        result.Should().Be("test-api-key");
    }

    [Fact]
    public async Task GetSecretAsync_WithKeyVaultFormat_ShouldConvertAndReturnValue()
    {
        // Arrange
        var secrets = new Dictionary<string, string?>
        {
            { "Azure:OpenAI:ApiKey", "test-api-key" }
        };
        var configuration = BuildConfiguration(secrets);
        var provider = new MockKeyVaultSecretProvider(configuration, _logger, _options);

        // Act - using Key Vault naming convention with --
        var result = await provider.GetSecretAsync("Azure--OpenAI--ApiKey");

        // Assert
        result.Should().Be("test-api-key");
    }

    [Fact]
    public async Task GetSecretAsync_WithNonExistingKey_ShouldReturnNull()
    {
        // Arrange
        var configuration = BuildConfiguration(new Dictionary<string, string?>());
        var provider = new MockKeyVaultSecretProvider(configuration, _logger, _options);

        // Act
        var result = await provider.GetSecretAsync("NonExistent:Key");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetSecretsAsync_WithMultipleExistingKeys_ShouldReturnAllValues()
    {
        // Arrange
        var secrets = new Dictionary<string, string?>
        {
            { "Azure:OpenAI:ApiKey", "api-key-1" },
            { "Azure:OpenAI:Endpoint", "https://test.openai.azure.com" },
            { "ConnectionStrings:DefaultConnection", "Server=localhost" }
        };
        var configuration = BuildConfiguration(secrets);
        var provider = new MockKeyVaultSecretProvider(configuration, _logger, _options);

        var secretNames = new[]
        {
            "Azure:OpenAI:ApiKey",
            "Azure:OpenAI:Endpoint",
            "ConnectionStrings:DefaultConnection"
        };

        // Act
        var results = await provider.GetSecretsAsync(secretNames);

        // Assert
        results.Should().HaveCount(3);
        results["Azure:OpenAI:ApiKey"].Should().Be("api-key-1");
        results["Azure:OpenAI:Endpoint"].Should().Be("https://test.openai.azure.com");
        results["ConnectionStrings:DefaultConnection"].Should().Be("Server=localhost");
    }

    [Fact]
    public async Task GetSecretsAsync_WithMixedKeys_ShouldReturnAllRequestedKeys()
    {
        // Arrange
        var secrets = new Dictionary<string, string?>
        {
            { "Azure:OpenAI:ApiKey", "api-key-1" }
        };
        var configuration = BuildConfiguration(secrets);
        var provider = new MockKeyVaultSecretProvider(configuration, _logger, _options);

        var secretNames = new[]
        {
            "Azure:OpenAI:ApiKey",
            "NonExistent:Key"
        };

        // Act
        var results = await provider.GetSecretsAsync(secretNames);

        // Assert - the implementation returns all requested keys, with null for missing ones
        results.Should().HaveCount(2);
        results["Azure:OpenAI:ApiKey"].Should().Be("api-key-1");
        results["NonExistent:Key"].Should().BeNull();
    }

    [Fact]
    public async Task GetSecretAsync_WithPrefix_ConfigKeyMatchesPrefix_ShouldReturnValue()
    {
        // Arrange
        // Note: The current implementation doesn't use prefix for lookups,
        // so we test that the prefixed key in config is found when looking for prefixed key
        var options = new KeyVaultOptions
        {
            UseMockKeyVault = true,
            SecretPrefix = "AAR-"
        };
        var secrets = new Dictionary<string, string?>
        {
            { "AAR-Azure:OpenAI:ApiKey", "prefixed-key" }
        };
        var configuration = BuildConfiguration(secrets);
        var provider = new MockKeyVaultSecretProvider(configuration, _logger, options);

        // Act - look for the exact key including prefix
        var result = await provider.GetSecretAsync("AAR-Azure:OpenAI:ApiKey");

        // Assert
        result.Should().Be("prefixed-key");
    }

    [Fact]
    public async Task GetSecretAsync_WithNestedConfiguration_ShouldRetrieveValue()
    {
        // Arrange
        var secrets = new Dictionary<string, string?>
        {
            { "VectorStore:CosmosEndpoint", "https://cosmos.documents.azure.com:443/" },
            { "VectorStore:CosmosKey", "cosmos-key-value" }
        };
        var configuration = BuildConfiguration(secrets);
        var provider = new MockKeyVaultSecretProvider(configuration, _logger, _options);

        // Act
        var endpoint = await provider.GetSecretAsync("VectorStore:CosmosEndpoint");
        var key = await provider.GetSecretAsync("VectorStore:CosmosKey");

        // Assert
        endpoint.Should().Be("https://cosmos.documents.azure.com:443/");
        key.Should().Be("cosmos-key-value");
    }

    [Theory]
    [InlineData("ConnectionStrings:DefaultConnection", "ConnectionStrings--DefaultConnection")]
    [InlineData("Azure:OpenAI:ApiKey", "Azure--OpenAI--ApiKey")]
    [InlineData("Jwt:Secret", "Jwt--Secret")]
    public async Task GetSecretAsync_ShouldHandleBothNamingConventions(string configKey, string keyVaultKey)
    {
        // Arrange
        var secrets = new Dictionary<string, string?>
        {
            { configKey, "secret-value" }
        };
        var configuration = BuildConfiguration(secrets);
        var provider = new MockKeyVaultSecretProvider(configuration, _logger, _options);

        // Act - should work with both formats
        var resultConfig = await provider.GetSecretAsync(configKey);
        var resultKeyVault = await provider.GetSecretAsync(keyVaultKey);

        // Assert
        resultConfig.Should().Be("secret-value");
        resultKeyVault.Should().Be("secret-value");
    }

    [Fact]
    public async Task GetSecretAsync_WithEmptySecretName_ShouldReturnNull()
    {
        // Arrange
        var configuration = BuildConfiguration(new Dictionary<string, string?>());
        var provider = new MockKeyVaultSecretProvider(configuration, _logger, _options);

        // Act
        var result = await provider.GetSecretAsync("");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetSecretsAsync_WithEmptyList_ShouldReturnEmptyDictionary()
    {
        // Arrange
        var configuration = BuildConfiguration(new Dictionary<string, string?>());
        var provider = new MockKeyVaultSecretProvider(configuration, _logger, _options);

        // Act
        var results = await provider.GetSecretsAsync(Array.Empty<string>());

        // Assert
        results.Should().BeEmpty();
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
