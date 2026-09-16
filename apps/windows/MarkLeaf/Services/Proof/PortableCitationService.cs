using MarkLeaf.Services.AI;

namespace MarkLeaf.Services.Proof;

internal static class PortableCitationService
{
    public static string ConvertAgentSourcesToFootnotes(
        string markdown,
        IReadOnlyList<AiSource> sources,
        DateTime? timestamp = null)
    {
        if (string.IsNullOrWhiteSpace(markdown) || sources.Count == 0) return markdown;
        var runId = (timestamp ?? DateTime.Now).ToString("yyyyMMddHHmmss");
        var result = markdown.Trim();
        var footnotes = new List<string>();
        foreach (var source in sources)
        {
            var token = $"ml-{runId}-{source.Id.ToLowerInvariant()}";
            var marker = $"[{source.Id}]";
            if (!result.Contains(marker, StringComparison.Ordinal)) continue;
            result = result.Replace(marker, $"[^{token}]", StringComparison.Ordinal);
            footnotes.Add($"[^{token}]: {source.DisplayPath}，第 {source.StartLine}-{source.EndLine} 行。由 MarkLeaf Agent 在本次任务中检索。");
        }
        return footnotes.Count == 0
            ? result
            : result + "\n\n## Agent 本次使用的来源\n\n" + string.Join("\n", footnotes);
    }
}
