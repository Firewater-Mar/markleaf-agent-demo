using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace MarkLeaf.Services.AI;

internal sealed class OpenAiCompatibleClient
{
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromMinutes(3),
    };

    public async Task<string> CompleteAsync(
        string endpoint,
        string model,
        string apiKey,
        string taskInstruction,
        string userRequest,
        IReadOnlyList<AiSource> sources,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) throw new ArgumentException("请填写 API 地址。", nameof(endpoint));
        if (string.IsNullOrWhiteSpace(model)) throw new ArgumentException("请填写模型名称。", nameof(model));
        if (string.IsNullOrWhiteSpace(userRequest)) throw new ArgumentException("请填写任务要求。", nameof(userRequest));
        if (sources.Count == 0) throw new InvalidOperationException("没有可供 AI 使用的资料。请打开文档或工作区。");

        var url = BuildChatCompletionsUrl(endpoint);
        var systemPrompt = """
            你是 MarkLeaf AI，一个可信文档创作助手。你只能依据用户提供的资料回答。
            规则：
            1. 不得编造资料中不存在的事实、数据、引文或参考文献。
            2. 每个重要事实后必须使用 [S1] 这样的来源编号；编号只能来自所提供资料。
            3. 资料不足时明确写“现有资料不足以确认”，并列出需要补充的内容。
            4. 输出 Markdown，保持专业、简洁，并服从用户指定的任务类型。
            5. 来源之间冲突时，不擅自选择一方；指出冲突及各自来源。
            """;
        var sourceContext = AiKnowledgeRetriever.BuildSourceContext(sources);
        var userPrompt = $"""
            任务类型：{taskInstruction}

            用户要求：
            {userRequest.Trim()}

            可用资料：
            {sourceContext}
            """;

        var payload = JsonSerializer.Serialize(new
        {
            model = model.Trim(),
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt },
            },
            temperature = 0.2,
            stream = false,
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());

        using var response = await Client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var detail = responseText.Length > 800 ? responseText[..800] + "…" : responseText;
            throw new HttpRequestException($"AI 服务返回 {(int)response.StatusCode}：{detail}");
        }

        using var document = JsonDocument.Parse(responseText);
        if (TryReadOpenAiContent(document.RootElement, out var content)
            || TryReadOllamaContent(document.RootElement, out content))
        {
            return content.Trim();
        }
        throw new InvalidDataException("AI 服务返回了无法识别的数据格式。请确认它兼容 OpenAI Chat Completions API。");
    }

    internal static Uri BuildChatCompletionsUrl(string endpoint)
    {
        var normalized = endpoint.Trim().TrimEnd('/');
        if (!normalized.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            normalized += "/chat/completions";
        return Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            ? uri
            : throw new ArgumentException("API 地址不是有效的网址。", nameof(endpoint));
    }

    private static bool TryReadOpenAiContent(JsonElement root, out string content)
    {
        content = string.Empty;
        if (!root.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
            return false;
        var first = choices[0];
        if (!first.TryGetProperty("message", out var message)
            || !message.TryGetProperty("content", out var value)
            || value.ValueKind != JsonValueKind.String)
            return false;
        content = value.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(content);
    }

    private static bool TryReadOllamaContent(JsonElement root, out string content)
    {
        content = string.Empty;
        if (!root.TryGetProperty("message", out var message)
            || !message.TryGetProperty("content", out var value)
            || value.ValueKind != JsonValueKind.String)
            return false;
        content = value.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(content);
    }
}
