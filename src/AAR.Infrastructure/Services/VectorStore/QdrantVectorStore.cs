// =============================================================================
// AAR.Infrastructure - Services/VectorStore/QdrantVectorStore.cs
// Qdrant vector database implementation with collection management
// =============================================================================

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AAR.Application.Configuration;
using AAR.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AAR.Infrastructure.Services.VectorStore;

/// <summary>
/// Qdrant vector store implementation with auto collection management
/// </summary>
public class QdrantVectorStore : IVectorStore, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly VectorDbOptions _options;
    private readonly ILogger<QdrantVectorStore> _logger;
    private readonly string _collectionName;
    private bool _collectionInitialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public QdrantVectorStore(
        IHttpClientFactory httpClientFactory,
        IOptions<AIProviderOptions> options,
        ILogger<QdrantVectorStore> logger)
    {
        _httpClient = httpClientFactory.CreateClient("Qdrant");
        _options = options.Value.VectorDb;
        _logger = logger;
        _collectionName = $"{_options.CollectionPrefix}_vectors";

        _httpClient.BaseAddress = new Uri(_options.Url);
        _httpClient.Timeout = TimeSpan.FromSeconds(30);

        // Add API key if provided (for Qdrant Cloud)
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("api-key", _options.ApiKey);
        }

        _logger.LogInformation("QdrantVectorStore initialized (collection: {Collection}, dimension: {Dimension})",
            _collectionName, _options.Dimension);
    }

    /// <inheritdoc/>
    public async Task IndexVectorAsync(
        string chunkId,
        float[] vector,
        VectorMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        await EnsureCollectionExistsAsync(cancellationToken);

        // Validate vector
        if (vector == null || vector.Length == 0)
        {
            _logger.LogError("Vector for chunk {ChunkId} is null or empty", chunkId);
            throw new ArgumentException($"Vector for chunk {chunkId} cannot be null or empty", nameof(vector));
        }

        if (vector.Length != _options.Dimension)
        {
            _logger.LogError("Vector for chunk {ChunkId} has dimension {ActualDim}, expected {ExpectedDim}",
                chunkId, vector.Length, _options.Dimension);
            throw new ArgumentException($"Vector dimension {vector.Length} does not match configured dimension {_options.Dimension}", 
                nameof(vector));
        }

        try
        {
            var point = new QdrantPoint
            {
                Id = chunkId,
                Vector = vector,
                Payload = new QdrantPayload
                {
                    ProjectId = metadata.ProjectId.ToString(),
                    FilePath = metadata.FilePath,
                    StartLine = metadata.StartLine,
                    EndLine = metadata.EndLine,
                    Language = metadata.Language ?? "unknown",
                    SemanticType = metadata.SemanticType,
                    SemanticName = metadata.SemanticName
                }
            };

            var request = new QdrantUpsertRequest { Points = [point] };

            var response = await _httpClient.PutAsJsonAsync(
                $"/collections/{_collectionName}/points",
                request,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            _logger.LogDebug("Indexed vector: {ChunkId}", chunkId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index vector {ChunkId}", chunkId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task IndexVectorsAsync(
        IEnumerable<(string chunkId, float[] vector, VectorMetadata metadata)> vectors,
        CancellationToken cancellationToken = default)
    {
        await EnsureCollectionExistsAsync(cancellationToken);

        var vectorList = vectors.ToList();
        if (vectorList.Count == 0)
            return;

        try
        {
            // Validate all vectors before indexing (dimension check)
            var invalidVectors = new List<int>();
            for (int i = 0; i < vectorList.Count; i++)
            {
                var v = vectorList[i];
                if (v.vector == null || v.vector.Length == 0)
                {
                    invalidVectors.Add(i);
                    _logger.LogWarning("Vector {Index} ({ChunkId}) is null or empty", i, v.chunkId);
                }
                else if (v.vector.Length != _options.Dimension)
                {
                    invalidVectors.Add(i);
                    _logger.LogWarning("Vector {Index} ({ChunkId}) has dimension {ActualDim}, expected {ExpectedDim}",
                        i, v.chunkId, v.vector.Length, _options.Dimension);
                }
            }

            if (invalidVectors.Count > 0)
            {
                _logger.LogError("Found {Count} invalid vectors out of {Total}. Skipping invalid vectors.",
                    invalidVectors.Count, vectorList.Count);
                
                // Remove invalid vectors from the list (process in reverse to maintain indices)
                foreach (var idx in invalidVectors.OrderByDescending(x => x))
                {
                    vectorList.RemoveAt(idx);
                }

                if (vectorList.Count == 0)
                {
                    _logger.LogWarning("All vectors were invalid; no vectors indexed");
                    return;
                }
            }

            // Qdrant can handle large batches, but we'll batch at 100 for safety
            const int batchSize = 100;
            var batches = vectorList.Chunk(batchSize).ToList();

            _logger.LogInformation("Indexing {Count} vectors in {Batches} batches (dimension: {Dimension}D)", 
                vectorList.Count, batches.Count, _options.Dimension);

            int batchNumber = 0;
            foreach (var batch in batches)
            {
                batchNumber++;
                cancellationToken.ThrowIfCancellationRequested();

                var points = batch.Select(v => new QdrantPoint
                {
                    Id = v.chunkId,
                    Vector = v.vector,
                    Payload = new QdrantPayload
                    {
                        ProjectId = v.metadata.ProjectId.ToString(),
                        FilePath = v.metadata.FilePath,
                        StartLine = v.metadata.StartLine,
                        EndLine = v.metadata.EndLine,
                        Language = v.metadata.Language ?? "unknown",
                        SemanticType = v.metadata.SemanticType,
                        SemanticName = v.metadata.SemanticName
                    }
                }).ToList();

                var request = new QdrantUpsertRequest { Points = points };

                try
                {
                    var response = await _httpClient.PutAsJsonAsync(
                        $"/collections/{_collectionName}/points",
                        request,
                        cancellationToken);

                    if (!response.IsSuccessStatusCode)
                    {
                        var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                        _logger.LogError("Qdrant returned {StatusCode}: {Error}", 
                            response.StatusCode, errorContent);
                        response.EnsureSuccessStatusCode();
                    }

                    _logger.LogDebug("Successfully indexed batch {Number}/{Total} ({Count} vectors)",
                        batchNumber, batches.Count, batch.Length);
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogError(ex, "Failed to index batch {Number}/{Total} ({Count} vectors). " +
                        "Likely causes: vector dimension mismatch ({ConfiguredDim}D), invalid payload, or Qdrant server error",
                        batchNumber, batches.Count, batch.Length, _options.Dimension);
                    throw;
                }
            }

            _logger.LogInformation("Successfully indexed {Count} vectors", vectorList.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to batch index vectors");
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<VectorSearchResult>> QueryAsync(
        float[] queryVector,
        int topK = 10,
        Guid? projectId = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureCollectionExistsAsync(cancellationToken);

        try
        {
            var searchRequest = new QdrantSearchRequest
            {
                Vector = queryVector,
                Limit = topK,
                WithPayload = true,
                Filter = projectId.HasValue
                    ? new QdrantFilter
                    {
                        Must = [new QdrantCondition 
                        { 
                            Key = "project_id", 
                            Match = new QdrantMatch { Value = projectId.Value.ToString() }
                        }]
                    }
                    : null
            };

            var response = await _httpClient.PostAsJsonAsync(
                $"/collections/{_collectionName}/points/search",
                searchRequest,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var searchResponse = await response.Content.ReadFromJsonAsync<QdrantSearchResponse>(cancellationToken)
                ?? throw new InvalidOperationException("Empty search response from Qdrant");

            var results = searchResponse.Result.Select(r => new VectorSearchResult
            {
                ChunkId = r.Id,
                Score = r.Score,
                Metadata = new VectorMetadata
                {
                    ProjectId = Guid.Parse(r.Payload.ProjectId),
                    FilePath = r.Payload.FilePath,
                    StartLine = r.Payload.StartLine,
                    EndLine = r.Payload.EndLine,
                    Language = r.Payload.Language,
                    SemanticType = r.Payload.SemanticType,
                    SemanticName = r.Payload.SemanticName
                }
            }).ToList();

            _logger.LogDebug("Search returned {Count} results", results.Count);

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query vectors");
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task DeleteByProjectIdAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        if (!_collectionInitialized)
            return; // Collection doesn't exist yet

        try
        {
            var deleteRequest = new QdrantDeleteRequest
            {
                Filter = new QdrantFilter
                {
                    Must = [new QdrantCondition
                    {
                        Key = "project_id",
                        Match = new QdrantMatch { Value = projectId.ToString() }
                    }]
                }
            };

            var response = await _httpClient.PostAsJsonAsync(
                $"/collections/{_collectionName}/points/delete",
                deleteRequest,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            _logger.LogInformation("Deleted vectors for project {ProjectId}", projectId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete vectors for project {ProjectId}", projectId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string chunkId, CancellationToken cancellationToken = default)
    {
        if (!_collectionInitialized)
            return;

        try
        {
            var deleteRequest = new QdrantDeleteRequest
            {
                Points = [chunkId]
            };

            var response = await _httpClient.PostAsJsonAsync(
                $"/collections/{_collectionName}/points/delete",
                deleteRequest,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            _logger.LogDebug("Deleted vector {ChunkId}", chunkId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete vector {ChunkId}", chunkId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<int> CountAsync(Guid? projectId = null, CancellationToken cancellationToken = default)
    {
        if (!_collectionInitialized)
            return 0;

        try
        {
            var countRequest = new QdrantCountRequest
            {
                Filter = projectId.HasValue
                    ? new QdrantFilter
                    {
                        Must = [new QdrantCondition
                        {
                            Key = "project_id",
                            Match = new QdrantMatch { Value = projectId.Value.ToString() }
                        }]
                    }
                    : null
            };

            var response = await _httpClient.PostAsJsonAsync(
                $"/collections/{_collectionName}/points/count",
                countRequest,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var countResponse = await response.Content.ReadFromJsonAsync<QdrantCountResponse>(cancellationToken);
            return countResponse?.Result?.Count ?? 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to count vectors");
            return 0;
        }
    }

    /// <summary>
    /// Ensures the collection exists, creating it if necessary
    /// </summary>
    private async Task EnsureCollectionExistsAsync(CancellationToken cancellationToken)
    {
        if (_collectionInitialized)
            return;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_collectionInitialized)
                return;

            // Check if collection exists
            var checkResponse = await _httpClient.GetAsync(
                $"/collections/{_collectionName}",
                cancellationToken);

            if (checkResponse.IsSuccessStatusCode)
            {
                _logger.LogInformation("Collection {Collection} already exists", _collectionName);
                _collectionInitialized = true;
                return;
            }

            // Create collection
            _logger.LogInformation("Creating collection {Collection} with dimension {Dimension}",
                _collectionName, _options.Dimension);

            var createRequest = new QdrantCreateCollectionRequest
            {
                Vectors = new QdrantVectorConfig
                {
                    Size = _options.Dimension,
                    Distance = "Cosine"
                }
            };

            var createResponse = await _httpClient.PutAsJsonAsync(
                $"/collections/{_collectionName}",
                createRequest,
                cancellationToken);

            createResponse.EnsureSuccessStatusCode();

            // Create index on project_id for faster filtering
            var indexRequest = new QdrantCreateIndexRequest
            {
                FieldName = "project_id",
                FieldSchema = "keyword"
            };

            var indexResponse = await _httpClient.PutAsJsonAsync(
                $"/collections/{_collectionName}/index",
                indexRequest,
                cancellationToken);

            indexResponse.EnsureSuccessStatusCode();

            _collectionInitialized = true;
            _logger.LogInformation("Collection {Collection} created successfully", _collectionName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure collection exists");
            throw;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        _initLock?.Dispose();
    }

    #region Qdrant API Models

    private class QdrantPoint
    {
        [JsonPropertyName("id")]
        public required string Id { get; init; }

        [JsonPropertyName("vector")]
        public required float[] Vector { get; init; }

        [JsonPropertyName("payload")]
        public required QdrantPayload Payload { get; init; }
    }

    private class QdrantPayload
    {
        [JsonPropertyName("project_id")]
        public required string ProjectId { get; init; }

        [JsonPropertyName("file_path")]
        public required string FilePath { get; init; }

        [JsonPropertyName("start_line")]
        public int StartLine { get; init; }

        [JsonPropertyName("end_line")]
        public int EndLine { get; init; }

        [JsonPropertyName("language")]
        public string? Language { get; init; }

        [JsonPropertyName("semantic_type")]
        public string? SemanticType { get; init; }

        [JsonPropertyName("semantic_name")]
        public string? SemanticName { get; init; }

        [JsonPropertyName("chunk_index")]
        public int? ChunkIndex { get; init; }

        [JsonPropertyName("total_chunks")]
        public int? TotalChunks { get; init; }
    }

    private class QdrantUpsertRequest
    {
        [JsonPropertyName("points")]
        public required List<QdrantPoint> Points { get; init; }
    }

    private class QdrantSearchRequest
    {
        [JsonPropertyName("vector")]
        public required float[] Vector { get; init; }

        [JsonPropertyName("limit")]
        public int Limit { get; init; }

        [JsonPropertyName("with_payload")]
        public bool WithPayload { get; init; }

        [JsonPropertyName("filter")]
        public QdrantFilter? Filter { get; init; }
    }

    private class QdrantFilter
    {
        [JsonPropertyName("must")]
        public List<QdrantCondition>? Must { get; init; }
    }

    private class QdrantCondition
    {
        [JsonPropertyName("key")]
        public required string Key { get; init; }

        [JsonPropertyName("match")]
        public QdrantMatch? Match { get; init; }
    }

    private class QdrantMatch
    {
        [JsonPropertyName("value")]
        public required string Value { get; init; }
    }

    private class QdrantSearchResponse
    {
        [JsonPropertyName("result")]
        public required List<QdrantSearchResult> Result { get; init; }
    }

    private class QdrantSearchResult
    {
        [JsonPropertyName("id")]
        public required string Id { get; init; }

        [JsonPropertyName("score")]
        public float Score { get; init; }

        [JsonPropertyName("payload")]
        public required QdrantPayload Payload { get; init; }
    }

    private class QdrantDeleteRequest
    {
        [JsonPropertyName("points")]
        public List<string>? Points { get; init; }

        [JsonPropertyName("filter")]
        public QdrantFilter? Filter { get; init; }
    }

    private class QdrantCountRequest
    {
        [JsonPropertyName("filter")]
        public QdrantFilter? Filter { get; init; }
    }

    private class QdrantCountResponse
    {
        [JsonPropertyName("result")]
        public QdrantCountResult? Result { get; init; }
    }

    private class QdrantCountResult
    {
        [JsonPropertyName("count")]
        public int Count { get; init; }
    }

    private class QdrantCreateCollectionRequest
    {
        [JsonPropertyName("vectors")]
        public required QdrantVectorConfig Vectors { get; init; }
    }

    private class QdrantVectorConfig
    {
        [JsonPropertyName("size")]
        public int Size { get; init; }

        [JsonPropertyName("distance")]
        public required string Distance { get; init; }
    }

    private class QdrantCreateIndexRequest
    {
        [JsonPropertyName("field_name")]
        public required string FieldName { get; init; }

        [JsonPropertyName("field_schema")]
        public required string FieldSchema { get; init; }
    }

    #endregion
}
