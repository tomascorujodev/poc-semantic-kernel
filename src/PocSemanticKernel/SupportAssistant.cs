using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using PocSemanticKernel.Plugins;
using PocSemanticKernel.Rag;

namespace PocSemanticKernel;

/// <summary>
/// The single-assistant setup from Step 3 (plugins + RAG), shared by the console demo
/// and the web UI (Step 5) so both behave exactly the same.
/// </summary>
public static class SupportAssistant
{
    public const string SystemPrompt = """
        You are a helpful customer-support assistant for an electronics store.
        Use the Orders tools for questions about specific orders.
        For any question about returns, refunds, warranty, shipping or order statuses,
        ALWAYS call Docs-SearchPolicies first and answer ONLY from its results.
        Cite the source file in brackets after each fact, e.g. [returns-policy.md].
        If the documents do not contain the answer, say you don't know. Never invent policies.
        Ask for confirmation before cancelling an order.
        """;

    public static void AddPlugins(Kernel kernel, VectorStoreCollection<string, DocumentChunk> collection)
    {
        kernel.Plugins.AddFromType<TimePlugin>("Time");
        kernel.Plugins.AddFromObject(new OrdersPlugin(), "Orders");
        kernel.Plugins.AddFromObject(new DocsPlugin(collection), "Docs");
    }

    // Auto(): the model may call the plugins; SK runs them and sends the results back.
    public static AzureOpenAIPromptExecutionSettings CreateSettings() => new()
    {
        FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
        Temperature = 0.2,
    };
}
