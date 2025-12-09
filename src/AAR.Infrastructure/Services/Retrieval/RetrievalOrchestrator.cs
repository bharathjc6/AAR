// =============================================================================
// AAR.Infrastructure - Services/Retrieval/RetrievalOrchestrator.cs
// Retrieval-augmented generation orchestrator with hierarchical summarization
// =============================================================================

using System.Diagnostics;
using System.Text;
using AAR.Application.Interfaces;
using AAR.Domain.Entities;
using AAR.Domain.Interfaces;
using AAR.Shared.Tokenization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AAR.Infrastructure.Services.Retrieval;

/// <summary>
/// Orchestrates retrieval-augmented generation with hierarchical summarization.
/// </summary>
public class RetrievalOrchestrator : IRetrievalOrchestrator
{
    private readonly IChunker _chunker;
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStore _vectorStore;
    private readonly IOpenAiService _openAiService;
    private readonly IChunkRepository _chunkRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITokenizer _tokenizer;
    private readonly ChunkerOptions _chunkerOptions;
    private readonly ModelRouterOptions _routerOptions;
    private readonly ILogger<RetrievalOrchestrator> _logger;

    public RetrievalOrchestrator(
        IChunker chunker,
        IEmbeddingService embeddingService,
        IVectorStore vectorStore,
        IOpenAiService openAiService,
        IChunkRepository chunkRepository,
        IUnitOfWork unitOfWork,
        ITokenizerFactory tokenizerFactory,
        IOptions<ChunkerOptions> chunkerOptions,
        IOptions<ModelRouterOptions> routerOptions,
        ILogger<RetrievalOrchestrator> logger)
    {
        _chunker = chunker;
        _embeddingService = embeddingService;
        _vectorStore = vectorStore;
        _openAiService = openAiService;
        _chunkRepository = chunkRepository;
        _unitOfWork = unitOfWork;
        _tokenizer = tokenizerFactory.Create();
        _chunkerOptions = chunkerOptions.Value;
        _routerOptions = routerOptions.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<RetrievalResult> RetrieveContextAsync(
        Guid projectId,
        string query,
        int maxTokens = 8000,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        
        _logger.LogInformation("Retrieving context for project {ProjectId}, max tokens: {MaxTokens}", 
            projectId, maxTokens);

        // Generate query embedding
        var queryEmbedding = await _embeddingService.CreateEmbeddingAsync(query, cancellationToken);

        // Retrieve top-K chunks
        var searchResults = await _vectorStore.QueryAsync(
            queryEmbedding, 
            _routerOptions.TopK, 
            projectId, 
            cancellationToken);

        if (searchResults.Count == 0)
        {
            _logger.LogWarning("No chunks found for project {ProjectId}", projectId);
            return new RetrievalResult
            {
                Context = string.Empty,
                TokenCount = 0,
                Sources = [],
                WasSummarized = false,
                RawChunkCount = 0,
                RetrievalTimeMs = stopwatch.ElapsedMilliseconds
            };
        }

        // Calculate total tokens
        var totalTokens = searchResults.Sum(r => r.Metadata.TokenCount);
        var sources = searchResults.Select(r => new SourceReference
        {
            ChunkId = r.ChunkId,
            FilePath = r.Metadata.FilePath,
            StartLine = r.Metadata.StartLine,
            EndLine = r.Metadata.EndLine,
            Score = r.Score,
            SemanticType = r.Metadata.SemanticType,
            SemanticName = r.Metadata.SemanticName
        }).ToList();

        string context;
        var wasSummarized = false;

        if (totalTokens > _routerOptions.SummarizationThreshold)
        {
            _logger.LogInformation("Total tokens ({TotalTokens}) exceeds threshold ({Threshold}), using hierarchical summarization",
                totalTokens, _routerOptions.SummarizationThreshold);
            
            context = await HierarchicalSummarizeAsync(searchResults, maxTokens, cancellationToken);
            wasSummarized = true;
        }
        else
        {
            context = BuildDirectContext(searchResults);
        }

        var finalTokenCount = _tokenizer.CountTokens(context);
        stopwatch.Stop();

        _logger.LogInformation("Retrieved context: {TokenCount} tokens, {SourceCount} sources, summarized: {Summarized}, time: {Time}ms",
            finalTokenCount, sources.Count, wasSummarized, stopwatch.ElapsedMilliseconds);

        return new RetrievalResult
        {
            Context = context,
            TokenCount = finalTokenCount,
            Sources = sources,
            WasSummarized = wasSummarized,
            RawChunkCount = searchResults.Count,
            RetrievalTimeMs = stopwatch.ElapsedMilliseconds
        };
    }

    // Batch size for processing large projects
    private const int ChunkBatchSize = 100;
    private const int FileBatchSize = 50;

    /// <inheritdoc/>
    public async Task<IndexingResult> IndexProjectAsync(
        Guid projectId,
        IDictionary<string, string> files,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var errors = new List<string>();
        var totalChunksCreated = 0;
        var totalEmbeddingsGenerated = 0;
        var totalTokens = 0;
        
        _logger.LogInformation("Indexing {FileCount} files for project {ProjectId}", files.Count, projectId);

        // Clear existing chunks for this project
        await _vectorStore.DeleteByProjectIdAsync(projectId, cancellationToken);
        await _chunkRepository.DeleteByProjectIdAsync(projectId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Process files in batches to avoid memory issues
        var fileList = files.ToList();
        var fileBatches = fileList.Chunk(FileBatchSize).ToList();
        var batchNumber = 0;

        foreach (var fileBatch in fileBatches)
        {
            batchNumber++;
            cancellationToken.ThrowIfCancellationRequested();
            
            _logger.LogInformation(
                "Processing file batch {BatchNumber}/{TotalBatches} ({FileCount} files) for project {ProjectId}",
                batchNumber, fileBatches.Count, fileBatch.Length, projectId);

            // Chunk this batch of files
            var batchFiles = fileBatch.ToDictionary(f => f.Key, f => f.Value);
            var chunks = await _chunker.ChunkFilesAsync(batchFiles, projectId, cancellationToken);
            
            if (chunks.Count == 0)
            {
                _logger.LogDebug("No chunks created for batch {BatchNumber}", batchNumber);
                continue;
            }

            _logger.LogInformation("Created {ChunkCount} chunks from batch {BatchNumber}", chunks.Count, batchNumber);

            // Process chunks in smaller batches for embeddings and DB
            var chunkBatches = chunks.Chunk(ChunkBatchSize).ToList();
            var chunkBatchNumber = 0;

            foreach (var chunkBatch in chunkBatches)
            {
                chunkBatchNumber++;
                cancellationToken.ThrowIfCancellationRequested();

                var chunkList = chunkBatch.ToList();
                
                // Generate embeddings for this batch
                var chunkContents = chunkList.Select(c => c.Content).ToList();
                var embeddings = await _embeddingService.CreateEmbeddingsAsync(chunkContents, cancellationToken);

                // Prepare database entities and vector data
                var vectorBatch = new List<(string, float[], VectorMetadata)>();
                var dbChunks = new List<Chunk>();

                for (var i = 0; i < chunkList.Count; i++)
                {
                    var chunk = chunkList[i];
                    var embedding = embeddings[i];

                    var dbChunk = Chunk.Create(
                        projectId: chunk.ProjectId,
                        filePath: chunk.FilePath,
                        startLine: chunk.StartLine,
                        endLine: chunk.EndLine,
                        tokenCount: chunk.TokenCount,
                        language: chunk.Language,
                        textHash: chunk.TextHash,
                        chunkHash: chunk.ChunkHash,
                        semanticType: chunk.SemanticType,
                        semanticName: chunk.SemanticName,
                        content: _chunkerOptions.StoreChunkText ? chunk.Content : null);

                    dbChunk.SetEmbedding(embedding, _embeddingService.ModelName);
                    dbChunks.Add(dbChunk);

                    var metadata = new VectorMetadata
                    {
                        ProjectId = chunk.ProjectId,
                        FilePath = chunk.FilePath,
                        StartLine = chunk.StartLine,
                        EndLine = chunk.EndLine,
                        Language = chunk.Language,
                        SemanticType = chunk.SemanticType,
                        SemanticName = chunk.SemanticName,
                        TokenCount = chunk.TokenCount,
                        Content = chunk.Content
                    };

                    vectorBatch.Add((chunk.ChunkHash, embedding, metadata));
                    totalTokens += chunk.TokenCount;
                }

                // Save batch to database
                await _chunkRepository.AddRangeAsync(dbChunks, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                // Index batch in vector store
                await _vectorStore.IndexVectorsAsync(vectorBatch, cancellationToken);

                totalChunksCreated += dbChunks.Count;
                totalEmbeddingsGenerated += embeddings.Count;

                _logger.LogDebug(
                    "Saved chunk batch {ChunkBatch}/{TotalChunkBatches} ({Count} chunks)",
                    chunkBatchNumber, chunkBatches.Count, dbChunks.Count);
            }
        }

        stopwatch.Stop();

        if (totalChunksCreated == 0)
        {
            _logger.LogWarning("No chunks created for project {ProjectId}", projectId);
        }
        else
        {
            _logger.LogInformation(
                "Indexed {ChunkCount} chunks ({TotalTokens} tokens) for project {ProjectId} in {Time}ms",
                totalChunksCreated, totalTokens, projectId, stopwatch.ElapsedMilliseconds);
        }

        return new IndexingResult
        {
            FilesProcessed = files.Count,
            ChunksCreated = totalChunksCreated,
            EmbeddingsGenerated = totalEmbeddingsGenerated,
            TotalTokens = totalTokens,
            IndexingTimeMs = stopwatch.ElapsedMilliseconds,
            Errors = errors.Count > 0 ? errors : null
        };
    }

    /// <inheritdoc/>
    public async Task<IndexingResult> IncrementalIndexAsync(
        Guid projectId,
        IDictionary<string, string> files,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var errors = new List<string>();
        var totalChunksSkipped = 0;
        var totalChunksCreated = 0;
        var totalEmbeddingsGenerated = 0;
        var totalTokens = 0;
        
        _logger.LogInformation("Incremental indexing {FileCount} files for project {ProjectId}", 
            files.Count, projectId);

        // Get existing chunk hashes
        var existingHashes = new HashSet<string>();
        var existingChunks = await _chunkRepository.GetByProjectIdAsync(projectId, cancellationToken);
        foreach (var chunk in existingChunks)
        {
            existingHashes.Add(chunk.ChunkHash);
        }

        _logger.LogInformation("Found {ExistingCount} existing chunks for project {ProjectId}", 
            existingHashes.Count, projectId);

        // Process files in batches
        var fileList = files.ToList();
        var fileBatches = fileList.Chunk(FileBatchSize).ToList();
        var batchNumber = 0;

        foreach (var fileBatch in fileBatches)
        {
            batchNumber++;
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation(
                "Processing incremental batch {BatchNumber}/{TotalBatches} ({FileCount} files)",
                batchNumber, fileBatches.Count, fileBatch.Length);

            // Chunk this batch of files
            var batchFiles = fileBatch.ToDictionary(f => f.Key, f => f.Value);
            var allChunks = await _chunker.ChunkFilesAsync(batchFiles, projectId, cancellationToken);

            // Filter out existing chunks
            var newChunks = new List<ChunkInfo>();
            foreach (var chunk in allChunks)
            {
                if (existingHashes.Contains(chunk.ChunkHash))
                {
                    totalChunksSkipped++;
                }
                else
                {
                    newChunks.Add(chunk);
                    existingHashes.Add(chunk.ChunkHash); // Track to avoid duplicates within batches
                }
            }

            if (newChunks.Count == 0)
            {
                _logger.LogDebug("No new chunks in batch {BatchNumber}", batchNumber);
                continue;
            }

            // Process new chunks in smaller batches
            var chunkBatches = newChunks.Chunk(ChunkBatchSize).ToList();

            foreach (var chunkBatch in chunkBatches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chunkList = chunkBatch.ToList();

                // Generate embeddings for this batch
                var chunkContents = chunkList.Select(c => c.Content).ToList();
                var embeddings = await _embeddingService.CreateEmbeddingsAsync(chunkContents, cancellationToken);

                // Prepare database entities and vector data
                var vectorBatch = new List<(string, float[], VectorMetadata)>();
                var dbChunks = new List<Chunk>();

                for (var i = 0; i < chunkList.Count; i++)
                {
                    var chunk = chunkList[i];
                    var embedding = embeddings[i];

                    var dbChunk = Chunk.Create(
                        projectId: chunk.ProjectId,
                        filePath: chunk.FilePath,
                        startLine: chunk.StartLine,
                        endLine: chunk.EndLine,
                        tokenCount: chunk.TokenCount,
                        language: chunk.Language,
                        textHash: chunk.TextHash,
                        chunkHash: chunk.ChunkHash,
                        semanticType: chunk.SemanticType,
                        semanticName: chunk.SemanticName,
                        content: _chunkerOptions.StoreChunkText ? chunk.Content : null);

                    dbChunk.SetEmbedding(embedding, _embeddingService.ModelName);
                    dbChunks.Add(dbChunk);

                    var metadata = new VectorMetadata
                    {
                        ProjectId = chunk.ProjectId,
                        FilePath = chunk.FilePath,
                        StartLine = chunk.StartLine,
                        EndLine = chunk.EndLine,
                        Language = chunk.Language,
                        SemanticType = chunk.SemanticType,
                        SemanticName = chunk.SemanticName,
                        TokenCount = chunk.TokenCount,
                        Content = chunk.Content
                    };

                    vectorBatch.Add((chunk.ChunkHash, embedding, metadata));
                    totalTokens += chunk.TokenCount;
                }

                // Save batch to database
                await _chunkRepository.AddRangeAsync(dbChunks, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _vectorStore.IndexVectorsAsync(vectorBatch, cancellationToken);

                totalChunksCreated += dbChunks.Count;
                totalEmbeddingsGenerated += embeddings.Count;
            }
        }

        stopwatch.Stop();

        _logger.LogInformation(
            "Incrementally indexed {ChunkCount} new chunks, skipped {Skipped} for project {ProjectId} in {Time}ms",
            totalChunksCreated, totalChunksSkipped, projectId, stopwatch.ElapsedMilliseconds);

        return new IndexingResult
        {
            FilesProcessed = files.Count,
            ChunksCreated = totalChunksCreated,
            EmbeddingsGenerated = totalEmbeddingsGenerated,
            TotalTokens = totalTokens,
            IndexingTimeMs = stopwatch.ElapsedMilliseconds,
            ChunksSkipped = totalChunksSkipped,
            Errors = errors.Count > 0 ? errors : null
        };
    }

    private async Task<string> HierarchicalSummarizeAsync(
        IReadOnlyList<VectorSearchResult> chunks,
        int maxTokens,
        CancellationToken cancellationToken)
    {
        // Bucket chunks
        var buckets = new List<List<VectorSearchResult>>();
        for (var i = 0; i < chunks.Count; i += _routerOptions.ChunksPerBucket)
        {
            buckets.Add(chunks.Skip(i).Take(_routerOptions.ChunksPerBucket).ToList());
        }

        _logger.LogDebug("Created {BucketCount} buckets for summarization", buckets.Count);

        // Summarize each bucket
        var summaries = new List<string>();
        foreach (var bucket in buckets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var bucketContent = BuildDirectContext(bucket);
            var summary = await SummarizeBucketAsync(bucketContent, cancellationToken);
            summaries.Add(summary);
        }

        // Combine summaries
        var combined = new StringBuilder();
        combined.AppendLine("# Code Analysis Context (Summarized)");
        combined.AppendLine();
        
        for (var i = 0; i < summaries.Count; i++)
        {
            combined.AppendLine($"## Section {i + 1}");
            combined.AppendLine(summaries[i]);
            combined.AppendLine();
        }

        var result = combined.ToString();
        var tokenCount = _tokenizer.CountTokens(result);

        // If still too long, recursively summarize
        if (tokenCount > maxTokens && summaries.Count > 1)
        {
            _logger.LogDebug("Combined summaries ({Tokens} tokens) still exceed max, re-summarizing", tokenCount);
            return await SummarizeBucketAsync(result, cancellationToken);
        }

        return result;
    }

    private async Task<string> SummarizeBucketAsync(string content, CancellationToken cancellationToken)
    {
        var prompt = $@"Summarize the following code sections concisely, preserving:
1. Key classes, methods, and their purposes
2. Important patterns and architectures used
3. Notable dependencies and relationships
4. Any potential issues or concerns

Keep the summary technical and focused. Include file paths and line ranges for key elements.

Code sections:
{content}

Provide a structured summary:";

        var summary = await _openAiService.AnalyzeCodeAsync(prompt, "summarizer", cancellationToken);
        return summary;
    }

    private static string BuildDirectContext(IReadOnlyList<VectorSearchResult> chunks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Retrieved Code Context");
        sb.AppendLine();

        foreach (var chunk in chunks)
        {
            var semanticInfo = !string.IsNullOrEmpty(chunk.Metadata.SemanticType)
                ? $" ({chunk.Metadata.SemanticType}: {chunk.Metadata.SemanticName})"
                : "";

            sb.AppendLine($"## {chunk.Metadata.FilePath}:{chunk.Metadata.StartLine}-{chunk.Metadata.EndLine}{semanticInfo}");
            sb.AppendLine($"Score: {chunk.Score:F3}");
            sb.AppendLine("```" + (chunk.Metadata.Language ?? ""));
            sb.AppendLine(chunk.Metadata.Content ?? "[Content not stored]");
            sb.AppendLine("```");
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
