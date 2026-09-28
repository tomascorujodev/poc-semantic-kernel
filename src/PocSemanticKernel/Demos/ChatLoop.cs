using System.Text;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace PocSemanticKernel.Demos;

/// <summary>
/// The console chat loop shared by the demos: keeps a ChatHistory, streams answers,
/// and lets the model call any plugin registered in the kernel (automatic function calling).
/// </summary>
public static class ChatLoop
{
    public static async Task RunAsync(Kernel kernel, string systemPrompt, string intro)
    {
        // Auto() sends the plugin descriptions to the model and lets SK run the
        // functions it asks for, then sends the results back.
        var settings = SupportAssistant.CreateSettings();

        var chat = kernel.GetRequiredService<IChatCompletionService>();

        // The model has no memory; we resend the whole conversation each turn.
        var history = new ChatHistory(systemPrompt);

        Console.WriteLine($"{intro} Type 'exit' to quit.\n");

        while (true)
        {
            Console.Write("You > ");
            var input = Console.ReadLine();
            if (input is null) break; // end of input (e.g. piped stdin)
            if (string.IsNullOrWhiteSpace(input)) continue;
            if (input.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase)) break;

            history.AddUserMessage(input);

            // Streaming: print the answer token by token. Tool calls happen inside this call.
            Console.Write("AI  > ");
            var answer = new StringBuilder();
            await foreach (var chunk in chat.GetStreamingChatMessageContentsAsync(history, settings, kernel))
            {
                Console.Write(chunk.Content);
                answer.Append(chunk.Content);
            }
            Console.WriteLine("\n");

            history.AddAssistantMessage(answer.ToString());
        }
    }
}
