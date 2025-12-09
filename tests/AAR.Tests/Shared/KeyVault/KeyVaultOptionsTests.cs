using AAR.Shared.KeyVault;
using FluentAssertions;
using Xunit;

namespace AAR.Tests.Shared.KeyVault;

/// <summary>
/// Unit tests for KeyVaultOptions.
/// </summary>
public class KeyVaultOptionsTests
{
    [Fact]
    public void DefaultValues_ShouldBeCorrect()
    {
        // Arrange & Act
        var options = new KeyVaultOptions();

        // Assert
        options.VaultUri.Should().BeNull(); // nullable string defaults to null
        options.UseKeyVault.Should().BeFalse();
        options.UseMockKeyVault.Should().BeFalse(); // Default is false in the class
        options.LocalSecretsPath.Should().Be("secrets.local.json");
        options.SecretPrefix.Should().BeNull(); // nullable string defaults to null
        options.TimeoutSeconds.Should().Be(30);
        options.ThrowOnUnavailable.Should().BeTrue(); // Default is true in the class
        options.PreloadSecrets.Should().BeEmpty();
        options.ReloadOnChange.Should().BeFalse(); // Default is false in the class
    }

    [Fact]
    public void VaultUri_ShouldBeSettable()
    {
        // Arrange
        var options = new KeyVaultOptions();

        // Act
        options.VaultUri = "https://my-vault.vault.azure.net/";

        // Assert
        options.VaultUri.Should().Be("https://my-vault.vault.azure.net/");
    }

    [Fact]
    public void UseKeyVault_ShouldBeSettable()
    {
        // Arrange
        var options = new KeyVaultOptions();

        // Act
        options.UseKeyVault = true;

        // Assert
        options.UseKeyVault.Should().BeTrue();
    }

    [Fact]
    public void PreloadSecrets_ShouldAcceptList()
    {
        // Arrange
        var options = new KeyVaultOptions();

        // Act
        options.PreloadSecrets = new List<string>
        {
            "Azure:OpenAI:ApiKey",
            "ConnectionStrings:DefaultConnection"
        };

        // Assert
        options.PreloadSecrets.Should().HaveCount(2);
        options.PreloadSecrets.Should().Contain("Azure:OpenAI:ApiKey");
        options.PreloadSecrets.Should().Contain("ConnectionStrings:DefaultConnection");
    }

    [Fact]
    public void TimeoutSeconds_ShouldBeSettable()
    {
        // Arrange
        var options = new KeyVaultOptions();

        // Act
        options.TimeoutSeconds = 60;

        // Assert
        options.TimeoutSeconds.Should().Be(60);
    }

    [Theory]
    [InlineData("https://vault1.vault.azure.net/", true, false)]
    [InlineData("https://vault2.vault.azure.net/", false, true)]
    [InlineData("", false, true)]
    public void ConfigurationCombinations_ShouldBeValid(string vaultUri, bool useKeyVault, bool useMockKeyVault)
    {
        // Arrange & Act
        var options = new KeyVaultOptions
        {
            VaultUri = vaultUri,
            UseKeyVault = useKeyVault,
            UseMockKeyVault = useMockKeyVault
        };

        // Assert
        options.VaultUri.Should().Be(vaultUri);
        options.UseKeyVault.Should().Be(useKeyVault);
        options.UseMockKeyVault.Should().Be(useMockKeyVault);
    }

    [Fact]
    public void SecretPrefix_ShouldBeSettable()
    {
        // Arrange
        var options = new KeyVaultOptions();

        // Act
        options.SecretPrefix = "AAR-Production-";

        // Assert
        options.SecretPrefix.Should().Be("AAR-Production-");
    }
}
