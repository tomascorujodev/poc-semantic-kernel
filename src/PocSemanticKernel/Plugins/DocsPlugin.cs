using System.ComponentModel;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel;
using PocSemanticKernel.Rag;

namespace PocSemanticKernel.Plugins;

public sealed record DocSearchResult(string Source, string Heading, string Text, double? Score);

/// <summary>
/// Retrieval exposed as a normal plugin: the model decides when to search,
/// just like it decides when to call the Orders plugin.
/// </summary>
public sealed class DocsPlugin(VectorStoreCollection<string, DocumentChunk> collection)
{
    [KernelFunction, Description("Searches the store's policy documents: returns, refunds, warranty, shipping and order statuses.")]
    public async Task<IReadOnlyList<DocSearchResult>> SearchPolicies(
        [Description("What to look for, written as a short question or keywords, e.g. 'return opened laptop fee'")] string query)
    {
        var results = new List<DocSearchResult>();

        // SearchAsync embeds the query and returns the chunks with the closest vectors.
        await foreach (var r in collection.SearchAsync(query, top: 3))
            results.Add(new(r.Record.Source, r.Record.Heading, r.Record.Text, r.Score));

        return results;
    }
}
