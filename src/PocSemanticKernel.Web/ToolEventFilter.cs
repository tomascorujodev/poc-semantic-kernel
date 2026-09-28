using System.Text.Json;
using Microsoft.SemanticKernel;

namespace PocSemanticKernel.Web;

/// <summary>
/// The web version of LoggingFilter: instead of printing plugin calls to the console,
/// it sends them to the browser as server-sent events, so the UI can show them.
/// </summary>
public sealed class ToolEventFilter(Func<string, object, Task> sendEvent) : IFunctionInvocationFilter
{
    public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
    {
        var name = $"{context.Function.PluginName}.{context.Function.Name}";
        var args = context.Arguments.ToDictionary(a => a.Key, a => a.Value?.ToString());
        await sendEvent("tool", new { name, args });

        await next(context); // runs the actual C# method

        var value = context.Result.GetValue<object>();
        var json = value is string s ? s : JsonSerializer.Serialize(value);
        await sendEvent("toolResult", new { name, preview = json.Length <= 300 ? json : json[..300] + "…" });
    }
}
