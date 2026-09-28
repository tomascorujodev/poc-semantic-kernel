using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using PocSemanticKernel.Plugins;
using PocSemanticKernel.Rag;

namespace PocSemanticKernel.Agents;

/// <summary>
/// The store's specialist agents. Each agent = name + description + instructions
/// + its own kernel with ONLY the plugins it needs.
/// </summary>
public static class StoreAgents
{
    public static ChatCompletionAgent CreateTriage(Kernel kernel) => new()
    {
        Name = "TriageAgent",
        // The description is what OTHER agents see when deciding to hand off to this one.
        Description = "Greets the customer and routes the request to the right specialist.",
        Instructions = """
            You are the first contact of an electronics store's customer support.
            You do not answer questions yourself: transfer the customer to the right specialist.
            - Orders (look up, status, cancel) -> OrdersAgent
            - Returns, refunds, warranty, shipping, order status meanings -> PolicyAgent
            If the request is unclear, ask one short question to clarify it.
            If the customer says goodbye or has nothing else to ask, end the task.
            """,
        Kernel = kernel.Clone(), // no plugins: triage only routes
        Arguments = AutoFunctionCalling(),
    };

    public static ChatCompletionAgent CreateOrders(Kernel kernel, OrdersPlugin ordersPlugin)
    {
        var agentKernel = kernel.Clone();
        agentKernel.Plugins.AddFromType<TimePlugin>("Time");
        agentKernel.Plugins.AddFromObject(ordersPlugin, "Orders");

        return new()
        {
            Name = "OrdersAgent",
            Description = "Looks up customer orders and cancels orders.",
            Instructions = """
                You handle questions about specific orders of an electronics store.
                Use the Orders tools. Never invent order data; if a tool returns nothing, say so.
                Ask for confirmation before cancelling an order.
                You do NOT know the store's policies (returns, warranty, shipping) and must never
                ask the customer about them. For any policy question, including one about a
                specific order (e.g. "is order 40 still under warranty?"), transfer to PolicyAgent:
                it can look up the order itself.
                For anything else, transfer back to TriageAgent.
                """,
            Kernel = agentKernel,
            Arguments = AutoFunctionCalling(),
        };
    }

    public static ChatCompletionAgent CreatePolicy(
        Kernel kernel, VectorStoreCollection<string, DocumentChunk> collection, OrdersPlugin ordersPlugin)
    {
        var agentKernel = kernel.Clone();
        agentKernel.Plugins.AddFromType<TimePlugin>("Time");
        agentKernel.Plugins.AddFromObject(new DocsPlugin(collection), "Docs");

        // Read-only access to orders: agents in a handoff share MESSAGES, not each other's
        // tool results, so this agent looks up order facts (product, date) itself.
        // Cancelling stays with OrdersAgent only.
        var orders = KernelPluginFactory.CreateFromObject(ordersPlugin, "Orders");
        agentKernel.Plugins.AddFromFunctions("Orders", [orders["GetOrder"]]);

        return new()
        {
            Name = "PolicyAgent",
            Description = "Answers questions about returns, refunds, warranty, shipping and order statuses, using the store's documents.",
            Instructions = """
                You answer questions about the store's policies.
                ALWAYS call Docs-SearchPolicies first and answer ONLY from its results.
                Cite the source file in brackets after each fact, e.g. [returns-policy.md].
                If the documents do not contain the answer, say you don't know. Never invent policies.
                If the question is about a specific order, call Orders-GetOrder to get its product
                and date, and Time-GetCurrentDateTime for today's date, then apply the policy.
                To cancel an order or list a customer's orders, transfer to OrdersAgent.
                For anything else, transfer back to TriageAgent.
                """,
            Kernel = agentKernel,
            Arguments = AutoFunctionCalling(),
        };
    }

    // Same settings as the Step 2/3 chat: the model may call the agent's plugins
    // (and the handoff functions the orchestration adds) by itself.
    private static KernelArguments AutoFunctionCalling() => new(new AzureOpenAIPromptExecutionSettings
    {
        FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
        Temperature = 0.2,
    });
}
