// =============================================================================
// AAR.Infrastructure - Services/VectorStore/QdrantVectorStore.cs
// Qdrant vector database implementation with collection management
// =============================================================================

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
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
    private readonly string _baseCollectionName;
    private readonly ConcurrentDictionary<string, bool> _collectionInitialized = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _initLocks = new();

    public QdrantVectorStore(
        IHttpClientFactory httpClientFactory,
        IOptions<AIProviderOptions> options,
        ILogger<QdrantVectorStore> logger)
    {
        _httpClient = httpClientFactory.CreateClient("Qdrant");
        _options = options.Value.VectorDb;
        _logger = logger;
        _baseCollectionName = $"{_options.CollectionPrefix}_vectors";

        _httpClient.BaseAddress = new Uri(_options.Url);
        _httpClient.Timeout = TimeSpan.FromSeconds(30);

        // Add API key if provided (for Qdrant Cloud)
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("api-key", _options.ApiKey);
        }

        _logger.LogInformation("QdrantVectorStore initialized (baseCollection: {Collection}, dimension: {Dimension}, perProject: {PerProject})",
            _baseCollectionName, _options.Dimension, _options.PerProjectCollections);
    }

    /// <inheritdoc/>
    public async Task IndexVectorAsync(
        string chunkId,
        float[] vector,
        VectorMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var collectionName = GetCollectionName(metadata?.ProjectId);
        await EnsureCollectionExistsAsync(collectionName, cancellationToken);

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
                    Id = ToDeterministicGuid(chunkId),
                    Vector = vector,
                    Payload = new QdrantPayload
                    {
                        ProjectId = metadata!.ProjectId.ToString(),
                        FilePath = metadata.FilePath,
                        StartLine = metadata.StartLine,
                        EndLine = metadata.EndLine,
                        Language = metadata.Language ?? "unknown",
                        SemanticType = !string.IsNullOrWhiteSpace(metadata.SemanticType) ? metadata.SemanticType : throw new InvalidOperationException($"semantic_type missing for chunk {chunkId}"),
                        SemanticName = !string.IsNullOrWhiteSpace(metadata.SemanticName) ? metadata.SemanticName : throw new InvalidOperationException($"semantic_name missing for chunk {chunkId}"),
                        ChunkHash = chunkId,
                        ChunkIndex = metadata.ChunkIndex,
                        TotalChunks = metadata.TotalChunks
                    }
                };

                var request = new QdrantUpsertRequest { Points = new List<QdrantPoint> { point } };

                var response = await _httpClient.PutAsJsonAsync(
                    $"/collections/{collectionName}/points",
                    request,
                    cancellationToken);

                response.EnsureSuccessStatusCode();

                // verify just-upserted point
                try
                {
                    var retrieveReq = new { ids = new[] { point.Id } };
                    var retrieveResp = await _httpClient.PostAsJsonAsync($"/collections/{collectionName}/points/retrieve", retrieveReq, cancellationToken);
                    retrieveResp.EnsureSuccessStatusCode();
                    var content = await retrieveResp.Content.ReadAsStringAsync(cancellationToken);
                    using var doc = JsonDocument.Parse(content);
                    if (doc.RootElement.TryGetProperty("result", out var resultArr) && resultArr.ValueKind == JsonValueKind.Array)
                    {
                        var item = resultArr.EnumerateArray().FirstOrDefault();
                        if (item.ValueKind == JsonValueKind.Undefined)
                            throw new InvalidOperationException("Qdrant verification failed: missing retrieved point");
                        if (item.TryGetProperty("payload", out var payload))
                        {
                            int total = payload.TryGetProperty("total_chunks", out var tc) && tc.ValueKind == JsonValueKind.Number ? tc.GetInt32() : 0;
                            int index = payload.TryGetProperty("chunk_index", out var ci) && ci.ValueKind == JsonValueKind.Number ? ci.GetInt32() : -1;
                            if (total <= 0)
                                throw new InvalidOperationException($"Qdrant verification failed for point {point.Id}: total_chunks={total}");
                            if (index < 0 || index >= total)
                                throw new InvalidOperationException($"Qdrant verification failed for point {point.Id}: chunk_index={index}, total_chunks={total}");
                        }
                        else
                        {
                            throw new InvalidOperationException("Qdrant verification failed: missing payload in retrieve response");
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException("Qdrant verification failed: unexpected retrieve response format");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Qdrant verification failed for chunk {ChunkId} in collection {Collection}", chunkId, collectionName);
                    throw;
                }

                _logger.LogDebug("Indexed vector: {ChunkId} into collection {Collection}", chunkId, collectionName);
            }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index vector {ChunkId} into collection {Collection}", chunkId, collectionName);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task IndexVectorsAsync(
        IEnumerable<(string chunkId, float[] vector, VectorMetadata metadata)> vectors,
        CancellationToken cancellationToken = default)
    {
        var vectorList = vectors.ToList();
        if (vectorList.Count == 0)
            return;
        // Group vectors by target collection (when PerProjectCollections is enabled we target per-project collections)
        var grouped = vectorList.GroupBy(v => GetCollectionName(v.metadata?.ProjectId)).ToList();

        foreach (var group in grouped)
        {
            var collection = group.Key;
            var items = group.ToList();

            // Validate vectors in this group
            var invalidVectors = new List<int>();
            for (int i = 0; i < items.Count; i++)
            {
                var v = items[i];
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
                _logger.LogError("Found {Count} invalid vectors out of {Total} for collection {Collection}. Skipping invalid vectors.",
                    invalidVectors.Count, items.Count, collection);

                foreach (var idx in invalidVectors.OrderByDescending(x => x))
                {
                    items.RemoveAt(idx);
                }

                if (items.Count == 0)
                {
                    _logger.LogWarning("All vectors were invalid for collection {Collection}; no vectors indexed", collection);
                    continue;
                }
            }

            await EnsureCollectionExistsAsync(collection, cancellationToken);

            const int batchSize = 100;
            var batches = items.Chunk(batchSize).ToList();

            _logger.LogInformation("Indexing {Count} vectors into collection {Collection} in {Batches} batches (dimension: {Dimension}D)",
                items.Count, collection, batches.Count, _options.Dimension);

            // get before count for verification (only meaningful if fail-fast enabled)
            int beforeCount = 0;
            if (_options.FailOnIndexingFailure)
            {
                beforeCount = await CountForCollectionAsync(collection, items.First().metadata?.ProjectId, cancellationToken);
            }

            int batchNumber = 0;
            foreach (var batch in batches)
            {
                batchNumber++;
                cancellationToken.ThrowIfCancellationRequested();

                var points = batch.Select(v => new QdrantPoint
                {
                    Id = ToDeterministicGuid(v.chunkId),
                    Vector = v.vector,
                    Payload = new QdrantPayload
                    {
                        ProjectId = v.metadata.ProjectId.ToString(),
                        FilePath = v.metadata.FilePath,
                        StartLine = v.metadata.StartLine,
                        EndLine = v.metadata.EndLine,
                        Language = v.metadata.Language ?? "unknown",
                        SemanticType = !string.IsNullOrWhiteSpace(v.metadata.SemanticType) ? v.metadata.SemanticType : throw new InvalidOperationException($"semantic_type missing for chunk {v.chunkId}"),
                        SemanticName = !string.IsNullOrWhiteSpace(v.metadata.SemanticName) ? v.metadata.SemanticName : throw new InvalidOperationException($"semantic_name missing for chunk {v.chunkId}"),
                        ChunkIndex = v.metadata.ChunkIndex,
                        TotalChunks = v.metadata.TotalChunks,
                        ChunkHash = v.chunkId
                    }
                }).ToList();

                var request = new QdrantUpsertRequest { Points = points };

                try
                {
                    var response = await _httpClient.PutAsJsonAsync(
                        $"/collections/{collection}/points",
                        request,
                        cancellationToken);

                    if (!response.IsSuccessStatusCode)
                    {
                        var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                        _logger.LogError("Qdrant returned {StatusCode} for collection {Collection}: {Error}",
                            response.StatusCode, collection, errorContent);
                        response.EnsureSuccessStatusCode();
                    }

                    // Verification: retrieve the upserted points and assert chunk metadata validity
                    await VerifyPointsMetadataAsync(collection, points.Select(p => p.Id), batchNumber, cancellationToken);

                    _logger.LogDebug("Successfully indexed batch {Number}/{Total} ({Count} vectors) into {Collection}",
                        batchNumber, batches.Count, batch.Length, collection);
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogError(ex, "Failed to index batch {Number}/{Total} ({Count} vectors) into {Collection}. Likely causes: vector dimension mismatch ({ConfiguredDim}D), invalid payload, or Qdrant server error",
                        batchNumber, batches.Count, batch.Length, collection, _options.Dimension);
                    throw;
                }
            }

            // verification
            if (_options.FailOnIndexingFailure)
            {
                var afterCount = await CountForCollectionAsync(collection, items.First().metadata?.ProjectId, cancellationToken);
                if (afterCount <= beforeCount)
                {
                    var msg = $"Indexing into collection {collection} did not increase stored points (before={beforeCount}, after={afterCount}).";
                    _logger.LogError(msg);
                    throw new InvalidOperationException(msg);
                }
                _logger.LogInformation("Collection {Collection} count increased from {Before} to {After}", collection, beforeCount, afterCount);
            }
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<VectorSearchResult>> QueryAsync(
        float[] queryVector,
        int topK = 10,
        Guid? projectId = null,
        CancellationToken cancellationToken = default)
    {
        var collectionName = GetCollectionName(projectId);
        await EnsureCollectionExistsAsync(collectionName, cancellationToken);

        try
        {
            var searchRequest = new QdrantSearchRequest
            {
                Vector = queryVector,
                Limit = topK,
                WithPayload = true,
                Filter = projectId.HasValue && !_options.PerProjectCollections
                    ? new QdrantFilter
                    {
                        Must = new List<QdrantCondition>
                        {
                            new QdrantCondition
                            {
                                Key = "project_id",
                                Match = new QdrantMatch { Value = projectId.Value.ToString() }
                            }
                        }
                    }
                    : null
            };

            var response = await _httpClient.PostAsJsonAsync(
                $"/collections/{collectionName}/points/search",
                searchRequest,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var searchResponse = await response.Content.ReadFromJsonAsync<QdrantSearchResponse>(cancellationToken)
                ?? throw new InvalidOperationException("Empty search response from Qdrant");

            var results = searchResponse.Result.Select(r => new VectorSearchResult
            {
                ChunkId = r.Payload?.ChunkHash ?? r.Id,
                Score = r.Score,
                Metadata = new VectorMetadata
                {
                    ProjectId = Guid.Parse(r.Payload!.ProjectId),
                    FilePath = r.Payload.FilePath,
                    StartLine = r.Payload.StartLine,
                    EndLine = r.Payload.EndLine,
                    Language = r.Payload.Language,
                    SemanticType = r.Payload.SemanticType,
                    SemanticName = r.Payload.SemanticName
                    ,ChunkIndex = r.Payload.ChunkIndex ?? 0
                    ,TotalChunks = r.Payload.TotalChunks ?? 1
                }
            }).ToList();

            _logger.LogDebug("Search returned {Count} results from collection {Collection}", results.Count, collectionName);

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query vectors from collection {Collection}", collectionName);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task DeleteByProjectIdAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        // if per-project collections are enabled, delete by posting to the specific collection(s)
        if (!_options.PerProjectCollections)
        {
            var collection = _baseCollectionName;
            if (!_collectionInitialized.TryGetValue(collection, out var exists) || !exists)
                return;

            try
            {
                var deleteRequest = new QdrantDeleteRequest
                {
                    Filter = new QdrantFilter
                    {
                        Must = new List<QdrantCondition>
                        {
                            new QdrantCondition
                            {
                                Key = "project_id",
                                Match = new QdrantMatch { Value = projectId.ToString() }
                            }
                        }
                    }
                };

                var response = await _httpClient.PostAsJsonAsync(
                    $"/collections/{collection}/points/delete",
                    deleteRequest,
                    cancellationToken);

                response.EnsureSuccessStatusCode();

                _logger.LogInformation("Deleted vectors for project {ProjectId} from collection {Collection}", projectId, collection);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete vectors for project {ProjectId} from collection {Collection}", projectId, _baseCollectionName);
                throw;
            }

            return;
        }

        // When per-project collections are enabled, remove the entire collection for the project
        var projectCollection = GetCollectionName(projectId);

        try
        {
            // Attempt to delete the collection itself (removes all points and metadata)
            var response = await _httpClient.DeleteAsync($"/collections/{projectCollection}", cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                // Remove internal tracking so future operations attempt to recreate the collection
                _collectionInitialized.TryRemove(projectCollection, out _);
                if (_initLocks.TryRemove(projectCollection, out var sem))
                {
                    try { sem.Dispose(); } catch { }
                }

                _logger.LogInformation("Deleted Qdrant collection {Collection} for project {Project}", projectCollection, projectId);
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Treat not-found as already deleted and ensure internal state is cleaned
                _collectionInitialized.TryRemove(projectCollection, out _);
                if (_initLocks.TryRemove(projectCollection, out var sem2))
                {
                    try { sem2.Dispose(); } catch { }
                }
                _logger.LogInformation("Qdrant collection {Collection} not found (treated as deleted)", projectCollection);
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Failed to delete Qdrant collection {Collection}: {Status} {Error}", projectCollection, response.StatusCode, errorContent);
                response.EnsureSuccessStatusCode();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete Qdrant collection {Collection}", projectCollection);
            throw;
        }

        // (shared-collection delete handled above for non-per-project mode)
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string chunkId, CancellationToken cancellationToken = default)
    {
        // delete across collections if per-project collections are enabled
            if (_options.PerProjectCollections)
        {
            // best-effort: try deleting from base collection and any project-specific collections
            var attempts = new[] { _baseCollectionName };
            foreach (var collection in attempts)
            {
                try
                {
                    var deleteRequest = new QdrantDeleteRequest { Points = new List<string> { ToDeterministicGuid(chunkId) } };
                    var response = await _httpClient.PostAsJsonAsync($"/collections/{collection}/points/delete", deleteRequest, cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogDebug("Deleted vector {ChunkId} from collection {Collection}", chunkId, collection);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Delete attempt failed for chunk {ChunkId} on collection {Collection}", chunkId, collection);
                }
            }

            return;
        }

        if (!_collectionInitialized.TryGetValue(_baseCollectionName, out var existsBase) || !existsBase)
            return;

        try
        {
            var deleteRequest = new QdrantDeleteRequest
            {
                Points = new List<string> { ToDeterministicGuid(chunkId) }
            };

            var response = await _httpClient.PostAsJsonAsync(
                $"/collections/{_baseCollectionName}/points/delete",
                deleteRequest,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            _logger.LogDebug("Deleted vector {ChunkId} from base collection", chunkId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete vector {ChunkId} from base collection", chunkId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<int> CountAsync(Guid? projectId = null, CancellationToken cancellationToken = default)
    {
        // Determine which collection to count in
        var collection = GetCollectionName(projectId);
        if (!_collectionInitialized.TryGetValue(collection, out var exists) || !exists)
            return 0;

        return await CountForCollectionAsync(collection, projectId, cancellationToken);
    }

    /// <summary>
    /// Ensures the collection exists, creating it if necessary
    /// </summary>
    private string GetCollectionName(Guid? projectId)
    {
        if (_options.PerProjectCollections && projectId.HasValue)
        {
            // normalize guid to compact form
            var gid = projectId.Value.ToString("N").ToLowerInvariant();
            return $"{_options.CollectionPrefix}_{gid}_vectors";
        }

        return _baseCollectionName;
    }

    private async Task EnsureCollectionExistsAsync(string collectionName, CancellationToken cancellationToken)
    {
        if (_collectionInitialized.TryGetValue(collectionName, out var exists) && exists)
            return;

        var sem = _initLocks.GetOrAdd(collectionName, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(cancellationToken);
        try
        {
            if (_collectionInitialized.TryGetValue(collectionName, out var exists2) && exists2)
                return;

            // Check if collection exists
            var checkResponse = await _httpClient.GetAsync($"/collections/{collectionName}", cancellationToken);
            if (checkResponse.IsSuccessStatusCode)
            {
                _logger.LogInformation("Collection {Collection} already exists", collectionName);
                _collectionInitialized[collectionName] = true;
                return;
            }

            // Create collection
            _logger.LogInformation("Creating collection {Collection} with dimension {Dimension}", collectionName, _options.Dimension);

            var createRequest = new QdrantCreateCollectionRequest
            {
                Vectors = new QdrantVectorConfig
                {
                    Size = _options.Dimension,
                    Distance = "Cosine"
                }
            };

            var createResponse = await _httpClient.PutAsJsonAsync($"/collections/{collectionName}", createRequest, cancellationToken);
            createResponse.EnsureSuccessStatusCode();

            // Create index on project_id for faster filtering (only relevant for shared collection)
            if (!_options.PerProjectCollections)
            {
                var indexRequest = new QdrantCreateIndexRequest
                {
                    FieldName = "project_id",
                    FieldSchema = "keyword"
                };

                var indexResponse = await _httpClient.PutAsJsonAsync($"/collections/{collectionName}/index", indexRequest, cancellationToken);
                indexResponse.EnsureSuccessStatusCode();
            }

            _collectionInitialized[collectionName] = true;
            _logger.LogInformation("Collection {Collection} created successfully", collectionName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure collection {Collection} exists", collectionName);
            throw;
        }
        finally
        {
            sem.Release();
        }
    }

    private async Task<int> CountForCollectionAsync(string collectionName, Guid? projectId, CancellationToken cancellationToken)
    {
        try
        {
            var countRequest = new QdrantCountRequest
            {
                Filter = (!_options.PerProjectCollections && projectId.HasValue)
                    ? new QdrantFilter
                    {
                        Must = new List<QdrantCondition>
                        {
                            new QdrantCondition
                            {
                                Key = "project_id",
                                Match = new QdrantMatch { Value = projectId.Value.ToString() }
                            }
                        }
                    }
                    : null
            };

            var response = await _httpClient.PostAsJsonAsync($"/collections/{collectionName}/points/count", countRequest, cancellationToken);
            response.EnsureSuccessStatusCode();
            var countResponse = await response.Content.ReadFromJsonAsync<QdrantCountResponse>(cancellationToken);
            return countResponse?.Result?.Count ?? 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to count points in collection {Collection}", collectionName);
            return 0;
        }
    }

    private async Task VerifyPointsMetadataAsync(string collection, IEnumerable<string> ids, int? batchNumber, CancellationToken cancellationToken)
    {
        try
        {
            var idList = ids.ToList();
            if (idList.Count == 0) return;

            // Try batch retrieve first
            try
            {
                var retrieveReq = new { ids = idList };
                var retrieveResp = await _httpClient.PostAsJsonAsync($"/collections/{collection}/points/retrieve", retrieveReq, cancellationToken);
                if (retrieveResp.IsSuccessStatusCode)
                {
                    var content = await retrieveResp.Content.ReadAsStringAsync(cancellationToken);
                    using var doc = JsonDocument.Parse(content);
                    if (doc.RootElement.TryGetProperty("result", out var resultArr) && resultArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in resultArr.EnumerateArray())
                        {
                            if (!item.TryGetProperty("payload", out var payload))
                                throw new InvalidOperationException("Qdrant verification failed: missing payload in retrieve response");

                            int total = payload.TryGetProperty("total_chunks", out var tc) && tc.ValueKind == JsonValueKind.Number ? tc.GetInt32() : 0;
                            int index = payload.TryGetProperty("chunk_index", out var ci) && ci.ValueKind == JsonValueKind.Number ? ci.GetInt32() : -1;
                            var id = item.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;

                            if (total <= 0)
                                throw new InvalidOperationException($"Qdrant verification failed for point {id}: total_chunks={total}");
                            if (index < 0 || index >= total)
                                throw new InvalidOperationException($"Qdrant verification failed for point {id}: chunk_index={index}, total_chunks={total}");
                        }
                        return; // success
                    }
                    throw new InvalidOperationException("Qdrant verification failed: unexpected retrieve response format");
                }

                // If retrieve endpoint not found, fall through to per-id GET
                if (retrieveResp.StatusCode != System.Net.HttpStatusCode.NotFound)
                {
                    // For other status codes, throw to be handled by outer catch
                    retrieveResp.EnsureSuccessStatusCode();
                }
            }
            catch (HttpRequestException) { throw; }
            catch (Exception ex) when (ex is InvalidOperationException == false)
            {
                // If retrieve endpoint is not supported (404) or other parsing issues, fall back
                _logger.LogDebug(ex, "Batch retrieve failed, falling back to per-id GET for collection {Collection}", collection);
            }

            // Fallback: GET each point individually
            foreach (var id in idList)
            {
                var getResp = await _httpClient.GetAsync($"/collections/{collection}/points/{id}", cancellationToken);
                if (!getResp.IsSuccessStatusCode)
                {
                    if (getResp.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        throw new InvalidOperationException($"Qdrant verification failed: point {id} not found in collection {collection}");
                    }
                    getResp.EnsureSuccessStatusCode();
                }

                var content = await getResp.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(content);
                // /points/{id} returns an object with 'result' maybe; attempt to find payload
                JsonElement payload;
                if (doc.RootElement.TryGetProperty("result", out var res) && res.ValueKind == JsonValueKind.Object && res.TryGetProperty("payload", out payload))
                {
                    // ok
                }
                else if (doc.RootElement.TryGetProperty("payload", out payload))
                {
                    // ok
                }
                else
                {
                    throw new InvalidOperationException($"Qdrant verification failed: missing payload for point {id}");
                }

                int total = payload.TryGetProperty("total_chunks", out var tc2) && tc2.ValueKind == JsonValueKind.Number ? tc2.GetInt32() : 0;
                int index = payload.TryGetProperty("chunk_index", out var ci2) && ci2.ValueKind == JsonValueKind.Number ? ci2.GetInt32() : -1;
                if (total <= 0)
                    throw new InvalidOperationException($"Qdrant verification failed for point {id}: total_chunks={total}");
                if (index < 0 || index >= total)
                    throw new InvalidOperationException($"Qdrant verification failed for point {id}: chunk_index={index}, total_chunks={total}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Qdrant verification step failed after indexing batch {Batch} into {Collection}", batchNumber, collection);
            throw;
        }
    }

    private static string ToDeterministicGuid(string input)
    {
        // Create a deterministic 16-byte value from the input and format as GUID
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var guidBytes = new byte[16];
        Array.Copy(hash, guidBytes, 16);
        var g = new Guid(guidBytes);
        return g.ToString();
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        foreach (var kv in _initLocks)
        {
            try { kv.Value?.Dispose(); } catch { }
        }
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

        [JsonPropertyName("chunk_hash")]
        public string? ChunkHash { get; init; }
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
