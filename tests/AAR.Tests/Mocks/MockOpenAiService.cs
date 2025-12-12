// AAR.Tests - Mocks/MockOpenAiService.cs
// Mock implementation of OpenAI service for testing

using AAR.Application.DTOs;
using AAR.Application.Interfaces;
using AAR.Domain.Enums;
using AAR.Shared;
using System.Collections.Concurrent;

namespace AAR.Tests.Mocks;

/// <summary>
/// Mock OpenAI service that returns deterministic responses for testing.
/// </summary>
public class MockOpenAiService : IOpenAiService
{
    private readonly ConcurrentBag<OpenAiCall> _calls = new();
    private int _delay;

    public IReadOnlyCollection<OpenAiCall> Calls => _calls.ToArray();
    public int CallCount => _calls.Count;
    public bool IsMockMode => true;

    public void SetDelay(int milliseconds) => _delay = milliseconds;
    public void Reset() { _calls.Clear(); _delay = 0; }

    public async Task<Result<AgentAnalysisResponse>> AnalyzeAsync(
        AgentType agentType,
        AnalysisContext context,
        IDictionary<string, string> fileContents,
        CancellationToken cancellationToken = default)
    {
        if (_delay > 0) await Task.Delay(_delay, cancellationToken);

        _calls.Add(new OpenAiCall { Type = "AnalyzeAsync", AgentType = agentType.ToString(), Timestamp = DateTime.UtcNow });

        var response = new AgentAnalysisResponse
        {
            Findings =
            [
                new AgentFinding
                {
                    Id = Guid.NewGuid().ToString(),
                    FilePath = "test/file.cs",
                    Category = agentType.ToString(),
                    Severity = "Medium",
                    Description = $"Mock {agentType} finding for testing",
                    Explanation = "This is a mock finding generated for testing purposes."
                }
            ],
            Summary = $"Mock analysis from {agentType}",
            Recommendations = [$"Consider reviewing the {agentType.ToString().ToLower()} aspects"]
        };

        return Result<AgentAnalysisResponse>.Success(response);
    }

    public async Task<string> AnalyzeCodeAsync(string prompt, string agentType, CancellationToken cancellationToken = default)
    {
        if (_delay > 0) await Task.Delay(_delay, cancellationToken);
        _calls.Add(new OpenAiCall { Type = "AnalyzeCodeAsync", AgentType = agentType, Timestamp = DateTime.UtcNow });
        return $@"{{""findings"": [], ""summary"": ""Mock analysis""}}";
    }

    public async Task<Result<string>> GenerateSummaryAsync(
        AnalysisContext context,
        IEnumerable<AgentAnalysisResponse> agentResponses,
        CancellationToken cancellationToken = default)
    {
        if (_delay > 0) await Task.Delay(_delay, cancellationToken);
        _calls.Add(new OpenAiCall { Type = "GenerateSummaryAsync", Timestamp = DateTime.UtcNow });
        return Result<string>.Success(@"{""healthScore"": 75, ""summary"": ""Mock summary""}");
    }

    public async Task<Result<string>> GeneratePatchAsync(
        string filePath, string originalCode, string issue, string suggestedFix,
        CancellationToken cancellationToken = default)
    {
        if (_delay > 0) await Task.Delay(_delay, cancellationToken);
        _calls.Add(new OpenAiCall { Type = "GeneratePatchAsync", Timestamp = DateTime.UtcNow });
        return Result<string>.Success($"// Fix: {issue}");
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
}

public record OpenAiCall
{
    public required string Type { get; init; }
    public string? AgentType { get; init; }
    public DateTime Timestamp { get; init; }
}
