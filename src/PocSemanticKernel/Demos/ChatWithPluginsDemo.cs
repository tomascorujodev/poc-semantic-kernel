using Microsoft.SemanticKernel;
using PocSemanticKernel.Filters;
using PocSemanticKernel.Plugins;

namespace PocSemanticKernel.Demos;

/// <summary>
/// Step 2: a chat loop where the model can call C# plugins by itself (automatic function calling).
/// </summary>
public static class ChatWithPluginsDemo
{
    public static async Task RunAsync(Kernel kernel)
    {
        // 1. Plugins: register C# classes as tools the model can use.
        kernel.Plugins.AddFromType<TimePlugin>("Time");
        kernel.Plugins.AddFromObject(new OrdersPlugin(), "Orders");

        // 2. Filter: log every plugin call.
        kernel.FunctionInvocationFilters.Add(new LoggingFilter());

        // 3. Chat loop: ChatHistory + automatic function calling + streaming (see ChatLoop).
        await ChatLoop.RunAsync(kernel,
            systemPrompt: """
                You are a helpful customer-support assistant for an electronics store.
                Use the available tools to answer questions about orders and dates.
                Never invent order data. If a tool returns nothing, say so.
                Ask for confirmation before cancelling an order.
                """,
            intro: "Chat with plugins. Try: \"What orders does Ana García have?\" or \"Cancel order 43\".");
    }
}
