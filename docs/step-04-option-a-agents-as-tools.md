# Step 4, Option A: Agents as Tools (not implemented)

> **Status:** 📘 Guide only. The PoC implements **Option B (handoff orchestration)**. This page explains how Option A would work, so you can compare the two or build it later.
>
> The APIs named here were checked against the installed packages (`Microsoft.SemanticKernel.Agents.Core` 1.80.1), but this code has **not** been compiled or run.

---

## 1. The idea

One **coordinator agent** talks to the customer. It has no business tools of its own. Instead, its "tools" are **other agents**: when it needs order data, it calls the Orders agent like a function; when it needs a policy, it calls the Policy agent.

```
            ┌─────────────────────────────────────┐
 User ◄────►│ CoordinatorAgent                    │   always the one who answers the user
            │ plugins:                            │
            │   Specialists-OrdersAgent(query)  ──┼──► OrdersAgent  (plugins: Orders, Time)
            │   Specialists-PolicyAgent(query)  ──┼──► PolicyAgent  (plugins: Docs / RAG)
            └─────────────────────────────────────┘
```

It is like a **manager who delegates**: the manager asks a specialist, gets the answer back, and replies to the customer. In Option B (handoff), the call is **transferred** and the specialist talks to the customer directly.

---

## 2. Option A vs Option B

| | **A: Agents as tools** | **B: Handoff** *(implemented)* |
|---|---|---|
| Who answers the user | Always the coordinator | The specialist the user was transferred to |
| How agents are connected | Specialists wrapped as `KernelFunction`s | Handoff rules → generated `transfer_to_X` functions |
| Packages | `Agents.Core` (stable) | + `Agents.Orchestration`, `Agents.Runtime.InProcess` (preview) |
| Chat loop | Normal loop, like Steps 2–3 (`AgentThread` keeps the history) | Owned by the orchestration (`InteractiveCallback`) |
| Streaming | `InvokeStreamingAsync` | `StreamingResponseCallback` |
| Can combine specialists in one answer | ✅ Easily: the coordinator can call both and merge the results | Needs a handoff back and forth |
| Cost | More tokens: the coordinator re-reads every specialist answer | Fewer: the specialist answers directly |
| Best for | One consistent "voice", answers that need several specialists | Call-centre style routing, specialists with long conversations |

---

## 3. Key concepts

| Concept | What it is |
|---|---|
| `ChatCompletionAgent` | Name + description + instructions + kernel (with plugins). Same class as in Option B. |
| `AgentKernelFunctionFactory.CreateFromAgent(agent)` | Wraps an agent as a `KernelFunction`. The model sees it as a tool; calling it runs the agent. |
| `ChatHistoryAgentThread` | The conversation memory of an agent. Replaces the `ChatHistory` we managed by hand in `ChatLoop.cs`. |
| `agent.InvokeStreamingAsync(message, thread)` | Runs the agent on a new message and streams the answer. |

---

## 4. Files it would need

```
src/PocSemanticKernel/
├─ PocSemanticKernel.csproj    + Microsoft.SemanticKernel.Agents.Core 1.80.1   (already added for Option B)
├─ Agents/StoreAgents.cs       reuse CreateOrders and CreatePolicy (already exist)
│                              + CreateCoordinator(kernel, orders, policy)      (new method)
├─ Demos/AgentsAsToolsDemo.cs  new: chat loop with the coordinator
└─ Program.cs                  + menu option, e.g. "5) Multi-agent support (agents as tools)"
```
`Rag/PolicyStore.cs`, the plugins and the `LoggingFilter` are reused unchanged.

---

## 5. How to build it

### 5.1 The coordinator (`Agents/StoreAgents.cs`)

