namespace MarkLeaf.AI.Contracts;

public sealed record AiConnectionOptions(string BaseUrl, string Model, string ApiKey, int TimeoutSeconds = 45);

public interface ITypographyAiClient
{
    Task TestConnectionAsync(AiConnectionOptions options, CancellationToken cancellationToken = default);

    Task<string> CompleteJsonAsync(AiConnectionOptions options, string systemPrompt, string userInput,
        CancellationToken cancellationToken = default);
}
