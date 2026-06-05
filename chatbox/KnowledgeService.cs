#pragma warning disable SKEXP0020

using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Connectors.InMemory;
using SmartComponents.LocalEmbeddings;

public sealed class KnowledgeChunk
{
    [VectorStoreKey]
    public string Id { get; set; } = "";

    [VectorStoreData]
    public string Text { get; set; } = "";

    [VectorStoreData]
    public string Source { get; set; } = "";

    // all-MiniLM-L6-v2 (SmartComponents.LocalEmbeddings) outputs 384 dimensions
    [VectorStoreVector(Dimensions: 384)]
    public ReadOnlyMemory<float> Embedding { get; set; }
}

public sealed class KnowledgeService
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly VectorStoreCollection<string, KnowledgeChunk> _collection;
    private bool _initialized;

    public KnowledgeService(IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator, InMemoryVectorStore vectorStore)
    {
        _embeddingGenerator = embeddingGenerator;
        _collection = vectorStore.GetCollection<string, KnowledgeChunk>("knowledge");
    }

    public async Task EnsureCollectionAsync()
    {
        if (_initialized) return;
        await _collection.EnsureCollectionExistsAsync();
        _initialized = true;
    }

    public async Task IndexAsync(string text, string source)
    {
        GeneratedEmbeddings<Embedding<float>> embeddings = await _embeddingGenerator.GenerateAsync([text]);
        await _collection.UpsertAsync(new KnowledgeChunk
        {
            Id = Guid.NewGuid().ToString("N"),
            Text = text,
            Source = source,
            Embedding = embeddings[0].Vector
        });
    }

    public async Task<IReadOnlyList<KnowledgeChunk>> SearchAsync(string query, int topK = 3)
    {
        GeneratedEmbeddings<Embedding<float>> embeddings = await _embeddingGenerator.GenerateAsync([query]);

        List<KnowledgeChunk> chunks = [];
        await foreach (VectorSearchResult<KnowledgeChunk> result in _collection.SearchAsync(embeddings[0].Vector, topK))
            chunks.Add(result.Record);

        return chunks;
    }
}

sealed class LocalEmbeddingGeneratorAdapter : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly LocalEmbedder _inner = new();

    public EmbeddingGeneratorMetadata Metadata { get; } = new("LocalEmbedder");

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var results = values
            .Select(text => new Embedding<float>(_inner.Embed(text).Values.ToArray()))
            .ToList();
        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(results));
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() => _inner.Dispose();
}
