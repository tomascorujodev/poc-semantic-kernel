using Microsoft.SemanticKernel;
using PocSemanticKernel.Filters;
using PocSemanticKernel.Rag;

namespace PocSemanticKernel.Demos;

/// <summary>
/// Step 3: RAG. Store documents are chunked, embedded and put in a vector store.
/// The model searches them through a plugin and answers only from what it finds.
/// </summary>
public static class RagDemo
{
    public static async Task RunAsync(Kernel kernel)
    {
        // 1. Vector store collection + ingestion (see PolicyStore).
        var collection = await PolicyStore.CreateAsync(kernel);

        // 2. Plugins: retrieval is just another tool, next to the Step 2 plugins.
        SupportAssistant.AddPlugins(kernel, collection);
        kernel.FunctionInvocationFilters.Add(new LoggingFilter());

        // 3. Grounding rules (answer only from the documents, cite the source) are in the system prompt.
        await ChatLoop.RunAsync(kernel, SupportAssistant.SystemPrompt,
            intro: "Chat with documents. Try: \"Can I return an opened laptop?\" or \"Is order 40 still under warranty?\".");
    }
}
