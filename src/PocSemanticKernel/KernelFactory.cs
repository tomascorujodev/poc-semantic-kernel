using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace PocSemanticKernel;

/// <summary>
/// Builds the Kernel: the central object that holds AI services (chat, embeddings)
/// and plugins. Every Semantic Kernel feature runs through it.
/// </summary>
public static class KernelFactory
{
    public static IConfiguration LoadConfiguration() =>
        new ConfigurationBuilder()
            .AddUserSecrets<Program>()       // local dev: `dotnet user-secrets set ...`
            .AddEnvironmentVariables()       // CI/servers: AzureOpenAI__Endpoint, etc.
            .Build();

    public static Kernel Create(IConfiguration config, LogLevel logLevel = LogLevel.Warning)
    {
        var endpoint = Required(config, "AzureOpenAI:Endpoint");
        var apiKey = Required(config, "AzureOpenAI:ApiKey");
        var chatDeployment = Required(config, "AzureOpenAI:ChatDeployment");
        var embeddingDeployment = Required(config, "AzureOpenAI:EmbeddingDeployment");

        var builder = Kernel.CreateBuilder();

        // AI service (connector): chat completion backed by an Azure OpenAI deployment.
        builder.AddAzureOpenAIChatCompletion(chatDeployment, endpoint, apiKey);

        // AI service: embeddings (text -> vector of numbers), used by RAG in Step 3.
        // SKEXP0010 marks this API as experimental (it may change in future SK versions).
#pragma warning disable SKEXP0010
        builder.AddAzureOpenAIEmbeddingGenerator(embeddingDeployment, endpoint, apiKey);
#pragma warning restore SKEXP0010

        builder.Services.AddLogging(l => l.AddConsole().SetMinimumLevel(logLevel));

        return builder.Build();
    }

    private static string Required(IConfiguration config, string key) =>
        config[key] ?? throw new InvalidOperationException(
            $"Missing setting '{key}'. Set it with: dotnet user-secrets set \"{key}\" \"<value>\"");
}
