namespace MarkLeaf.Services.AI;

public sealed record AiModelPreset(string Id, string Name, string Description)
{
    public string DisplayText => $"{Name} — {Description}";
}

public sealed record AiProviderPreset(
    string Id,
    string Name,
    string BaseUrl,
    string DefaultModel,
    IReadOnlyList<AiModelPreset> Models);

public static class AiProviderCatalog
{
    public static IReadOnlyList<AiProviderPreset> All { get; } =
    [
        new("openai", "OpenAI", "https://api.openai.com/v1", "gpt-5-mini",
        [
            new("gpt-5-mini", "GPT-5 mini（推荐）", "理解力与成本较均衡"),
            new("gpt-4.1-mini", "GPT-4.1 mini", "响应快，兼容性好"),
            new("gpt-4.1", "GPT-4.1", "适合复杂模板和长要求"),
        ]),
        new("deepseek", "DeepSeek", "https://api.deepseek.com", "deepseek-flash",
        [
            new("deepseek-flash", "DeepSeek Flash（推荐）", "速度快，适合日常排版任务"),
            new("deepseek-v4-pro", "DeepSeek V4 Pro", "适合复杂约束与模板理解"),
        ]),
        new("kimi", "Kimi / Moonshot", "https://api.moonshot.ai/v1", "kimi-k2.5",
        [
            new("kimi-k2.5", "Kimi K2.5（推荐）", "适合中文长文本与复杂要求"),
        ]),
        new("qwen", "通义千问 / 百炼", "https://dashscope.aliyuncs.com/compatible-mode/v1", "qwen3.8-flash",
        [
            new("qwen3.8-flash", "Qwen 3.8 Flash（推荐）", "响应快，适合日常任务"),
            new("qwen3.7-plus", "Qwen 3.7 Plus", "质量与成本较均衡"),
            new("qwen3.8-max", "Qwen 3.8 Max", "适合复杂模板和严格约束"),
        ]),
        new("zhipu", "智谱 GLM", "https://open.bigmodel.cn/api/paas/v4", "glm-4-flash",
        [
            new("glm-4-flash", "GLM-4 Flash（推荐）", "快速、低成本的文本理解"),
        ]),
        new("custom", "自定义兼容接口", string.Empty, string.Empty, []),
    ];

    public static AiProviderPreset Find(string? id) =>
        All.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal)) ?? All[0];
}
