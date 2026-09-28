using System.Text.Json;
using Microsoft.SemanticKernel;

namespace PocSemanticKernel.Filters;

/// <summary>
/// A function invocation filter: runs around every KernelFunction call.
/// Here it prints each plugin call so you can see what the model decided to do.
/// Filters are also the place for security checks, human approval, or telemetry.
/// </summary>
public sealed class LoggingFilter : IFunctionInvocationFilter
{
    public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
    {
        // Prompt functions (like InvokePromptAsync) have no plugin name; only log plugin tools.
        if (context.Function.PluginName is null)
        {
            await next(context);
            return;
        }

        var args = string.Join(", ", context.Arguments.Select(a => $"{a.Key}={a.Value}"));
        WriteColored($"  🔧 {context.Function.PluginName}.{context.Function.Name}({args})", ConsoleColor.DarkYellow);

        await next(context); // runs the actual C# method (or the next filter)

        var value = context.Result.GetValue<object>();
        var json = value is string s ? s : JsonSerializer.Serialize(value);
        WriteColored($"  ↳ {Truncate(json, 200)}", ConsoleColor.DarkGray);
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max] + "…";

    private static void WriteColored(string text, ConsoleColor color)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ForegroundColor = previous;
    }
}
