# Step 5 of poc-semantic-kernel: Web UI

> **Goal of this step:** put the assistant in a **web page**: a chat that streams answers, shows the tool calls and displays citations.
>
> **Result:** ✅ Working. `dotnet run` in `src/PocSemanticKernel.Web` serves a chat page at http://localhost:5126. It uses the **Step 3 assistant** (plugins + RAG) through an ASP.NET Core API that streams with **Server-Sent Events**.

---

## Contents
1. [What we built and why](#1-what-we-built-and-why)
2. [Key concepts](#2-key-concepts)
3. [Architecture of this step](#3-architecture-of-this-step)
4. [The code explained](#4-the-code-explained)
5. [Running the app](#5-running-the-app)
6. [How it was tested](#6-how-it-was-tested)
7. [Troubleshooting](#7-troubleshooting)
8. [Limitations and how to take it to production](#8-limitations-and-how-to-take-it-to-production)
9. [Lessons learned](#9-lessons-learned)
10. [Glossary](#10-glossary)

---

## 1. What we built and why

| Decision | Choice | Why |
|---|---|---|
| Which assistant | **Step 3** single assistant (Orders + Time + Docs/RAG) | Streams naturally. The handoff agents (Step 4) move the conversation between agents inside an orchestration, which is much harder to put behind a simple request/response API |
| Backend | **ASP.NET Core minimal API** | Few lines, same language as the rest of the PoC |
| Streaming | **Server-Sent Events (SSE)** over a `POST` | Data only flows server → browser during an answer; SSE is simpler than WebSockets or SignalR |
| Frontend | **One static HTML file**, plain JS, no framework | Nothing to install or build; easy to read |
| Code sharing | Web project **references** the console project | Fastest way to reuse `KernelFactory`, plugins, RAG and the system prompt |

---

## 2. Key concepts

| Concept | What it is | Where it is in the code |
|---|---|---|
| **Minimal API** | ASP.NET Core endpoints defined with `app.MapPost(...)` and a lambda, no controllers | `PocSemanticKernel.Web/Program.cs` |
| **Server-Sent Events (SSE)** | One HTTP response that stays open and sends text events (`event:` + `data:` lines, blank line between events) | `SendEvent(...)` in `Program.cs` |
| **Flush** | Push the bytes to the browser now instead of buffering them | `response.Body.FlushAsync()` |
| **Session** | One `ChatHistory` per browser tab, keyed by a random id the page creates | `sessions` dictionary in `Program.cs`, `crypto.randomUUID()` in `index.html` |
| **Per-request kernel clone** | A copy of the shared kernel with an extra filter for *this* request | `kernel.Clone()` in `Program.cs` |
| **ToolEventFilter** | Like `LoggingFilter`, but sends each tool call to the browser as an SSE event | `ToolEventFilter.cs` |
| **SupportAssistant** | Shared system prompt + plugins + settings, used by both the console (option 3) and the web app | `src/PocSemanticKernel/SupportAssistant.cs` |

### Why SSE and not WebSockets?

| | SSE | WebSockets / SignalR |
|---|---|---|
| Direction | Server → browser | Both ways |
| Protocol | Plain HTTP | Upgrade to a different protocol |
| Fits "send a question, stream the answer" | ✅ Exactly | Works, but more machinery |
| Used by | OpenAI / Azure OpenAI APIs for streaming | Real-time apps (games, collaboration) |

> ℹ️ The browser's `EventSource` API only supports `GET`. We send the question with `POST` (the body can be long), so the page reads the SSE stream with `fetch` + a small parser (`readEvents`).

---

## 3. Architecture of this step

```
 Browser: wwwroot/index.html                     ASP.NET Core: PocSemanticKernel.Web/Program.cs
 ───────────────────────────                     ─────────────────────────────────────────────
                                                 At startup (once):
                                                   kernel = KernelFactory.Create(...)     ← same secrets
                                                   PolicyStore.CreateAsync(kernel)        ← RAG ingestion (13 chunks)
                                                   SupportAssistant.AddPlugins(...)       ← Time, Orders, Docs

 POST /api/chat {sessionId, message} ──────────► history = sessions[sessionId] (new ChatHistory if missing)
                                                 requestKernel = kernel.Clone() + ToolEventFilter
                                                 GetStreamingChatMessageContentsAsync(history, Auto(), requestKernel)
   🔧 Docs.SearchPolicies(query=…)   ◄────────── event: tool        (from ToolEventFilter, before the call)
   (click → result preview)          ◄────────── event: toolResult  (after the call)
   "Yes, you can return…" (live)     ◄────────── event: token       (×100 or so)
   📄 returns-policy.md badge        ◄────────── event: done        (or event: error)

 "New chat" button:
 POST /api/reset {sessionId} ──────────────────► sessions.TryRemove(sessionId)
```

### Files

```
src/
├─ PocSemanticKernel/                 ← console project (Steps 1–4), referenced by the web project
│  └─ SupportAssistant.cs             ← new: shared prompt + plugins + settings
└─ PocSemanticKernel.Web/             ← new ASP.NET Core project (dotnet new web)
   ├─ Program.cs                      ← startup + /api/chat + /api/reset
   ├─ ToolEventFilter.cs              ← tool calls → SSE events
   ├─ wwwroot/index.html              ← the chat page
   └─ Properties/launchSettings.json  ← http://localhost:5126
```

Commands used:
```powershell
dotnet new web -n PocSemanticKernel.Web -o src/PocSemanticKernel.Web -f net10.0
dotnet sln PocSemanticKernel.slnx add src/PocSemanticKernel.Web/PocSemanticKernel.Web.csproj
dotnet add src/PocSemanticKernel.Web reference src/PocSemanticKernel/PocSemanticKernel.csproj
```
No new NuGet packages: ASP.NET Core is part of the SDK, and SK comes through the project reference.

---

## 4. The code explained

### 4.1 Startup (`Program.cs`)

```csharp
var kernel = KernelFactory.Create(KernelFactory.LoadConfiguration());
var collection = await PolicyStore.CreateAsync(kernel);
SupportAssistant.AddPlugins(kernel, collection);
var chat = kernel.GetRequiredService<IChatCompletionService>();
var sessions = new ConcurrentDictionary<string, ChatHistory>();
```
- **Exactly the same setup as Step 3**, shared through `SupportAssistant`.
- **Configuration:** `KernelFactory.LoadConfiguration()` uses `AddUserSecrets<Program>()` inside the console project, so it reads the **console project's User Secrets**. The web app needs no extra setup.
- **Ingestion runs once at startup** (13 embedding calls), not per request.
- `ConcurrentDictionary` because several requests can arrive at the same time.

### 4.2 The chat endpoint

```csharp
app.MapPost("/api/chat", async (ChatRequest request, HttpResponse response, CancellationToken ct) =>
{
    var history = sessions.GetOrAdd(request.SessionId, _ => new ChatHistory(SupportAssistant.SystemPrompt));

    response.ContentType = "text/event-stream";
    async Task SendEvent(string type, object data)
    {
        await response.WriteAsync($"event: {type}\ndata: {JsonSerializer.Serialize(data)}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }

    var requestKernel = kernel.Clone();
    requestKernel.FunctionInvocationFilters.Add(new ToolEventFilter(SendEvent));

    history.AddUserMessage(request.Message);
    await foreach (var chunk in chat.GetStreamingChatMessageContentsAsync(
                       history, SupportAssistant.CreateSettings(), requestKernel, ct))
    {
        answer.Append(chunk.Content);
        await SendEvent("token", new { text = chunk.Content });
    }
    history.AddAssistantMessage(answer.ToString());
    await SendEvent("done", new { });
});
```
- **SSE format:** each event is `event: <type>` + `data: <json>` + a blank line.
- **`FlushAsync` after each event** is what makes the answer appear word by word. Without it, ASP.NET Core may buffer the output.
- **`ct` (CancellationToken)**: if the user closes the tab, the request is cancelled and SK stops calling Azure.
- **Why `kernel.Clone()` per request?** The filter needs to write to *this* request's response. If we added it to the shared kernel, every request would get every other request's tool events. The clone shares the services and plugins (cheap) but has its own filter list.
- **Error handling:** an `HttpOperationException` (e.g. **429** rate limit) is sent as an `error` event, and the unanswered user message is removed from the history so the conversation stays consistent.

### 4.3 The filter (`ToolEventFilter.cs`)

```csharp
public sealed class ToolEventFilter(Func<string, object, Task> sendEvent) : IFunctionInvocationFilter
{
    public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
    {
        await sendEvent("tool", new { name, args });          // before the call
        await next(context);                                   // the real C# method
        await sendEvent("toolResult", new { name, preview });  // after the call (max 300 chars)
    }
}
```
The **same filter concept as Step 2**, but the output goes to the browser instead of the console. This is a good example of why filters exist: the plugins didn't change at all.

### 4.4 The page (`wwwroot/index.html`)

| Part | What it does |
|---|---|
| `sessionId = crypto.randomUUID()` | One conversation per tab; "New chat" creates a new id and calls `/api/reset` |
| `readEvents(response)` | Reads the `fetch` body as a stream, splits it at blank lines, and yields `{type, data}` events |
| `render(text)` | **Escapes HTML first**, then turns `**bold**` into `<strong>` and `[file.md]` into a 📄 badge |
| `event: tool` / `toolResult` | Adds a small 🔧 line; click it to see the result preview |
| `event: token` | Appends text and re-renders the bubble (live streaming) |
| `event: error` | Shows a red ⚠ message |
| CSS variables + `prefers-color-scheme` | Light and dark mode; `max-width` layout that works on a phone |

> ⚠️ **Security:** the model's answer is **untrusted text**. Rendering it with `innerHTML` without escaping would allow HTML/JS injection (for example, if a document or a user message contained `<img onerror=…>`). `render()` escapes first and only then adds our own safe tags.

### 4.5 `SupportAssistant.cs` (console project)

The system prompt, plugin registration and execution settings that were inside `RagDemo.cs` and `ChatLoop.cs` moved into one class, so the console demo (option 3) and the web app are **guaranteed to behave the same**.

---

## 5. Running the app

```powershell
cd C:\Proyectos-2\poc-semantic-kernel\src\PocSemanticKernel.Web
dotnet run
```
Open **http://localhost:5126**. The console shows `Ingested 13 chunks` and `Now listening on: http://localhost:5126`.

Try the suggestion buttons:
- "Can I return an opened laptop?"
- "What orders does Ana García have?"
- "Is order 40 still under warranty?"
- "Do you ship to the USA?"

---

## 6. How it was tested

### API tests (curl, real Azure calls)

| # | Request | Result |
|---|---|---|
| 1 | `GET /` | ✅ `200 text/html` |
| 2 | "Can I return an opened laptop?" | ✅ `tool` Docs.SearchPolicies → tokens → "15% restocking fee … [returns-policy.md]" → `done` |
| 3 | Same session: "And an opened monitor?" | ✅ Answered from the conversation history: "also … 15% restocking fee … [returns-policy.md]" |
| 4 | New session: "Do you ship to the USA?" | ✅ "ships only to countries within the European Union … [shipping.md]" |
| 5 | "What orders does Ana García have?" | ✅ `tool` Orders.GetOrdersByCustomer(customerName=Ana García) → Laptop Pro 14 + USB-C Dock |
| 6 | Same session: "Which customer did I just ask about?" | ✅ "Ana García" (memory works) |
| 7 | `POST /api/reset`, then the same question | ✅ "You have not mentioned any customer's name yet" (history cleared) |

Real SSE output of test 2 (shortened):
```
event: tool
data: {"name":"Docs.SearchPolicies","args":{"query":"return opened laptop"}}

event: toolResult
data: {"name":"Docs.SearchPolicies","preview":"[{\"Source\":\"returns-policy.md\",\"Heading\":\"Condition of returned items\",...,\"Score\":0.4869...

event: token
data: {"text":"Yes"}

event: token
data: {"text":", you can return"}
...
event: done
data: {}
```

### Page JavaScript test (Node.js)

The page's own `escapeHtml`, `render` and `readEvents` functions were extracted from `index.html` and run in Node against the live server with "Is order 40 still under warranty?":

```
tool : Orders.GetOrder {"orderId":"40"}
tool : Docs.SearchPolicies {"query":"warranty period"}
events: {"tool":2,"toolResult":2,"token":110,"done":1}
html  : Order 40 is for a Laptop Pro 14, which has an extended 3-year manufacturer warranty ... <span class="cite">📄 warranty.md</span>.
xss   : &lt;img src=x onerror=alert(1)&gt; <strong>bold</strong> <span class="cite">📄 warranty.md</span>
```
- The SSE parser handled 115 events correctly.
- Citations became badges.
- The injection test was escaped (`&lt;img …&gt;`), so it is shown as text and not executed.

> ⏳ The page was **not** tested visually in a browser during development. Open it once to check the layout.

---

## 7. Troubleshooting

| # | Problem | Cause | Fix |
|---|---|---|---|
| 1 | Testing with curl from Git Bash: `Cannot transcode invalid UTF-8 JSON text … Unable to translate bytes [ED]` for "Ana García" | **Test-client problem, not the app.** The shell passed "í" as the Latin-1 byte `ED` instead of UTF-8. Browsers always send UTF-8. | Put the JSON body in a file and send it with `curl --data-binary @file.json` |
| 2 | Build error: `.exe … is being used by another process` | A previous `dotnet run` is still running | Stop it (Ctrl+C) before building |
| 3 | Answer appears all at once instead of streaming | Output buffered | Make sure `FlushAsync` is called after each event (and no response compression/buffering middleware is added) |
| 4 | `⚠ Azure OpenAI rate limit reached (HTTP 429)` in the chat | Deployment quota (tokens per minute) exceeded | Wait a minute, or raise the quota in Foundry |

---

## 8. Limitations and how to take it to production

| Limitation in the PoC | Production approach |
|---|---|
| Sessions in memory: lost on restart, don't work with several servers | Store chat history in a database or cache (Redis, Cosmos DB); trim or summarize long histories to control token cost |
| All users share the same fake orders (`OrdersPlugin` is a singleton) | Real database; filter data by the **authenticated user** inside the plugin, never trust the model to do it |
| No login | Authentication (Entra ID), and pass the user's identity to the plugins |
| Web project references the **console** project | Move shared code (plugins, RAG, `SupportAssistant`, `KernelFactory`) into a **class library** used by both |
| In-memory vector store, re-ingested at every start | Persistent store (Azure AI Search) with a separate ingestion job |
| API key in User Secrets | Managed identity / Key Vault |
| Two concurrent requests in the same session could mix the history | Lock per session, or disable the Send button (the page already does this) |
| Confirmation before cancelling is only a prompt rule | Approval filter, or a confirm button in the UI that the API checks |
| No monitoring | OpenTelemetry (SK emits traces and metrics), log token usage and tool calls |
| No rate limiting on our API | ASP.NET Core rate limiting middleware, per user |

---

## 9. Lessons learned
- **The SK code didn't change for the web.** The same kernel, plugins, RAG and prompt; only the "shell" (console vs HTTP) changed. That's the benefit of keeping the AI logic separate (`SupportAssistant`).
- **Streaming is essential for chat UX.** Answers take seconds; tokens arriving live feel much faster.
- **SSE is the natural fit for LLM streaming.** It's what the model APIs themselves use.
- **Filters make the AI transparent.** Showing tool calls in the UI builds user trust and makes debugging much easier.
- **Per-request state must not live in shared objects.** Hence `kernel.Clone()` with a per-request filter.
- **Treat model output as untrusted input.** Escape it before rendering.

---

## 10. Glossary

| Term | Definition |
|---|---|
| **Minimal API** | ASP.NET Core style where endpoints are lambdas registered with `MapGet`/`MapPost` |
| **SSE (Server-Sent Events)** | A standard for a server to push text events over one open HTTP response |
| **Flush** | Send buffered output to the client immediately |
| **Session** | The server-side state (chat history) for one user conversation |
| **Static files** | Files like HTML/CSS/JS served as they are from `wwwroot` |
| **XSS (cross-site scripting)** | Injecting HTML/JS into a page through untrusted text |
| **Class library** | A .NET project that produces a DLL to be shared by other projects, with no entry point |

---

## Next steps

The PoC is complete (Steps 1–5).
