using Microsoft.Extensions.VectorData;

namespace PocSemanticKernel.Rag;

/// <summary>
/// One searchable piece of a document, as stored in the vector store.
/// The attributes tell the vector store what each property is.
/// </summary>
public sealed class DocumentChunk
{
    [VectorStoreKey]
    public required string Id { get; init; }          // e.g. "returns-policy.md#2"

    [VectorStoreData]
    public required string Source { get; init; }      // file name, used for citations

    [VectorStoreData]
    public required string Heading { get; init; }     // section title, e.g. "Return window"

    [VectorStoreData]
    public required string Text { get; init; }        // the chunk content

    // The vector. Because it is a string, the store calls the embedding generator
    // to turn it into 1536 numbers (the size of text-embedding-3-small) on upsert.
    [VectorStoreVector(1536)]
    public string Embedding => $"{Heading}\n{Text}";
}
