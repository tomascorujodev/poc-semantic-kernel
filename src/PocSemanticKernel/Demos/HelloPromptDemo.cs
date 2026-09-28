using Microsoft.SemanticKernel;

namespace PocSemanticKernel.Demos;

/// <summary>Step 1: the simplest call. A prompt template goes to the model and text comes back.</summary>
public static class HelloPromptDemo
{
    public static async Task RunAsync(Kernel kernel)
    {
        // {{$topic}} is Semantic Kernel's prompt template syntax.
        var result = await kernel.InvokePromptAsync(
            "Explain {{$topic}} in two short sentences for a beginner developer.",
            new KernelArguments { ["topic"] = "Microsoft Semantic Kernel" });

        Console.WriteLine(result);
    }
}
