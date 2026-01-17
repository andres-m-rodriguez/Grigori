using HNSW.Net;
using Microsoft.Extensions.Logging;

namespace Grigori.Core.Storage;

/// <summary>
/// Wraps an HNSW (Hierarchical Navigable Small World) index for fast approximate nearest neighbor search.
/// Provides 50-100x faster search compared to full linear scan for large codebases.
/// </summary>
public class HnswIndex : IDisposable
{
    private SmallWorld<float[], float>? _graph;
    private readonly ILogger<HnswIndex> _logger;
    private readonly object _lock = new();

    // Mapping between HNSW internal indices and database IDs
    private readonly List<long> _idMapping = [];
    private readonly Dictionary<long, int> _reverseIdMapping = [];

    // HNSW parameters
    private readonly int _m;
    private readonly int _efConstruction;
    private int _efSearch;

    /// <summary>
    /// Creates a new HNSW index with configurable parameters.
    /// </summary>
    /// <param name="logger">Logger instance</param>
    /// <param name="m">Number of connections per layer (default: 16). Higher = better recall but more memory.</param>
    /// <param name="efConstruction">Size of dynamic candidate list during construction (default: 200). Higher = better quality but slower build.</param>
    /// <param name="efSearch">Size of dynamic candidate list during search (default: 50). Higher = better recall but slower search.</param>
    public HnswIndex(ILogger<HnswIndex> logger, int m = 16, int efConstruction = 200, int efSearch = 50)
    {
        _logger = logger;
        _m = m;
        _efConstruction = efConstruction;
        _efSearch = efSearch;
    }

    /// <summary>
    /// Gets whether the index has been built and contains vectors.
    /// </summary>
    public bool IsBuilt => _graph != null && _idMapping.Count > 0;

    /// <summary>
    /// Gets the number of vectors in the index.
    /// </summary>
    public int Count => _idMapping.Count;

    /// <summary>
    /// Gets or sets the efSearch parameter (can be adjusted at runtime for recall/speed tradeoff).
    /// </summary>
    public int EfSearch
    {
        get => _efSearch;
        set => _efSearch = Math.Max(1, value);
    }

    /// <summary>
    /// Builds the HNSW index from a collection of vectors with their database IDs.
    /// </summary>
    /// <param name="vectors">Collection of (databaseId, embedding) tuples</param>
    public void BuildIndex(IEnumerable<(long Id, float[] Embedding)> vectors)
    {
        lock (_lock)
        {
            var vectorList = vectors.ToList();
            if (vectorList.Count == 0)
            {
                _logger.LogWarning("Cannot build HNSW index with no vectors");
                return;
            }

            _idMapping.Clear();
            _reverseIdMapping.Clear();

            var embeddings = new float[vectorList.Count][];
            for (int i = 0; i < vectorList.Count; i++)
            {
                embeddings[i] = vectorList[i].Embedding;
                _idMapping.Add(vectorList[i].Id);
                _reverseIdMapping[vectorList[i].Id] = i;
            }

            var parameters = new SmallWorldParameters
            {
                M = _m,
                LevelLambda = 1.0 / Math.Log(_m),
                ConstructionPruning = _efConstruction
            };

            _graph = new SmallWorld<float[], float>(CosineDistance.SIMD, new SeededRandomGenerator(42), parameters);
            _graph.AddItems(embeddings);

            _logger.LogInformation("Built HNSW index with {Count} vectors (M={M}, efConstruction={EfConstruction})",
                vectorList.Count, _m, _efConstruction);
        }
    }

