// =============================================================================
// AAR.Infrastructure - Services/Retrieval/RetrievalOrchestrator.cs
// Retrieval-augmented generation orchestrator with hierarchical summarization
// =============================================================================

using System.Diagnostics;
using System.Text;
using AAR.Application.DTOs;
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
    private readonly IJobProgressService _progressService;
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
                    
                    // CRITICAL: Clear change tracker to prevent memory buildup
                    _unitOfWork.ClearChangeTracker();

                    // Index batch in vector store
                    await _vectorStore.IndexVectorsAsync(vectorBatch, cancellationToken);

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
            var embeddings = await _embeddingService.CreateEmbeddingsAsync(batch, cancellationToken);
            allEmbeddings.AddRange(embeddings);
            
            _logger.LogDebug("Generated embeddings {Start}-{End}/{Total}",
                i + 1, Math.Min(i + EmbeddingBatchSize, contents.Count), contents.Count);
        }

        return allEmbeddings;
    }

    // Source file extensions to process
    private static readonly HashSet<string> SourceExtensions =
    [
        ".cs", ".ts", ".tsx", ".js", ".jsx", ".py", ".java", ".go", ".rs", ".cpp", ".c", ".h",
        ".hpp", ".rb", ".php", ".swift", ".kt", ".scala", ".vue", ".svelte", ".razor", ".cshtml"
    ];

    // Directories to exclude
    private static readonly HashSet<string> ExcludedDirectories =
    [
        "node_modules", "bin", "obj", ".git", ".vs", ".idea", ".vscode",
        "packages", "dist", "build", "__pycache__", ".venv", "venv",
        "coverage", ".nyc_output", "TestResults", ".nuget"
    ];

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

        for (var fileIndex = 0; fileIndex < allFiles.Count; fileIndex += StreamingFileBatchSize)
        {
            batchNumber++;
            cancellationToken.ThrowIfCancellationRequested();

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
            }, cancellationToken);

            try
            {
                // Load file contents for this batch only
                _logger.LogDebug("Batch {BatchNumber}: Loading file contents...", batchNumber);
                var batchContents = new Dictionary<string, string>(batchFiles.Count);
                foreach (var (fullPath, relativePath) in batchFiles)
                {
                    try
                    {
                        // Use synchronous read with timeout to avoid async deadlocks
                        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
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
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogWarning("Timeout reading file: {File}", relativePath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to read file: {File}", relativePath);
                    }
                }
                
                _logger.LogDebug("Batch {BatchNumber}: Loaded {FileCount} files", batchNumber, batchContents.Count);

                if (batchContents.Count == 0)
                {
                    totalFilesProcessed += batchFiles.Count;
                    continue;
                }

                // Chunk this batch with timeout
                _logger.LogDebug("Batch {BatchNumber}: Starting chunking...", batchNumber);
                using var chunkCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                chunkCts.CancelAfter(TimeSpan.FromMinutes(2)); // 2 minute timeout for chunking
                
                IReadOnlyList<ChunkInfo> chunks;
                try
                {
                    chunks = await _chunker.ChunkFilesAsync(batchContents, projectId, chunkCts.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
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
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var currentChunkBatch = chunkList.Skip(chunkIndex).Take(StreamingChunkBatchSize).ToList();
                    
                    // Generate embeddings with timeout
                    _logger.LogDebug("Batch {BatchNumber}: Generating embeddings for chunk batch {ChunkBatch} ({Count} chunks)...", 
                        batchNumber, chunkBatchNum, currentChunkBatch.Count);
                    
                    var contents = currentChunkBatch.Select(c => c.Content).ToList();
                    
                    using var embeddingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    embeddingCts.CancelAfter(TimeSpan.FromMinutes(2)); // 2 minute timeout for embeddings
                    
                    IReadOnlyList<float[]> embeddings;
                    try
                    {
                        embeddings = await _embeddingService.CreateEmbeddingsAsync(contents, embeddingCts.Token);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
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
                            content: null); // Don't store content in DB

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
                        };

                        vectorBatch.Add((chunk.ChunkHash, embedding, metadata));
                        totalTokens += chunk.TokenCount;
                    }

                    // Save to database with timeout
                    _logger.LogDebug("Batch {BatchNumber}: Saving {Count} chunks to database...", batchNumber, dbChunks.Count);
                    using var dbCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    dbCts.CancelAfter(TimeSpan.FromSeconds(60)); // 60 second timeout for DB save
                    
                    try
                    {
                        await _chunkRepository.AddRangeAsync(dbChunks, dbCts.Token);
                        await _unitOfWork.SaveChangesAsync(dbCts.Token);
                        _unitOfWork.ClearChangeTracker();
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogWarning("Batch {BatchNumber}: Database save timed out", batchNumber);
                        _unitOfWork.ClearChangeTracker();
                        continue;
                    }

                    // Index in vector store
                    _logger.LogDebug("Batch {BatchNumber}: Indexing vectors...", batchNumber);
                    await _vectorStore.IndexVectorsAsync(vectorBatch, cancellationToken);

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

            // Skip non-source files
            if (!SourceExtensions.Contains(extension))
                continue;

            // Skip excluded directories
            var shouldSkip = false;
            foreach (var dir in ExcludedDirectories)
            {
                if (relativePath.Contains($"{Path.DirectorySeparatorChar}{dir}{Path.DirectorySeparatorChar}") ||
                    relativePath.Contains($"{Path.AltDirectorySeparatorChar}{dir}{Path.AltDirectorySeparatorChar}") ||
                    relativePath.StartsWith($"{dir}{Path.DirectorySeparatorChar}") ||
                    relativePath.StartsWith($"{dir}{Path.AltDirectorySeparatorChar}"))
                {
                    shouldSkip = true;
                    break;
                }
            }

            if (!shouldSkip)
            {
                yield return (file, relativePath);
            }
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
