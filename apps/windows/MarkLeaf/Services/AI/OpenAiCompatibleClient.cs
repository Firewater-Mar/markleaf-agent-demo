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

        var systemPrompt = """
            你是 MarkLeaf AI，一个可信文档创作助手。你只能依据用户提供的资料回答。
            规则：
            1. 不得编造资料中不存在的事实、数据、引文或参考文献。
            2. 每个重要事实后必须使用 [S1] 这样的来源编号；编号只能来自所提供资料。
            3. 资料不足时明确写“现有资料不足以确认”，并列出需要补充的内容。
            4. 输出 Markdown，保持专业、简洁，并服从用户指定的任务类型。
            5. 来源之间冲突时，不擅自选择一方；指出冲突及各自来源。
            6. “当前文档”“这个文件”等指代只指任务中明确标出的操作主文件，不得擅自扩大为整个项目。
            7. 不要把资料核查过程、风险清单或“待补充资料”混入用户要求改写的正式正文；它们只能放在预览说明中。
            8. 表格必须使用正常的 Markdown 竖线，不要输出转义后的 \|。
            9. 做要求覆盖检查时，区分“文档中没有证明”和“产品实际未实现”；不得根据材料缺失断言功能不存在。
            10. 覆盖检查除了状态与证据，还要给出可执行的补充任务；文内引用不等于原始来源已经可靠核验。
            11. 严格遵守用户的字数、范围和输出格式，不要附上未在回答中实际使用的来源。
            """;
        var sourceContext = AiKnowledgeRetriever.BuildSourceContext(sources);
        var userPrompt = $"""
            任务类型：{taskInstruction}

            用户要求：
            {userRequest.Trim()}

            可用资料：
            {sourceContext}
            """;

        return await SendCompletionAsync(
            endpoint,
            model,
            apiKey,
            systemPrompt,
            userPrompt,
            0.2,
            cancellationToken).ConfigureAwait(false);
    }

    public Task<string> CompleteStructuredAsync(
        string endpoint,
        string model,
        string apiKey,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(systemPrompt)) throw new ArgumentException("请提供系统任务说明。", nameof(systemPrompt));
        if (string.IsNullOrWhiteSpace(userPrompt)) throw new ArgumentException("请提供结构化任务内容。", nameof(userPrompt));
        return SendCompletionAsync(
            endpoint,
            model,
            apiKey,
            systemPrompt,
            userPrompt,
            0,
            cancellationToken);
    }

    private static async Task<string> SendCompletionAsync(
        string endpoint,
        string model,
        string apiKey,
        string systemPrompt,
        string userPrompt,
        double temperature,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) throw new ArgumentException("请填写 API 地址。", nameof(endpoint));
        if (string.IsNullOrWhiteSpace(model)) throw new ArgumentException("请填写模型名称。", nameof(model));
        var url = BuildChatCompletionsUrl(endpoint);

        var payload = JsonSerializer.Serialize(new
        {
            model = model.Trim(),
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt },
            },
            temperature,
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

    public async Task<IReadOnlyList<string>> ListModelsAsync(
        string endpoint,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildModelsUrl(endpoint));
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());

        using var response = await Client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(BuildServiceError(response.StatusCode, responseText));

        using var document = JsonDocument.Parse(responseText);
        return ReadModelIds(document.RootElement);
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

    internal static Uri BuildModelsUrl(string endpoint)
    {
        var normalized = endpoint.Trim().TrimEnd('/');
        const string completionsSuffix = "/chat/completions";
        if (normalized.EndsWith(completionsSuffix, StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^completionsSuffix.Length];
        if (!normalized.EndsWith("/models", StringComparison.OrdinalIgnoreCase))
            normalized += "/models";
        return Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            ? uri
            : throw new ArgumentException("API 地址不是有效的网址。", nameof(endpoint));
    }

    internal static IReadOnlyList<string> ReadModelIds(JsonElement root)
    {
        var results = new List<string>();
        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
                if (item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                    AddModel(results, id.GetString());
        }
        if (root.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in models.EnumerateArray())
            {
                if (item.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                    AddModel(results, name.GetString());
                else if (item.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.String)
                    AddModel(results, model.GetString());
            }
        }
        return results;
    }

    private static void AddModel(List<string> results, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && !results.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase))
            results.Add(value.Trim());
    }

    private static string BuildServiceError(System.Net.HttpStatusCode statusCode, string responseText)
    {
        var detail = responseText.Length > 500 ? responseText[..500] + "…" : responseText;
        return $"AI 服务返回 {(int)statusCode}：{detail}";
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
