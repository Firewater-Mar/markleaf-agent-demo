using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MarkLeaf.AI.Contracts;

namespace MarkLeaf.AI.OpenAICompatible;

public sealed class OpenAICompatibleTypographyClient : ITypographyAiClient
{
    public async Task TestConnectionAsync(AiConnectionOptions options, CancellationToken cancellationToken = default)
    {
        _ = await CompleteJsonAsync(options,
            "Return one JSON object only. Do not include Markdown fences.",
            "Return {\"status\":\"ok\"}.", cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> CompleteJsonAsync(AiConnectionOptions options, string systemPrompt, string userInput,
        CancellationToken cancellationToken = default)
    {
        Validate(options);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 10, 120)) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MarkLeaf-Typography/2.7.8");

        var endpoint = BuildEndpoint(options.BaseUrl);
        var (response, body) = await SendAsync(structuredOutput: true).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode && response.StatusCode == System.Net.HttpStatusCode.BadRequest
            && body.Contains("response_format", StringComparison.OrdinalIgnoreCase))
        {
            response.Dispose();
            (response, body) = await SendAsync(structuredOutput: false).ConfigureAwait(false);
        }
        using (response)
        {
        if (!response.IsSuccessStatusCode)
        {
            var detail = ReadError(body);
            throw new HttpRequestException($"API 返回 {(int)response.StatusCode} {response.ReasonPhrase}。{detail}");
        }
        }

        string? content;
        try
        {
            using var document = JsonDocument.Parse(body);
            content = document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidDataException("API 响应格式不兼容，未找到 choices[0].message.content。", ex);
        }
        if (string.IsNullOrWhiteSpace(content)) throw new InvalidDataException("API 没有返回可用的规则 JSON。");
        return StripFence(content);

        async Task<(HttpResponseMessage Response, string Body)> SendAsync(bool structuredOutput)
        {
            object payload = structuredOutput
                ? new
                {
                    model = options.Model,
                    temperature = 0,
                    response_format = new { type = "json_object" },
                    messages = new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = userInput },
                    },
                }
                : new
                {
                    model = options.Model,
                    temperature = 0,
                    messages = new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = userInput },
                    },
                };
            var result = await client.PostAsJsonAsync(endpoint, payload, cancellationToken).ConfigureAwait(false);
            return (result, await result.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        }
    }

    private static Uri BuildEndpoint(string baseUrl)
    {
        var value = baseUrl.Trim().TrimEnd('/');
        if (value.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)) return new Uri(value, UriKind.Absolute);
        return new Uri(value + "/chat/completions", UriKind.Absolute);
    }

    private static void Validate(AiConnectionOptions options)
    {
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new InvalidDataException("API 地址必须是有效的 HTTP 或 HTTPS 地址。");
        if (string.IsNullOrWhiteSpace(options.Model)) throw new InvalidDataException("请填写模型名称。");
        if (string.IsNullOrWhiteSpace(options.ApiKey)) throw new InvalidDataException("请填写 API Key。");
    }

    private static string ReadError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var message))
                return " " + message.GetString();
        }
        catch (JsonException) { }
        return string.Empty;
    }

    private static string StripFence(string value)
    {
        var text = value.Trim();
        if (!text.StartsWith("```", StringComparison.Ordinal)) return text;
        var firstLine = text.IndexOf('\n');
        var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
        return firstLine >= 0 && lastFence > firstLine ? text[(firstLine + 1)..lastFence].Trim() : text;
    }
}
