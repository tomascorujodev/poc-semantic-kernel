using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using PocSemanticKernel;
using PocSemanticKernel.Rag;
using PocSemanticKernel.Web;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// 1. Semantic Kernel: same configuration (User Secrets) and same assistant as Step 3.
var kernel = KernelFactory.Create(KernelFactory.LoadConfiguration());
var collection = await PolicyStore.CreateAsync(kernel);
SupportAssistant.AddPlugins(kernel, collection);
var chat = kernel.GetRequiredService<IChatCompletionService>();

// 2. One ChatHistory per browser session. In memory: lost on restart (fine for a PoC).
var sessions = new ConcurrentDictionary<string, ChatHistory>();

// 3. Serves wwwroot/index.html (the chat page).
app.UseDefaultFiles();
app.UseStaticFiles();

// 4. Chat endpoint. The answer is streamed back as server-sent events (SSE):
//    "tool" / "toolResult" (plugin calls), "token" (answer text), "done" or "error".
app.MapPost("/api/chat", async (ChatRequest request, HttpResponse response, CancellationToken ct) =>
{
    var history = sessions.GetOrAdd(request.SessionId, _ => new ChatHistory(SupportAssistant.SystemPrompt));

    response.ContentType = "text/event-stream";
    response.Headers.CacheControl = "no-cache";

    async Task SendEvent(string type, object data)
    {
        await response.WriteAsync($"event: {type}\ndata: {JsonSerializer.Serialize(data)}\n\n", ct);
        await response.Body.FlushAsync(ct); // push it to the browser now, don't buffer
    }

    // A per-request copy of the kernel, so the tool events go to THIS request's stream.
    var requestKernel = kernel.Clone();
    requestKernel.FunctionInvocationFilters.Add(new ToolEventFilter(SendEvent));

    history.AddUserMessage(request.Message);
    var answer = new StringBuilder();
    try
    {
        await foreach (var chunk in chat.GetStreamingChatMessageContentsAsync(
                           history, SupportAssistant.CreateSettings(), requestKernel, ct))
        {
            if (string.IsNullOrEmpty(chunk.Content)) continue;
            answer.Append(chunk.Content);
            await SendEvent("token", new { text = chunk.Content });
        }
        history.AddAssistantMessage(answer.ToString());
        await SendEvent("done", new { });
    }
    catch (HttpOperationException ex)
    {
        history.RemoveAt(history.Count - 1); // forget the unanswered question
        var message = ex.StatusCode == HttpStatusCode.TooManyRequests
            ? "Azure OpenAI rate limit reached (HTTP 429). Wait a minute and try again."
            : $"The AI service failed: {ex.Message}";
        await SendEvent("error", new { message });
    }
});

// 5. "New chat" button: forget the session's history.
app.MapPost("/api/reset", (ResetRequest request) =>
{
    sessions.TryRemove(request.SessionId, out _);
    return Results.Ok();
});

app.Run();

record ChatRequest(string SessionId, string Message);
record ResetRequest(string SessionId);
