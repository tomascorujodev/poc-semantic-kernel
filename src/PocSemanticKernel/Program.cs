using PocSemanticKernel;
using PocSemanticKernel.Demos;

var config = KernelFactory.LoadConfiguration();

// Pick a demo from the command line (dotnet run -- 2) or from a menu.
var choice = args.FirstOrDefault();
if (choice is null)
{
    Console.WriteLine("""
        Semantic Kernel PoC
          1) Hello prompt        (Step 1)
          2) Chat with plugins   (Step 2)
          3) Chat with documents (Step 3, RAG)
          4) Multi-agent support (Step 4, handoff)
        """);
    Console.Write("Choose: ");
    choice = Console.ReadLine()?.Trim();
}

var kernel = KernelFactory.Create(config);

switch (choice)
{
    case "1": await HelloPromptDemo.RunAsync(kernel); break;
    case "2": await ChatWithPluginsDemo.RunAsync(kernel); break;
    case "3": await RagDemo.RunAsync(kernel); break;
    case "4": await HandoffDemo.RunAsync(kernel); break;
    default: Console.WriteLine($"Unknown option '{choice}'."); break;
}
