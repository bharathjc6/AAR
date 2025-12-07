// =============================================================================
// AAR.Shared - Tokenization/TokenizerFactory.cs
// Factory for creating tokenizer instances based on configuration
// =============================================================================

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AAR.Shared.Tokenization;

/// <summary>
/// Factory for creating tokenizer instances based on configuration.
/// </summary>
public sealed class TokenizerFactory : ITokenizerFactory
{
    private readonly TokenizerOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Lazy<ITokenizer> _tokenizer;

    public TokenizerFactory(
        IOptions<TokenizerOptions> options,
        ILoggerFactory loggerFactory)
    {
        _options = options.Value;
        _loggerFactory = loggerFactory;
        _tokenizer = new Lazy<ITokenizer>(CreateTokenizer);
    }

    /// <inheritdoc/>
    public ITokenizer Create() => _tokenizer.Value;

    private ITokenizer CreateTokenizer()
    {
        var logger = _loggerFactory.CreateLogger<TokenizerFactory>();

        return _options.Mode switch
        {
            TokenizerMode.Tiktoken => CreateTiktokenTokenizer(logger),
            TokenizerMode.Heuristic => CreateHeuristicTokenizer(logger),
            TokenizerMode.Service => CreateServiceTokenizer(logger),
            _ => CreateHeuristicTokenizer(logger)
        };
    }

    private ITokenizer CreateTiktokenTokenizer(ILogger logger)
    {
        try
        {
            var tiktokenLogger = _loggerFactory.CreateLogger<TiktokenTokenizer>();
            var options = Options.Create(_options);
            var tokenizer = new TiktokenTokenizer(options, tiktokenLogger);
            logger.LogInformation("Created TiktokenTokenizer with encoding: {Encoding}", _options.Encoding);
            return tokenizer;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to create TiktokenTokenizer, falling back to heuristic");
            
            if (_options.FallbackToHeuristic)
            {
                return CreateHeuristicTokenizer(logger);
            }
            
            throw;
        }
    }

    private ITokenizer CreateHeuristicTokenizer(ILogger logger)
    {
        logger.LogInformation("Created HeuristicTokenizer");
        return new HeuristicTokenizer();
    }

    private ITokenizer CreateServiceTokenizer(ILogger logger)
    {
        if (string.IsNullOrEmpty(_options.ServiceUrl))
        {
            logger.LogWarning("Tokenizer service URL not configured, falling back to heuristic");
            return CreateHeuristicTokenizer(logger);
        }

        // Service tokenizer implementation would go here
        // For now, fall back to heuristic
        logger.LogWarning("Service tokenizer not yet implemented, falling back to heuristic");
        return CreateHeuristicTokenizer(logger);
    }
}

/// <summary>
/// Factory interface for creating tokenizers
/// </summary>
public interface ITokenizerFactory
{
    /// <summary>
    /// Creates a tokenizer based on the configured mode.
    /// </summary>
    ITokenizer Create();
}
