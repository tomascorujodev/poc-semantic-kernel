using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents.Orchestration.Handoff;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Agents.Runtime.InProcess;
using PocSemanticKernel.Agents;
using PocSemanticKernel.Filters;
using PocSemanticKernel.Plugins;
using PocSemanticKernel.Rag;

namespace PocSemanticKernel.Demos;

/// <summary>
/// Step 4: multi-agent support with a handoff orchestration. A triage agent transfers
/// the customer to a specialist (orders or policies), like a call centre transferring a call.
/// </summary>
public static class HandoffDemo
{
    public static async Task RunAsync(Kernel kernel)
    {
#pragma warning disable SKEXP0110 // The agent orchestration API is experimental.

        // 1. Shared setup: the filter is added BEFORE the agents clone the kernel, so every agent has it.
        kernel.FunctionInvocationFilters.Add(new LoggingFilter());
        var collection = await PolicyStore.CreateAsync(kernel);

        // 2. The agents: each one has its own instructions and only its own plugins.
        var triage = StoreAgents.CreateTriage(kernel);
        var ordersPlugin = new OrdersPlugin(); // one instance, so both agents see the same data
        var orders = StoreAgents.CreateOrders(kernel, ordersPlugin);
        var policy = StoreAgents.CreatePolicy(kernel, collection, ordersPlugin);

        // 3. Handoff rules: who may transfer to whom. SK turns each rule into a function
        //    (e.g. transfer_to_PolicyAgent) that the model can call, like any other tool.
        var handoffs = OrchestrationHandoffs
            .StartWith(triage)
            .Add(triage, orders, policy)
            .Add(orders, policy, "Transfer to this agent if the customer asks about returns, warranty or shipping policies")
            .Add(orders, triage, "Transfer to this agent if the request is not about orders or policies")
            .Add(policy, orders, "Transfer to this agent if the customer wants to cancel an order or list their orders")
            .Add(policy, triage, "Transfer to this agent if the request is not about orders or policies");

        Console.WriteLine("Multi-agent support (handoff). Try: \"Can I return an opened laptop?\" then \"What orders does Ana García have?\". Type 'exit' to quit.\n");

        while (true)
        {
            Console.Write("You > ");
            var input = Console.ReadLine();
            if (input is null || input.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase)) break;
            if (string.IsNullOrWhiteSpace(input)) continue;

            var userQuit = false;
            var orchestration = new HandoffOrchestration(handoffs, triage, orders, policy)
            {
                // Called with each agent's message. AuthorName tells us which agent is speaking.
                ResponseCallback = response =>
                {
                    if (!string.IsNullOrWhiteSpace(response.Content))
                        Console.WriteLine($"[{response.AuthorName}] {response.Content}\n");
                    return ValueTask.CompletedTask;
                },
                // Called when an agent needs input from the customer. This is the chat loop.
                InteractiveCallback = () =>
                {
                    Console.Write("You > ");
                    var reply = Console.ReadLine();
                    if (reply is null || reply.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase))
                    {
                        userQuit = true;
                        reply = "I have no more questions. Goodbye.";
                    }
                    return ValueTask.FromResult(new ChatMessageContent(AuthorRole.User, reply));
                },
            };

            // 4. The runtime passes messages between the agents (in this process).
            await using var runtime = new InProcessRuntime();
            await runtime.StartAsync();

            // 5. Run until an agent ends the task (it calls a built-in "complete task" function).
            try
            {
                var result = await orchestration.InvokeAsync(input, runtime);
                var summary = await result.GetValueAsync();
                Console.WriteLine($"✔ Task completed: {summary}\n");
                await runtime.RunUntilIdleAsync();
            }
            catch (HttpOperationException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                // 429: the deployment's tokens-per-minute quota is used up. Multi-agent runs make
                // many model calls, so this happens sooner than in a single-assistant chat.
                Console.WriteLine("⚠ Azure OpenAI rate limit reached (HTTP 429). Wait a minute and try again.\n");
            }

            if (userQuit) break;
        }

#pragma warning restore SKEXP0110
    }
}
