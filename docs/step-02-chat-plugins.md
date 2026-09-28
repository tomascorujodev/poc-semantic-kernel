# Step 2 of poc-semantic-kernel: Chat + Plugins

> **Goal of this step:** turn the single prompt from Step 1 into a real **chat** that remembers the conversation, and let the model **call our C# code** (plugins) to get data and take actions.
>
> **Result:** ✅ Working. `dotnet run -- 2` answers "What orders does John Smith have?" by calling `Orders.GetOrdersByCustomer`, and asks for confirmation before calling `Orders.CancelOrder`.

---

## Contents
1. [What changes in this step?](#1-what-changes-in-this-step)
2. [Key concepts](#2-key-concepts)
3. [How function calling works](#3-how-function-calling-works)
4. [Architecture of this step](#4-architecture-of-this-step)
5. [The code explained](#5-the-code-explained)
6. [Running the app](#6-running-the-app)
7. [Troubleshooting](#7-troubleshooting)
8. [Lessons learned](#8-lessons-learned)
9. [Glossary](#9-glossary)
10. [Next steps](#10-next-steps)

---

## 1. What changes in this step?

In Step 1 the model could only **generate text** from what it learned in training. It could not:
- remember what you said before,
- know today's date,
- look anything up in *your* systems,
- *do* anything (cancel an order, send an email…).

Step 2 fixes that with two ideas:
- **Chat history**: we keep the conversation and send it every turn.
- **Plugins + function calling**: we describe C# methods to the model; the model can ask to run them, and Semantic Kernel runs them for it.

This is the core idea behind every AI "assistant" or "copilot" that works with real data.

No new packages and no Azure changes are needed: everything is in `Microsoft.SemanticKernel`.

---

## 2. Key concepts

| Concept | What it is | Where it is in the code |
|---|---|---|
| **Chat completion service** | The SK service for multi-turn chat (`IChatCompletionService`) | `kernel.GetRequiredService<IChatCompletionService>()` in `Demos/ChatLoop.cs` |
| **ChatHistory** | The list of messages (system, user, assistant, tool) sent to the model every turn | `new ChatHistory(systemPrompt)` in `Demos/ChatLoop.cs` |
| **System prompt** | The first message: the model's role and rules | `ChatWithPluginsDemo.cs` |
| **Native plugin** | A C# class whose methods are exposed to the model as tools | `Plugins/OrdersPlugin.cs`, `Plugins/TimePlugin.cs` |
| **`[KernelFunction]`** | Marks a method as callable by the model | on each plugin method |
| **`[Description]`** | The text the model reads to decide *when* and *how* to call a function | on methods and parameters |
| **Function calling** | The model answers with "call function X with arguments Y" instead of text | handled by SK |
| **`FunctionChoiceBehavior.Auto()`** | Sends the plugin descriptions to the model and lets SK **run** the calls automatically | `AzureOpenAIPromptExecutionSettings` in `Demos/ChatLoop.cs` |
| **Function invocation filter** | Code that runs **around** every function call (like middleware) | `Filters/LoggingFilter.cs` |
| **Streaming** | Receive the answer token by token instead of waiting for all of it | `GetStreamingChatMessageContentsAsync` in `Demos/ChatLoop.cs` |

### Message roles in a ChatHistory

| Role | Who writes it | Example |
|---|---|---|
| `system` | Developer | "You are a customer-support assistant… Ask for confirmation before cancelling." |
| `user` | The person chatting | "Cancel order 43" |
| `assistant` | The model | "Are you sure you want to cancel order 43?" / or a **tool call request** |
| `tool` | SK, with the function's result | `"Order 43 was cancelled."` |

---

## 3. How function calling works

**The model never runs code.** It can only *ask* for a function to be run. Your application decides whether and how to run it.

```
 Your app (SK)                                      Azure OpenAI (gpt-4.1-mini)
 ─────────────                                      ───────────────────────────
 1. history + tool schemas  ───────────────────────►
    [Orders-GetOrdersByCustomer(customerName), ...]
                                                    2. "I need data" → returns a TOOL CALL:
                            ◄─────────────────────     Orders-GetOrdersByCustomer("John Smith")
 3. SK finds the C# method, runs it
    (filters run around it)
 4. history + tool result   ───────────────────────►
    [{"Id":42,...},{"Id":43,...}]
                                                    5. writes the final answer in text
                            ◄─────────────────────     "John Smith has two orders: ..."
```

- Steps 2–4 can repeat several times in one turn (the model may call several functions, in sequence or in parallel).
- With `Auto()`, **all of this happens inside one call** to `GetStreamingChatMessageContentsAsync`. Your code just sees the final answer (and the filter sees each call).
- **What the model sees** of a plugin is only a JSON schema: function name, description, parameter names, types and descriptions. That's why good `[Description]`s matter so much. They are part of the prompt.

### `FunctionChoiceBehavior` options

| Option | Meaning |
|---|---|
| `Auto()` | The model decides whether to call functions or answer directly. SK runs the calls. *(used here)* |
| `Required()` | The model **must** call at least one function. |
| `None()` | Functions are described, but the model may not call them (useful to "plan" without acting). |
| `Auto(autoInvoke: false)` | The model can request calls, but **your code** runs them manually. Useful for full control or approval UIs. |

---

## 4. Architecture of this step

```
┌──────────────────────────────────────────────────────────────┐
│ ChatLoop (console)                                           │
│   ChatHistory: system + user + assistant + tool messages     │
└───────────────┬──────────────────────────────────────────────┘
                │ GetStreamingChatMessageContentsAsync(history, settings{Auto()}, kernel)
                ▼
┌──────────────────────────────────────────────────────────────┐
│ Kernel                                                       │
│  ├─ Chat completion (Azure OpenAI, gpt-4.1-mini)             │
│  ├─ Plugins                                                  │
│  │   ├─ Time   : GetCurrentDateTime()                        │
│  │   └─ Orders : GetOrdersByCustomer, GetOrder, CancelOrder  │
│  └─ Filters                                                  │
│      └─ LoggingFilter  (prints 🔧 call + ↳ result)           │
└──────────────────────────────────────────────────────────────┘
```

### Files

```
src/PocSemanticKernel/
├─ Demos/
│  ├─ ChatLoop.cs             ← shared chat loop (history + Auto() + streaming)
│  └─ ChatWithPluginsDemo.cs  ← registers plugins + filter, sets the system prompt
├─ Filters/LoggingFilter.cs   ← IFunctionInvocationFilter
├─ Plugins/
│  ├─ OrdersPlugin.cs         ← fake orders "database"
│  └─ TimePlugin.cs           ← current date and time
└─ Program.cs                 ← menu option 2
```

> ℹ️ The chat loop was originally inside `ChatWithPluginsDemo.cs`. In Step 3 it was moved to `ChatLoop.cs` so Step 2 and Step 3 could share it.

---

## 5. The code explained

### 5.1 A native plugin (`Plugins/TimePlugin.cs`)

```csharp
public sealed class TimePlugin
{
    [KernelFunction, Description("Gets the current local date and time, including the time zone.")]
    public string GetCurrentDateTime() =>
        $"{DateTimeOffset.Now:dddd, yyyy-MM-dd HH:mm:ss zzz} ({TimeZoneInfo.Local.DisplayName})";
}
```
- A normal class. `[KernelFunction]` makes the method visible to the model.
- LLMs **don't know today's date**. Their knowledge stops at training time. A tiny plugin like this fixes a whole class of wrong answers ("is it still under warranty?").

### 5.2 A plugin with data and actions (`Plugins/OrdersPlugin.cs`)

```csharp
[KernelFunction, Description("Gets all orders placed by a customer.")]
public IReadOnlyList<Order> GetOrdersByCustomer(
    [Description("Customer full name, e.g. 'Ana García'")] string customerName) => ...

[KernelFunction, Description("Gets one order by its numeric id. Returns null if it does not exist.")]
public Order? GetOrder([Description("The order id, e.g. 42")] int orderId) => ...

[KernelFunction, Description("Cancels an order. Only orders with status 'Pending' or 'Processing' can be cancelled.")]
public string CancelOrder([Description("The order id to cancel")] int orderId) { ... }
```
- The data is an in-memory list (5 fake orders). In a real app these methods would call a database or an API.
- **Return types can be objects** (`Order`, lists). SK serializes them to JSON for the model.
- **Business rules are enforced in code**, not only in the prompt: `CancelOrder` refuses to cancel a "Delivered" order even if the model asks. *Never trust the model to enforce rules.*
- Parameter types matter: `int orderId` means SK converts and validates the model's argument.

### 5.3 Registering plugins and the filter (`Demos/ChatWithPluginsDemo.cs`)

```csharp
kernel.Plugins.AddFromType<TimePlugin>("Time");            // SK creates the instance
kernel.Plugins.AddFromObject(new OrdersPlugin(), "Orders"); // we pass an instance (keeps its state)
kernel.FunctionInvocationFilters.Add(new LoggingFilter());
```
- The model sees the functions as `Time-GetCurrentDateTime`, `Orders-GetOrder`, etc. (*plugin* + `-` + *function*).
- `AddFromObject` is used for `OrdersPlugin` because it holds state (the order list): cancelling changes it.

### 5.4 The system prompt

```
You are a helpful customer-support assistant for an electronics store.
Use the available tools to answer questions about orders and dates.
Never invent order data. If a tool returns nothing, say so.
Ask for confirmation before cancelling an order.
```
Each line has a purpose: a role, when to use tools, anti-hallucination, and a safety rule for a destructive action.

### 5.5 The chat loop (`Demos/ChatLoop.cs`)

```csharp
var settings = new AzureOpenAIPromptExecutionSettings
{
    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
    Temperature = 0.2,
};
var history = new ChatHistory(systemPrompt);

while (true)
{
    history.AddUserMessage(input);
    await foreach (var chunk in chat.GetStreamingChatMessageContentsAsync(history, settings, kernel))
        Console.Write(chunk.Content);
    history.AddAssistantMessage(answer);
}
```
- **The model is stateless.** Memory is just the `history` we resend each turn. That also means **cost grows** with the conversation length: every turn re-sends all previous messages.
- `Temperature = 0.2`: low randomness, good for support answers.
- Passing `kernel` is what gives the model access to the plugins.

### 5.6 The filter (`Filters/LoggingFilter.cs`)

```csharp
public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
{
    // before: print "🔧 Plugin.Function(args)"
    await next(context);          // runs the actual C# method (or the next filter)
    // after: print "↳ result"
}
```
- Works like **ASP.NET middleware**: code before `next` runs before the function, code after runs after it.
- If you **don't call `next`**, the function doesn't run. You can set `context.Result` yourself instead. That's how you would build an **approval step** ("Allow CancelOrder? y/n") or block dangerous calls.
- Other filter types in SK: `IPromptRenderFilter` (see/modify the rendered prompt) and `IAutoFunctionInvocationFilter` (control the automatic calling loop, e.g. stop after N calls).

---

## 6. Running the app

```powershell
cd C:\Proyectos-2\poc-semantic-kernel\src\PocSemanticKernel
dotnet run -- 2
```

**Actual output:**
```
You > What orders does John Smith have?
AI  >   🔧 Orders.GetOrdersByCustomer(customerName=John Smith)
  ↳ [{"Id":42,"Customer":"John Smith","Product":"4K Monitor 27\u0022",...,"Status":"Processing"...},{"Id":43,...}]
John Smith has two orders:
1. A 4K Monitor 27" with order ID 42, currently in Processing status.
2. A Mechanical Keyboard with order ID 43, currently in Pending status.

You > Cancel order 43
AI  > Are you sure you want to cancel order 43 for the Mechanical Keyboard? Please confirm.

You > Yes, cancel it
AI  >   🔧 Orders.CancelOrder(orderId=43)
  ↳ Order 43 was cancelled.
Order 43 for the Mechanical Keyboard has been cancelled.
```

**What this shows:**
- The model **chose** the right function and **extracted** the argument (`John Smith`) from natural language.
- On "Cancel order 43" it did **not** call `CancelOrder`: it followed the system prompt and asked first.
- On "Yes, cancel it" it understood the context from the **history** (which order) and called `CancelOrder(43)`.

---

## 7. Troubleshooting

No problems were hit in this step. Things to watch for:

| Symptom | Likely cause |
|---|---|
| The model answers without calling a function | `FunctionChoiceBehavior` not set, `kernel` not passed to the chat call, or the `[Description]` doesn't match the question |
| The model calls the wrong function | Descriptions are vague or overlap. Make each one specific ("Gets ONE order by id" vs "Gets ALL orders of a customer") |
| The model invents data | Missing "never invent" rule, or the function returned nothing and the model filled the gap |
| `🔧` lines not printed | The filter wasn't added, or it was added to a different kernel instance |
| Wrong argument types | Use typed parameters (`int`, `DateOnly`, enums) so SK validates them |

---

## 8. Lessons learned
- **The model decides, your code executes.** Function calling is a request, not execution. This is the key to keeping control.
- **Descriptions are prompts.** The model only sees names, descriptions and parameter types. Write them for the model.
- **Enforce rules in code, not only in the prompt.** "Ask for confirmation" in the prompt is a *soft* rule the model could ignore; the status check in `CancelOrder` is a *hard* rule. For real destructive actions, add an approval filter.
- **The model is stateless.** Chat memory = the history you resend, and it costs tokens every turn.
- **Filters are the control point** for logging, security, approvals and telemetry.

---

## 9. Glossary

| Term | Definition |
|---|---|
| **Plugin** | A group of functions exposed to the model (native C#, prompts, or OpenAPI) |
| **Native function** | A C# method marked with `[KernelFunction]` |
| **Function calling / tool calling** | The model's ability to request that a function be run with specific arguments |
| **Tool schema** | The JSON description of a function sent to the model |
| **Auto function invocation** | SK automatically runs the requested functions and sends the results back to the model |
| **System prompt** | The instructions that define the assistant's role and rules |
| **Filter** | Middleware around function calls, prompt rendering, or the auto-invocation loop |
| **Streaming** | Receiving the model's output in small pieces as it's generated |
| **Stateless** | The model keeps nothing between requests; the app must resend the context |

---

## 10. Next steps

| Step | Topic | Main concepts |
|---|---|---|
| **3** | RAG over documents | Embeddings, vector store, search as a plugin, grounding, citations → [`step-03-rag.md`](step-03-rag.md) |
| **4** | Agents | `ChatCompletionAgent`, handoff orchestration → [`step-04-agents.md`](step-04-agents.md) |
