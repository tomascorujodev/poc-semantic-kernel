using Microsoft.Extensions.VectorData;

namespace PocSemanticKernel.Rag;

/// <summary>
/// Ingestion: reads Markdown files, splits them into chunks (one per "## " section)
/// and stores them in a vector store collection. The collection embeds each chunk.
/// </summary>
public static class DocumentIngestor
{
    public static async Task<int> IngestAsync(VectorStoreCollection<string, DocumentChunk> collection, string folder)
    {
        await collection.EnsureCollectionExistsAsync();

        var chunks = Directory.GetFiles(folder, "*.md").SelectMany(ChunkFile).ToList();
        await collection.UpsertAsync(chunks); // one embedding call per chunk happens here

        return chunks.Count;
    }

    /// <summary>
    /// Splits a file by "## " headings. Small, focused chunks match questions better
    /// and cost fewer tokens than whole files.
    /// </summary>
    private static IEnumerable<DocumentChunk> ChunkFile(string path)
    {
        var source = Path.GetFileName(path);
        var sections = File.ReadAllText(path).Split("\n## ", StringSplitOptions.RemoveEmptyEntries);

        // sections[0] is the "# Title" part before the first "## " heading.
        for (var i = 1; i < sections.Length; i++)
        {
            var lines = sections[i].Split('\n', 2);
            yield return new DocumentChunk
            {
                Id = $"{source}#{i}",
                Source = source,
                Heading = lines[0].Trim(),
                Text = lines.Length > 1 ? lines[1].Trim() : "",
            };
        }
    }
}
