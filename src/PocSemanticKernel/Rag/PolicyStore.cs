using CommunityToolkit.VectorData.InMemory;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel;

namespace PocSemanticKernel.Rag;

/// <summary>
/// Creates the "store-policies" vector store collection and fills it with the documents
/// in Data/Docs. Shared by the RAG demo (Step 3) and the agents demo (Step 4).
/// </summary>
public static class PolicyStore
{
    public static async Task<VectorStoreCollection<string, DocumentChunk>> CreateAsync(Kernel kernel)
    {
        // The collection uses the kernel's embedding generator to turn text into vectors,
        // both when storing chunks and when searching.
        var embeddings = kernel.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
        var collection = new InMemoryCollection<string, DocumentChunk>(
            "store-policies", new() { EmbeddingGenerator = embeddings });

        // Ingestion: chunk + embed + store. In-memory data is lost on exit, so this runs every start.
        var docsFolder = Path.Combine(AppContext.BaseDirectory, "Data", "Docs");
        var count = await DocumentIngestor.IngestAsync(collection, docsFolder);
        Console.WriteLine($"Ingested {count} chunks from {docsFolder}\n");

        return collection;
    }
}