    /// <summary>
    /// Adds a single vector to the index. Note: For best performance, prefer BuildIndex for bulk operations.
    /// This rebuilds the entire index as HNSW doesn't support efficient incremental adds.
    /// </summary>
    /// <param name="id">Database ID of the vector</param>
    /// <param name="embedding">The embedding vector</param>
    /// <param name="existingVectors">Function to retrieve all existing vectors for rebuild</param>
    public void AddVector(long id, float[] embedding, Func<IEnumerable<(long Id, float[] Embedding)>> existingVectors)
    {
        lock (_lock)
        {
            if (_reverseIdMapping.ContainsKey(id))
            {
                _logger.LogDebug("Vector with ID {Id} already exists in index, skipping", id);
                return;
            }

            // For incremental updates, we need to rebuild the index
            // This is a limitation of the HNSW library - in production you'd batch updates
            var allVectors = existingVectors().ToList();
            if (!allVectors.Any(v => v.Id == id))
            {
                allVectors.Add((id, embedding));
            }

            BuildIndex(allVectors);
        }
    }

    /// <summary>
    /// Removes a vector from the index by rebuilding without it.
    /// </summary>
    /// <param name="id">Database ID of the vector to remove</param>
    /// <param name="existingVectors">Function to retrieve remaining vectors for rebuild</param>
    public void RemoveVector(long id, Func<IEnumerable<(long Id, float[] Embedding)>> existingVectors)
    {
        lock (_lock)
        {
            if (!_reverseIdMapping.ContainsKey(id))
            {
                return;
            }

            var remainingVectors = existingVectors().Where(v => v.Id != id).ToList();
            if (remainingVectors.Count > 0)
            {
                BuildIndex(remainingVectors);
            }
            else
            {
                _graph = null;
                _idMapping.Clear();
                _reverseIdMapping.Clear();
            }
        }
    }

    /// <summary>
    /// Searches for approximate nearest neighbors using the HNSW index.
    /// </summary>
    /// <param name="query">Query embedding vector</param>
    /// <param name="k">Number of nearest neighbors to return</param>
    /// <returns>List of (databaseId, distance) tuples sorted by distance (ascending)</returns>
    public List<(long Id, float Distance)> Search(float[] query, int k)
    {
        lock (_lock)
        {
            if (_graph == null || _idMapping.Count == 0)
            {
                return [];
            }

            // Adjust k to not exceed available vectors
            k = Math.Min(k, _idMapping.Count);

            var results = _graph.KNNSearch(query, k);

            return results
                .Select(r => (_idMapping[r.Id], r.Distance))
                .OrderBy(r => r.Distance)
                .ToList();
        }
    }

