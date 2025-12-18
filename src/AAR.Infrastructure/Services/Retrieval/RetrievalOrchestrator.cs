// =============================================================================
// AAR.Infrastructure - Services/Retrieval/RetrievalOrchestrator.cs
// Retrieval-augmented generation orchestrator with hierarchical summarization
// =============================================================================

using System.Diagnostics;
using System.Text;
using System.IO;
using AAR.Application.DTOs;
using AAR.Application.Interfaces;
using AAR.Domain.Entities;
using AAR.Domain.Interfaces;
using AAR.Infrastructure.Services.Watchdog;
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
    private readonly IJobProgressService _progressService;
    private readonly IBatchProcessingWatchdog _watchdog;
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
        IJobProgressService progressService,
        IBatchProcessingWatchdog watchdog,
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
        _progressService = progressService;
        _watchdog = watchdog;
        _chunkerOptions = chunkerOptions.Value;
        _routerOptions = routerOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Repair vectors for a project by re-indexing chunks stored in the DB (uses stored embeddings).
    /// </summary>
    public async Task RepairProjectVectorsAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Repairing vectors for project {ProjectId}", projectId);

        var chunks = await _chunkRepository.GetWithEmbeddingsAsync(projectId, cancellationToken);
        if (chunks == null || chunks.Count == 0)
        {
            _logger.LogInformation("No chunks with embeddings found for project {ProjectId}", projectId);
            return;
        }

        var vectorBatch = new List<(string, float[], VectorMetadata)>();
        foreach (var c in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var emb = c.GetEmbedding();
            if (emb == null)
            {
                _logger.LogWarning("Skipping chunk {ChunkHash} - no embedding", c.ChunkHash);
                continue;
            }

            var semanticType = string.IsNullOrWhiteSpace(c.SemanticType) ? "file" : c.SemanticType;
            var semanticName = string.IsNullOrWhiteSpace(c.SemanticName) ? Path.GetFileName(c.FilePath) : c.SemanticName;

            var metadata = new VectorMetadata
            {
                ProjectId = c.ProjectId,
                FilePath = c.FilePath,
                StartLine = c.StartLine,
                EndLine = c.EndLine,
                Language = c.Language,
                SemanticType = semanticType,
                SemanticName = semanticName,
                TokenCount = c.TokenCount,
                Content = c.Content,
                ChunkIndex = c.ChunkIndex,
                TotalChunks = c.TotalChunks
            };

            vectorBatch.Add((c.ChunkHash, emb, metadata));
        }

        if (vectorBatch.Count == 0)
        {
            _logger.LogInformation("No vectors to repair for project {ProjectId}", projectId);
            return;
        }

        _logger.LogInformation("Re-indexing {Count} vectors for project {ProjectId}", vectorBatch.Count, projectId);
        await _vectorStore.IndexVectorsAsync(vectorBatch, cancellationToken);
        _logger.LogInformation("Repair completed for project {ProjectId}", projectId);
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
                Sources = Array.Empty<SourceReference>(),
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

    // Batch size for processing large projects - reduced for memory efficiency
    private const int ChunkBatchSize = 50;  // Reduced from 100
    private const int FileBatchSize = 25;   // Reduced from 50
    private const int EmbeddingBatchSize = 20; // Small batches for embedding API

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
        var totalFilesProcessed = 0;
        
        _logger.LogInformation("Indexing {FileCount} files for project {ProjectId}", files.Count, projectId);

        // Clear existing chunks for this project
        await _vectorStore.DeleteByProjectIdAsync(projectId, cancellationToken);
        await _chunkRepository.DeleteByProjectIdAsync(projectId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _unitOfWork.ClearChangeTracker(); // Free memory from deleted entities

        // Convert to list once and process in streaming fashion
        var fileKeys = files.Keys.ToList();
        var totalBatches = (int)Math.Ceiling((double)fileKeys.Count / FileBatchSize);
        var batchNumber = 0;

        for (var fileIndex = 0; fileIndex < fileKeys.Count; fileIndex += FileBatchSize)
        {
            batchNumber++;
            cancellationToken.ThrowIfCancellationRequested();

            // Get only the files for this batch to minimize memory
            var batchKeys = fileKeys.Skip(fileIndex).Take(FileBatchSize).ToList();
            var batchFiles = new Dictionary<string, string>(batchKeys.Count);
            foreach (var key in batchKeys)
            {
                batchFiles[key] = files[key];
            }
            
            _logger.LogInformation(
                "Processing file batch {BatchNumber}/{TotalBatches} ({FileCount} files) for project {ProjectId}",
                batchNumber, totalBatches, batchFiles.Count, projectId);

            try
            {
                // Chunk this batch of files
                var chunks = await _chunker.ChunkFilesAsync(batchFiles, projectId, cancellationToken);
                
                // Clear batch files from memory immediately
                batchFiles.Clear();
                
                if (chunks.Count == 0)
                {
                    _logger.LogDebug("No chunks created for batch {BatchNumber}", batchNumber);
                    totalFilesProcessed += batchKeys.Count;
                    continue;
                }

                _logger.LogInformation("Created {ChunkCount} chunks from batch {BatchNumber}", chunks.Count, batchNumber);

                // Process chunks in smaller batches for embeddings
                var chunkList = chunks.ToList();
                var totalChunkBatches = (int)Math.Ceiling((double)chunkList.Count / ChunkBatchSize);
                
                for (var chunkIndex = 0; chunkIndex < chunkList.Count; chunkIndex += ChunkBatchSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var chunkBatchNumber = (chunkIndex / ChunkBatchSize) + 1;
                    var currentChunkBatch = chunkList.Skip(chunkIndex).Take(ChunkBatchSize).ToList();
                    
                    // Generate embeddings in smaller sub-batches for API rate limiting
                    var embeddings = await GenerateEmbeddingsInBatchesAsync(
                        currentChunkBatch.Select(c => c.Content).ToList(),
                        cancellationToken);

                    // Prepare and save database entities
                    var dbChunks = new List<Chunk>(currentChunkBatch.Count);
                    var vectorBatch = new List<(string, float[], VectorMetadata)>(currentChunkBatch.Count);

                    for (var i = 0; i < currentChunkBatch.Count; i++)
                    {
                        var chunk = currentChunkBatch[i];
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
                            content: _chunkerOptions.StoreChunkText ? chunk.Content : null,
                            chunkIndex: chunk.ChunkIndex,
                            totalChunks: chunk.TotalChunks);

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
                            Content = chunk.Content,
                            Namespace = chunk.Namespace,
                            Responsibility = chunk.Responsibility
                            ,ChunkIndex = chunk.ChunkIndex
                            ,TotalChunks = chunk.TotalChunks
                        };

                        vectorBatch.Add((chunk.ChunkHash, embedding, metadata));
                        totalTokens += chunk.TokenCount;
                    }

                    // Save batch to database
                    await _chunkRepository.AddRangeAsync(dbChunks, cancellationToken);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    
                    // CRITICAL: Clear change tracker to prevent memory buildup
                    _unitOfWork.ClearChangeTracker();

                    // Index batch in vector store with verification
                    var beforeCount = await _vectorStore.CountAsync(projectId, cancellationToken);
                    // Index and verify vector upsert
                    var beforeCountInc = await _vectorStore.CountAsync(projectId, cancellationToken);
                    await _vectorStore.IndexVectorsAsync(vectorBatch, cancellationToken);
                    var afterCountInc = await _vectorStore.CountAsync(projectId, cancellationToken);

                    if (afterCountInc - beforeCountInc < dbChunks.Count)
                    {
                        _logger.LogWarning("Incremental indexing: indexed delta {Delta} < expected {Expected}. Retrying once.",
                            afterCountInc - beforeCountInc, dbChunks.Count);
                        await _vectorStore.IndexVectorsAsync(vectorBatch, cancellationToken);
                        var afterRetryInc = await _vectorStore.CountAsync(projectId, cancellationToken);
                        if (afterRetryInc - beforeCountInc < dbChunks.Count)
                        {
                            _logger.LogError("Incremental indexing retry did not increase vector count as expected (before: {Before}, afterRetry: {AfterRetry}).",
                                beforeCountInc, afterRetryInc);
                        }
                    }
                    var afterCount = await _vectorStore.CountAsync(projectId, cancellationToken);

                    if (afterCount - beforeCount < dbChunks.Count)
                    {
                        _logger.LogWarning("Indexed vectors count increased by {Delta} but expected {Expected}. Retrying once.",
                            afterCount - beforeCount, dbChunks.Count);
                        // Retry once
                        await _vectorStore.IndexVectorsAsync(vectorBatch, cancellationToken);
                        var afterRetry = await _vectorStore.CountAsync(projectId, cancellationToken);
                        if (afterRetry - beforeCount < dbChunks.Count)
                        {
                            _logger.LogError("Vector indexing retry did not increase count as expected (before: {Before}, afterRetry: {AfterRetry}).",
                                beforeCount, afterRetry);
                        }
                    }

                    totalChunksCreated += dbChunks.Count;
                    totalEmbeddingsGenerated += embeddings.Count;

                    _logger.LogDebug(
                        "Saved chunk batch {ChunkBatch}/{TotalChunkBatches} ({Count} chunks) - Memory: {MemoryMB:F1} MB",
                        chunkBatchNumber, totalChunkBatches, dbChunks.Count,
                        GC.GetTotalMemory(false) / 1024.0 / 1024.0);
                    
                    // Clear batch data
                    dbChunks.Clear();
                    vectorBatch.Clear();
                    embeddings.Clear();
                }
                
                // Clear chunk list after processing
                chunkList.Clear();
                totalFilesProcessed += batchKeys.Count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing file batch {BatchNumber}", batchNumber);
                errors.Add($"Batch {batchNumber}: {ex.Message}");
                totalFilesProcessed += batchKeys.Count;
            }

            // Force GC every few batches to keep memory under control
            if (batchNumber % 5 == 0)
            {
                GC.Collect(1, GCCollectionMode.Optimized, false);
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
            FilesProcessed = totalFilesProcessed,
            ChunksCreated = totalChunksCreated,
            EmbeddingsGenerated = totalEmbeddingsGenerated,
            TotalTokens = totalTokens,
            IndexingTimeMs = stopwatch.ElapsedMilliseconds,
            Errors = errors.Count > 0 ? errors : null
        };
    }

    /// <summary>
    /// Generates embeddings in smaller batches to avoid API timeouts and memory issues
    /// </summary>
    private async Task<List<float[]>> GenerateEmbeddingsInBatchesAsync(
        List<string> contents,
        CancellationToken cancellationToken)
    {
        var allEmbeddings = new List<float[]>(contents.Count);
        for (var i = 0; i < contents.Count; i += EmbeddingBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = contents.Skip(i).Take(EmbeddingBatchSize).ToList();

            try
            {
                var embeddings = await _embeddingService.CreateEmbeddingsAsync(batch, cancellationToken);

                // If the embedding service returned fewer embeddings than requested, fall back to per-item generation
                if (embeddings.Count != batch.Count)
                {
                    _logger.LogWarning("Embedding API returned {Returned}/{Requested} embeddings. Falling back to per-item generation for the batch.", embeddings.Count, batch.Count);
                    for (var j = 0; j < batch.Count; j++)
                    {
                        if (j < embeddings.Count && embeddings[j] != null)
                        {
                            allEmbeddings.Add(embeddings[j]);
                            continue;
                        }

                        // Attempt per-item retries
                        float[]? emb = null;
                        for (var attempt = 1; attempt <= 2; attempt++)
                        {
                            try
                            {
                                emb = await _embeddingService.CreateEmbeddingAsync(batch[j], cancellationToken);
                                break;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Per-item embedding attempt {Attempt} failed for batch item {Index}", attempt, j);
                                await Task.Delay(200, cancellationToken);
                            }
                        }

                        if (emb == null)
                        {
                            _logger.LogError("Failed to generate embedding for one item after retries. Inserting zero-vector to preserve ordering.");
                            emb = new float[_embeddingService.Dimension];
                        }

                        allEmbeddings.Add(emb);
                    }
                }
                else
                {
                    allEmbeddings.AddRange(embeddings);
                }

                _logger.LogDebug("Generated embeddings {Start}-{End}/{Total}",
                    i + 1, Math.Min(i + EmbeddingBatchSize, contents.Count), contents.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Batch embedding failed for items {Start}-{End}. Falling back to per-item generation.", i + 1, Math.Min(i + EmbeddingBatchSize, contents.Count));

                foreach (var text in batch)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    float[]? emb = null;
                    for (var attempt = 1; attempt <= 2; attempt++)
                    {
                        try
                        {
                            emb = await _embeddingService.CreateEmbeddingAsync(text, cancellationToken);
                            break;
                        }
                        catch (Exception ex2)
                        {
                            _logger.LogWarning(ex2, "Per-item embedding attempt {Attempt} failed", attempt);
                            await Task.Delay(200, cancellationToken);
                        }
                    }

                    if (emb == null)
                    {
                        _logger.LogError("Failed to generate embedding for one item after retries. Inserting zero-vector to preserve ordering.");
                        emb = new float[_embeddingService.Dimension];
                    }

                    allEmbeddings.Add(emb);
                }
            }
        }

        return allEmbeddings;
    }

    // Source file extensions to process
    private static readonly HashSet<string> SourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".ts", ".tsx", ".js", ".jsx", ".py", ".java", ".go", ".rs", ".cpp", ".c", ".h",
        ".hpp", ".rb", ".php", ".swift", ".kt", ".scala", ".vue", ".svelte", ".razor", ".cshtml"
    };

    // Directories to exclude
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "bin", "obj", ".git", ".vs", ".idea", ".vscode",
        "packages", "dist", "build", "__pycache__", ".venv", "venv",
        "coverage", ".nyc_output", "TestResults", ".nuget"
    };

    /// <inheritdoc/>
    public async Task<IndexingResult> IndexProjectStreamingAsync(
        Guid projectId,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var errors = new List<string>();
        var totalChunksCreated = 0;
        var totalEmbeddingsGenerated = 0;
        var totalTokens = 0;
        var totalFilesProcessed = 0;

        // Enumerate files without loading content
        var allFiles = EnumerateSourceFiles(workingDirectory).ToList();
        _logger.LogInformation("Found {FileCount} source files for project {ProjectId}", allFiles.Count, projectId);

        // Clear existing chunks for this project
        await _vectorStore.DeleteByProjectIdAsync(projectId, cancellationToken);
        await _chunkRepository.DeleteByProjectIdAsync(projectId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _unitOfWork.ClearChangeTracker();

        var totalBatches = (int)Math.Ceiling((double)allFiles.Count / StreamingFileBatchSize);
        var batchNumber = 0;

        // Track the entire indexing operation with watchdog
        using var watchdogCts = _watchdog.TrackBatch(projectId, 0, totalBatches, cancellationToken);
        var linkedCt = watchdogCts.Token;

        for (var fileIndex = 0; fileIndex < allFiles.Count; fileIndex += StreamingFileBatchSize)
        {
            batchNumber++;
            linkedCt.ThrowIfCancellationRequested();

            // Update watchdog with current batch number and phase
            _watchdog.UpdatePhase(projectId, $"Batch {batchNumber}/{totalBatches}: Starting");
            
            var batchFiles = allFiles.Skip(fileIndex).Take(StreamingFileBatchSize).ToList();
            
            _logger.LogInformation(
                "Processing streaming batch {BatchNumber}/{TotalBatches} ({FileCount} files) - Memory: {MemoryMB:F1} MB",
                batchNumber, totalBatches, batchFiles.Count,
                GC.GetTotalMemory(false) / 1024.0 / 1024.0);

            // Report indexing progress (5% to 35% range for indexing phase)
            var indexingProgress = 5 + (int)(30.0 * batchNumber / totalBatches);
            await _progressService.ReportProgressAsync(new JobProgressUpdate
            {
                ProjectId = projectId,
                Phase = "Indexing",
                ProgressPercent = indexingProgress,
                CurrentFile = $"Processing batch {batchNumber}/{totalBatches}...",
                FilesProcessed = Math.Min(fileIndex + StreamingFileBatchSize, allFiles.Count),
                TotalFiles = allFiles.Count
            }, linkedCt);

            try
            {
                // Load file contents for this batch only
                _watchdog.UpdatePhase(projectId, $"Batch {batchNumber}/{totalBatches}: Loading files");
                _logger.LogDebug("Batch {BatchNumber}: Loading file contents...", batchNumber);
                var batchContents = new Dictionary<string, string>(batchFiles.Count);
                foreach (var (fullPath, relativePath) in batchFiles)
                {
                    try
                    {
                        // Heartbeat for each file to prevent watchdog timeout
                        _watchdog.Heartbeat(projectId);
                        
                        // Use synchronous read with timeout to avoid async deadlocks
                        using var cts = CancellationTokenSource.CreateLinkedTokenSource(linkedCt);
                        cts.CancelAfter(TimeSpan.FromSeconds(30)); // 30 second timeout per file
                        
                        var content = await File.ReadAllTextAsync(fullPath, cts.Token);
                        // Skip very large files
                        if (content.Length <= MaxFileSizeBytes)
                        {
                            batchContents[relativePath] = content;
                        }
                        else
                        {
                            _logger.LogDebug("Skipping large file: {File} ({SizeKB} KB)", 
                                relativePath, content.Length / 1024);
                        }
                    }
                    catch (OperationCanceledException) when (!linkedCt.IsCancellationRequested)
                    {
                        _logger.LogWarning("Timeout reading file: {File}", relativePath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to read file: {File}", relativePath);
                    }
                }
                
                _logger.LogDebug("Batch {BatchNumber}: Loaded {FileCount} files", batchNumber, batchContents.Count);
                _watchdog.Heartbeat(projectId);

                if (batchContents.Count == 0)
                {
                    totalFilesProcessed += batchFiles.Count;
                    continue;
                }

                // Chunk this batch with timeout
                _watchdog.UpdatePhase(projectId, $"Batch {batchNumber}/{totalBatches}: Chunking");
                _logger.LogDebug("Batch {BatchNumber}: Starting chunking...", batchNumber);
                using var chunkCts = CancellationTokenSource.CreateLinkedTokenSource(linkedCt);
                chunkCts.CancelAfter(TimeSpan.FromMinutes(2)); // 2 minute timeout for chunking
                
                IReadOnlyList<ChunkInfo> chunks;
                try
                {
                    chunks = await _chunker.ChunkFilesAsync(batchContents, projectId, chunkCts.Token);
                    _watchdog.Heartbeat(projectId);
                }
                catch (OperationCanceledException) when (!linkedCt.IsCancellationRequested)
                {
                    _logger.LogWarning("Batch {BatchNumber}: Chunking timed out after 2 minutes, skipping batch", batchNumber);
                    totalFilesProcessed += batchFiles.Count;
                    errors.Add($"Batch {batchNumber}: Chunking timeout");
                    continue;
                }
                
                // Release file contents immediately
                batchContents.Clear();

                if (chunks.Count == 0)
                {
                    _logger.LogDebug("Batch {BatchNumber}: No chunks created", batchNumber);
                    totalFilesProcessed += batchFiles.Count;
                    continue;
                }

                _logger.LogDebug("Batch {BatchNumber}: Created {ChunkCount} chunks, starting embeddings...", batchNumber, chunks.Count);

                // Process chunks in small batches
                var chunkList = chunks.ToList();
                var chunkBatchNum = 0;
                for (var chunkIndex = 0; chunkIndex < chunkList.Count; chunkIndex += StreamingChunkBatchSize)
                {
                    chunkBatchNum++;
                    linkedCt.ThrowIfCancellationRequested();
                    _watchdog.Heartbeat(projectId);
                    
                    var currentChunkBatch = chunkList.Skip(chunkIndex).Take(StreamingChunkBatchSize).ToList();
                    
                    // Generate embeddings with timeout
                    _watchdog.UpdatePhase(projectId, $"Batch {batchNumber}/{totalBatches}: Embeddings {chunkBatchNum}");
                    _logger.LogDebug("Batch {BatchNumber}: Generating embeddings for chunk batch {ChunkBatch} ({Count} chunks)...", 
                        batchNumber, chunkBatchNum, currentChunkBatch.Count);
                    
                    var contents = currentChunkBatch.Select(c => c.Content).ToList();
                    
                    using var embeddingCts = CancellationTokenSource.CreateLinkedTokenSource(linkedCt);
                    embeddingCts.CancelAfter(TimeSpan.FromMinutes(1)); // Reduced to 1 minute timeout for embeddings
                    
                    IReadOnlyList<float[]> embeddings;
                    try
                    {
                        embeddings = await _embeddingService.CreateEmbeddingsAsync(contents, embeddingCts.Token);
                        _watchdog.Heartbeat(projectId);
                    }
                    catch (OperationCanceledException) when (!linkedCt.IsCancellationRequested)
                    {
                        _logger.LogWarning("Batch {BatchNumber}: Embedding generation timed out, skipping chunk batch", batchNumber);
                        continue;
                    }
                    
                    contents.Clear();
                    _logger.LogDebug("Batch {BatchNumber}: Embeddings generated, preparing DB entities...", batchNumber);

                    // Prepare database entities - don't store embedding JSON to save memory
                    var dbChunks = new List<Chunk>(currentChunkBatch.Count);
                    var vectorBatch = new List<(string, float[], VectorMetadata)>(currentChunkBatch.Count);

                    for (var i = 0; i < currentChunkBatch.Count; i++)
                    {
                        var chunk = currentChunkBatch[i];
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
                            content: null, // Don't store content in DB
                            chunkIndex: chunk.ChunkIndex,
                            totalChunks: chunk.TotalChunks);

                        // Don't store embedding JSON - vector store has it
                        // dbChunk.SetEmbedding(embedding, _embeddingService.ModelName);
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
                            Content = chunk.Content // Vector store needs content for retrieval
                            ,ChunkIndex = chunk.ChunkIndex
                            ,TotalChunks = chunk.TotalChunks
                        };

                        vectorBatch.Add((chunk.ChunkHash, embedding, metadata));
                        totalTokens += chunk.TokenCount;
                    }

                    // Save to database with timeout
                    _watchdog.UpdatePhase(projectId, $"Batch {batchNumber}/{totalBatches}: Saving DB");
                    _logger.LogDebug("Batch {BatchNumber}: Saving {Count} chunks to database...", batchNumber, dbChunks.Count);
                    using var dbCts = CancellationTokenSource.CreateLinkedTokenSource(linkedCt);
                    dbCts.CancelAfter(TimeSpan.FromSeconds(60)); // 60 second timeout for DB save
                    
                    try
                    {
                        await _chunkRepository.AddRangeAsync(dbChunks, dbCts.Token);
                        await _unitOfWork.SaveChangesAsync(dbCts.Token);
                        _unitOfWork.ClearChangeTracker();
                        _watchdog.Heartbeat(projectId);
                    }
                    catch (OperationCanceledException) when (!linkedCt.IsCancellationRequested)
                    {
                        _logger.LogWarning("Batch {BatchNumber}: Database save timed out", batchNumber);
                        _unitOfWork.ClearChangeTracker();
                        continue;
                    }

                    // Index in vector store with verification
                    _watchdog.UpdatePhase(projectId, $"Batch {batchNumber}/{totalBatches}: Vector indexing");
                    _logger.LogDebug("Batch {BatchNumber}: Indexing vectors...", batchNumber);
                    var beforeCountStream = await _vectorStore.CountAsync(projectId, linkedCt);
                    await _vectorStore.IndexVectorsAsync(vectorBatch, linkedCt);
                    var afterCountStream = await _vectorStore.CountAsync(projectId, linkedCt);

                    if (afterCountStream - beforeCountStream < dbChunks.Count)
                    {
                        _logger.LogWarning("Streaming indexed vectors increased by {Delta} but expected {Expected}. Retrying once.",
                            afterCountStream - beforeCountStream, dbChunks.Count);
                        await _vectorStore.IndexVectorsAsync(vectorBatch, linkedCt);
                        var afterRetryStream = await _vectorStore.CountAsync(projectId, linkedCt);
                        if (afterRetryStream - beforeCountStream < dbChunks.Count)
                        {
                            _logger.LogError("Streaming vector indexing retry did not increase count as expected (before: {Before}, afterRetry: {AfterRetry}).",
                                beforeCountStream, afterRetryStream);
                        }
                    }
                    _watchdog.Heartbeat(projectId);

                    totalChunksCreated += dbChunks.Count;
                    totalEmbeddingsGenerated += embeddings.Count;
                    
                    _logger.LogDebug("Batch {BatchNumber}: Chunk batch complete", batchNumber);

                    // Clear immediately
                    dbChunks.Clear();
                    vectorBatch.Clear();
                    currentChunkBatch.Clear();
                }

                _logger.LogInformation("Batch {BatchNumber}/{TotalBatches} completed successfully", batchNumber, totalBatches);
                chunkList.Clear();
                totalFilesProcessed += batchFiles.Count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in streaming batch {BatchNumber}", batchNumber);
                errors.Add($"Batch {batchNumber}: {ex.Message}");
                totalFilesProcessed += batchFiles.Count;
            }

            // Non-blocking GC hint for large projects - let runtime decide when to collect
            if (batchNumber % 5 == 0)
            {
                // Use non-blocking, non-compacting collection to avoid freezing
                GC.Collect(1, GCCollectionMode.Optimized, blocking: false);
                _logger.LogDebug("GC hint issued - Memory: {MemoryMB:F1} MB", 
                    GC.GetTotalMemory(false) / 1024.0 / 1024.0);
            }
        }

        stopwatch.Stop();

        // Mark project as complete in watchdog
        _watchdog.Complete(projectId);

        _logger.LogInformation(
            "Streaming indexed {ChunkCount} chunks ({TotalTokens} tokens) for project {ProjectId} in {Time}ms",
            totalChunksCreated, totalTokens, projectId, stopwatch.ElapsedMilliseconds);

        return new IndexingResult
        {
            FilesProcessed = totalFilesProcessed,
            ChunksCreated = totalChunksCreated,
            EmbeddingsGenerated = totalEmbeddingsGenerated,
            TotalTokens = totalTokens,
            IndexingTimeMs = stopwatch.ElapsedMilliseconds,
            Errors = errors.Count > 0 ? errors : null
        };
    }

    /// <summary>
    /// Enumerates source files without loading content
    /// </summary>
    private IEnumerable<(string FullPath, string RelativePath)> EnumerateSourceFiles(string workingDirectory)
    {
        foreach (var file in Directory.EnumerateFiles(workingDirectory, "*.*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(workingDirectory, file);
            var extension = Path.GetExtension(file).ToLowerInvariant();

            _logger.LogDebug("Discovered file: {File} (ext: {Ext})", relativePath, extension);

            // Skip non-source files
            if (!SourceExtensions.Contains(extension))
            {
                _logger.LogTrace("Skipping file due to unsupported extension: {File}", relativePath);
                continue;
            }

            // Skip excluded directories
            var shouldSkip = false;
            string skipReason = string.Empty;
            foreach (var dir in ExcludedDirectories)
            {
                if (relativePath.Contains($"{Path.DirectorySeparatorChar}{dir}{Path.DirectorySeparatorChar}") ||
                    relativePath.Contains($"{Path.AltDirectorySeparatorChar}{dir}{Path.AltDirectorySeparatorChar}") ||
                    relativePath.StartsWith($"{dir}{Path.DirectorySeparatorChar}") ||
                    relativePath.StartsWith($"{dir}{Path.AltDirectorySeparatorChar}"))
                {
                    shouldSkip = true;
                    skipReason = $"excluded directory: {dir}";
                    break;
                }
            }

            if (shouldSkip)
            {
                _logger.LogTrace("Skipping file {File}: {Reason}", relativePath, skipReason);
                continue;
            }

            _logger.LogDebug("Accepting file for processing: {File}", relativePath);
            yield return (file, relativePath);
        }
    }

    // Streaming batch sizes - very small to minimize memory
    private const int StreamingFileBatchSize = 10;  // Only 10 files at a time
    private const int StreamingChunkBatchSize = 25; // Small chunk batches
    private const int MaxFileSizeBytes = 200_000;   // 200KB max file size

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
        var totalFilesProcessed = 0;
        
        _logger.LogInformation("Incremental indexing {FileCount} files for project {ProjectId}", 
            files.Count, projectId);

        // Get existing chunk hashes - only load hashes, not full entities
        var existingHashes = new HashSet<string>();
        var existingChunks = await _chunkRepository.GetByProjectIdAsync(projectId, cancellationToken);
        foreach (var chunk in existingChunks)
        {
            existingHashes.Add(chunk.ChunkHash);
        }
        
        // Clear tracked entities from the query
        _unitOfWork.ClearChangeTracker();

        _logger.LogInformation("Found {ExistingCount} existing chunks for project {ProjectId}", 
            existingHashes.Count, projectId);

        // Process files in streaming fashion
        var fileKeys = files.Keys.ToList();
        var totalBatches = (int)Math.Ceiling((double)fileKeys.Count / FileBatchSize);
        var batchNumber = 0;

        for (var fileIndex = 0; fileIndex < fileKeys.Count; fileIndex += FileBatchSize)
        {
            batchNumber++;
            cancellationToken.ThrowIfCancellationRequested();

            var batchKeys = fileKeys.Skip(fileIndex).Take(FileBatchSize).ToList();
            var batchFiles = new Dictionary<string, string>(batchKeys.Count);
            foreach (var key in batchKeys)
            {
                batchFiles[key] = files[key];
            }

            _logger.LogInformation(
                "Processing incremental batch {BatchNumber}/{TotalBatches} ({FileCount} files)",
                batchNumber, totalBatches, batchFiles.Count);

            try
            {
                // Chunk this batch of files
                var allChunks = await _chunker.ChunkFilesAsync(batchFiles, projectId, cancellationToken);
                batchFiles.Clear();

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
                        existingHashes.Add(chunk.ChunkHash);
                    }
                }

                if (newChunks.Count == 0)
                {
                    _logger.LogDebug("No new chunks in batch {BatchNumber}", batchNumber);
                    totalFilesProcessed += batchKeys.Count;
                    continue;
                }

                _logger.LogInformation("Found {NewCount} new chunks in batch {BatchNumber}", newChunks.Count, batchNumber);

                // Process new chunks in smaller batches
                for (var chunkIndex = 0; chunkIndex < newChunks.Count; chunkIndex += ChunkBatchSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var chunkList = newChunks.Skip(chunkIndex).Take(ChunkBatchSize).ToList();

                    // Generate embeddings in smaller sub-batches
                    var embeddings = await GenerateEmbeddingsInBatchesAsync(
                        chunkList.Select(c => c.Content).ToList(),
                        cancellationToken);

                    // Prepare database entities and vector data
                    var vectorBatch = new List<(string, float[], VectorMetadata)>(chunkList.Count);
                    var dbChunks = new List<Chunk>(chunkList.Count);

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
                            content: _chunkerOptions.StoreChunkText ? chunk.Content : null,
                            chunkIndex: chunk.ChunkIndex,
                            totalChunks: chunk.TotalChunks);

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
                            ,ChunkIndex = chunk.ChunkIndex
                            ,TotalChunks = chunk.TotalChunks
                        };

                        vectorBatch.Add((chunk.ChunkHash, embedding, metadata));
                        totalTokens += chunk.TokenCount;
                    }

                    // Save batch to database
                    await _chunkRepository.AddRangeAsync(dbChunks, cancellationToken);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    _unitOfWork.ClearChangeTracker();
                    
                    await _vectorStore.IndexVectorsAsync(vectorBatch, cancellationToken);

                    totalChunksCreated += dbChunks.Count;
                    totalEmbeddingsGenerated += embeddings.Count;
                    
                    dbChunks.Clear();
                    vectorBatch.Clear();
                    embeddings.Clear();
                }
                
                newChunks.Clear();
                totalFilesProcessed += batchKeys.Count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in incremental batch {BatchNumber}", batchNumber);
                errors.Add($"Batch {batchNumber}: {ex.Message}");
                totalFilesProcessed += batchKeys.Count;
            }

            if (batchNumber % 5 == 0)
            {
                GC.Collect(1, GCCollectionMode.Optimized, false);
            }
        }

        stopwatch.Stop();

        _logger.LogInformation(
            "Incrementally indexed {ChunkCount} new chunks, skipped {Skipped} for project {ProjectId} in {Time}ms",
            totalChunksCreated, totalChunksSkipped, projectId, stopwatch.ElapsedMilliseconds);

        return new IndexingResult
        {
            FilesProcessed = totalFilesProcessed,
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