```csharp
#pragma warning disable SKEXP0110 // agents-as-functions is experimental
public static ChatCompletionAgent CreateCoordinator(Kernel kernel, ChatCompletionAgent orders, ChatCompletionAgent policy)
{
    var agentKernel = kernel.Clone();

    // Each specialist becomes a function the coordinator can call.
    // The agent's Description becomes the function description the model reads.
    agentKernel.Plugins.AddFromFunctions("Specialists",
    [
        AgentKernelFunctionFactory.CreateFromAgent(orders),
        AgentKernelFunctionFactory.CreateFromAgent(policy),
    ]);

    return new()
    {
        Name = "CoordinatorAgent",
        Instructions = """
            You are the customer-support assistant of an electronics store.
            You don't have the data yourself: ask your specialists.
            - Questions about specific orders -> OrdersAgent
            - Returns, refunds, warranty, shipping -> PolicyAgent
            You may ask both and combine their answers.
            Keep the citations the specialists give, e.g. [warranty.md].
            Ask the customer for confirmation before asking OrdersAgent to cancel an order.
            """,
        Kernel = agentKernel,
        Arguments = AutoFunctionCalling(), // same helper as the other agents
    };
}
#pragma warning restore SKEXP0110
```

Notes:
- The specialists' **instructions change slightly**: remove the "transfer to …" lines, because in Option A they never talk to the user and never hand off. Something like *"Answer the coordinator's question using your tools"* is enough.
- `CreateFromAgent` also accepts an optional function name and description, if you want different names than the agents'.

### 5.2 The chat loop (`Demos/AgentsAsToolsDemo.cs`)

```csharp
public static async Task RunAsync(Kernel kernel)
{
    kernel.FunctionInvocationFilters.Add(new LoggingFilter()); // before cloning, so all agents log
    var collection = await PolicyStore.CreateAsync(kernel);

    var orders = StoreAgents.CreateOrders(kernel);
    var policy = StoreAgents.CreatePolicy(kernel, collection);
    var coordinator = StoreAgents.CreateCoordinator(kernel, orders, policy);

    // The thread keeps the conversation history between turns (like ChatHistory in Step 2).
    AgentThread thread = new ChatHistoryAgentThread();

    while (true)
    {
        Console.Write("You > ");
        var input = Console.ReadLine();
        if (input is null || input.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase)) break;

        Console.Write("AI  > ");
        await foreach (var chunk in coordinator.InvokeStreamingAsync(
                           new ChatMessageContent(AuthorRole.User, input), thread))
        {
            Console.Write(chunk.Message.Content);
            thread = chunk.Thread; // the agent may return an updated thread
        }
        Console.WriteLine("\n");
    }
}
```

### 5.3 What you would see

With the `LoggingFilter`, a question that needs both specialists would show nested tool calls:

```
You > Is order 40 still under warranty?
  🔧 Specialists.OrdersAgent(query=Get details of order 40)
  🔧 Orders.GetOrder(orderId=40)                 ← inside the Orders agent
  ↳ {"Id":40,"Product":"Laptop Pro 14","OrderDate":"2026-09-02",...}
  🔧 Specialists.PolicyAgent(query=Warranty period for Pro laptops)
  🔧 Docs.SearchPolicies(query=warranty Pro laptop)   ← inside the Policy agent
  ↳ [{"Source":"warranty.md","Heading":"Coverage period",...}]
AI  > Yes. Order 40 (Laptop Pro 14) was delivered on 2026-09-02, and Pro laptops have a
      3-year warranty [warranty.md], so it's covered until 2029.
```
*(Illustrative output, not from a real run.)*

---

## 6. Things to watch out for

- **Token cost:** each specialist call is a full model call, and the coordinator then reads the answer. A question that uses two specialists costs about 3 model calls, plus the tool calls inside each specialist.
- **Lost details:** the coordinator may shorten or rephrase a specialist's answer and drop citations. That's why its instructions say "keep the citations".
- **Confirmation for dangerous actions:** only the coordinator talks to the user, so *it* must ask for confirmation before delegating a cancellation. A filter that blocks `Orders.CancelOrder` until the user approves is safer than relying on instructions.
- **Specialists have no memory of the conversation.** Each call sends only the coordinator's `query`, so the coordinator must include all the context the specialist needs (e.g. the order id).