    /// <summary>
    /// Saves the HNSW index and ID mappings to disk.
    /// </summary>
    /// <param name="indexPath">Path to save the index file</param>
    public void Save(string indexPath)
    {
        lock (_lock)
        {
            if (_graph == null || _idMapping.Count == 0)
            {
                _logger.LogWarning("Cannot save empty HNSW index");
                return;
            }

            try
            {
                // Create a combined format: [version][params][mappingCount][id1][id2]...[graphData]
                using var stream = File.Create(indexPath);
                using var writer = new BinaryWriter(stream);

                // Write version for future compatibility
                writer.Write((int)1);

                // Write HNSW parameters
                writer.Write(_m);
                writer.Write(_efConstruction);
                writer.Write(_efSearch);

                // Write ID mappings
                writer.Write(_idMapping.Count);
                foreach (var id in _idMapping)
                {
                    writer.Write(id);
                }

                // Serialize the graph to a memory stream first to get its size
                using var graphStream = new MemoryStream();
                _graph.SerializeGraph(graphStream);
                var graphData = graphStream.ToArray();

                // Write graph data
                writer.Write(graphData.Length);
                writer.Write(graphData);

                _logger.LogInformation("Saved HNSW index to {Path} ({Count} vectors, {Size} bytes)",
                    indexPath, _idMapping.Count, stream.Length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save HNSW index to {Path}", indexPath);
                throw;
            }
        }
    }

    /// <summary>
    /// Loads the HNSW index and ID mappings from disk.
    /// </summary>
    /// <param name="indexPath">Path to the saved index file</param>
    /// <param name="vectorProvider">Function to retrieve vectors by their database IDs (needed for deserialization)</param>
    /// <returns>True if loaded successfully, false if file doesn't exist or is invalid</returns>
    public bool Load(string indexPath, Func<IEnumerable<long>, IEnumerable<(long Id, float[] Embedding)>> vectorProvider)
    {
        lock (_lock)
        {
            if (!File.Exists(indexPath))
            {
                _logger.LogDebug("HNSW index file not found: {Path}", indexPath);
                return false;
            }

            try
            {
                using var stream = File.OpenRead(indexPath);
                using var reader = new BinaryReader(stream);

                // Read version
                var version = reader.ReadInt32();
                if (version != 1)
                {
                    _logger.LogWarning("Unsupported HNSW index version: {Version}", version);
                    return false;
                }

                // Read HNSW parameters (for information, we use constructor values)
                var savedM = reader.ReadInt32();
                var savedEfConstruction = reader.ReadInt32();
                var savedEfSearch = reader.ReadInt32();

                // Read ID mappings
                var mappingCount = reader.ReadInt32();
                _idMapping.Clear();
                _reverseIdMapping.Clear();

                var ids = new List<long>(mappingCount);
                for (int i = 0; i < mappingCount; i++)
                {
                    var id = reader.ReadInt64();
                    ids.Add(id);
                    _idMapping.Add(id);
                    _reverseIdMapping[id] = i;
                }

                // Read graph data
                var graphLength = reader.ReadInt32();
                var graphData = reader.ReadBytes(graphLength);

                // Get vectors for deserialization
                var vectorDict = vectorProvider(ids).ToDictionary(v => v.Id, v => v.Embedding);
                var vectors = ids.Select(id => vectorDict.TryGetValue(id, out var emb) ? emb : null).ToArray();

                // Check for missing vectors (database may have changed)
                if (vectors.Any(v => v == null))
                {
                    _logger.LogWarning("Some vectors are missing from database, HNSW index is stale");
                    _idMapping.Clear();
                    _reverseIdMapping.Clear();
                    return false;
                }

                // Create parameters for deserialization
                var parameters = new SmallWorldParameters
                {
                    M = savedM,
                    LevelLambda = 1.0 / Math.Log(savedM),
                    ConstructionPruning = savedEfConstruction
                };

                // Deserialize graph
                using var graphStream = new MemoryStream(graphData);
                var (graph, _) = SmallWorld<float[], float>.DeserializeGraph(
                    vectors!,
                    CosineDistance.SIMD,
                    new SeededRandomGenerator(42),
                    graphStream);
                _graph = graph;

                _logger.LogInformation("Loaded HNSW index from {Path} ({Count} vectors)", indexPath, mappingCount);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load HNSW index from {Path}", indexPath);
                _idMapping.Clear();
                _reverseIdMapping.Clear();
                _graph = null;
                return false;
            }
        }
    }

    /// <summary>
    /// Converts HNSW distance (which uses 1 - cosine similarity for cosine distance) back to cosine similarity score.
    /// </summary>
    /// <param name="distance">The HNSW distance value</param>
    /// <returns>Cosine similarity score (0 to 1)</returns>
    public static float DistanceToSimilarity(float distance)
    {
        // HNSW's CosineDistance returns 1 - cosine_similarity
        // So similarity = 1 - distance
        return 1.0f - distance;
    }

    public void Dispose()
    {
        _graph = null;
        _idMapping.Clear();
        _reverseIdMapping.Clear();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// A seeded random generator for reproducible HNSW graph construction.
/// </summary>
internal class SeededRandomGenerator : IProvideRandomValues
{
    private readonly Random _random;

    public SeededRandomGenerator(int seed)
    {
        _random = new Random(seed);
    }

    public bool IsThreadSafe => false;
    public int Next(int minValue, int maxValue) => _random.Next(minValue, maxValue);
    public float NextFloat() => (float)_random.NextDouble();
    public void NextFloats(Span<float> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] = (float)_random.NextDouble();
        }
    }
}
