using MarkLeaf.Services.AI;
using System.Text.RegularExpressions;

namespace MarkLeaf.Services.Proof;

internal static partial class ProofSourceQueryService
{
    private const int TargetChunkCharacters = 1800;

    public static IReadOnlyList<AiSource> BuildSources(
        ProofProject project,
        string currentMarkdown,
        string currentDocumentName,
        string? currentDocumentPath,
        string query,
        int maxSources,
        bool currentDocumentOnly = false)
    {
        var terms = Tokenize(query);
        var chunks = new List<SourceChunk>();
        AddChunks(chunks, currentDocumentName, currentMarkdown, terms, current: true, kind: "Markdown");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in project.Sources.Where(source => !string.IsNullOrWhiteSpace(source.ExtractedText)))
        {
            if (currentDocumentOnly || PathsEqual(source.FilePath, currentDocumentPath)) continue;
            var identity = SourceIdentity(source);
            if (!seen.Add(identity)) continue;
            AddChunks(chunks, source.DisplayName, source.ExtractedText, terms, current: false, source.Kind);
        }

        return chunks
            .OrderByDescending(chunk => chunk.Score)
            .ThenBy(chunk => chunk.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(chunk => chunk.Start)
            .Take(Math.Clamp(maxSources, 1, 16))
            .Select((chunk, index) => new AiSource(
                $"S{index + 1}",
                chunk.Path,
                chunk.Start,
                chunk.End,
                chunk.Text,
                chunk.Score,
                chunk.Locator))
            .ToArray();
    }

    public static bool ShouldUseOnlyCurrentDocument(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return false;
        var explicitlyCurrent = new[] { "当前文档", "当前文件", "这个文件", "本文件", "本文档" }
            .Any(signal => query.Contains(signal, StringComparison.OrdinalIgnoreCase));
        if (!explicitlyCurrent) return false;
        return !new[] { "项目资料", "全部资料", "所有资料", "根据资料", "结合资料", "证据", "来源", "要求", "比赛" }
            .Any(signal => query.Contains(signal, StringComparison.OrdinalIgnoreCase));
    }

    private static string SourceIdentity(ProofSource source)
    {
        if (!string.IsNullOrWhiteSpace(source.FilePath))
        {
            try { return Path.GetFullPath(source.FilePath); }
            catch { return source.FilePath.Trim(); }
        }
        return source.DisplayName.Trim();
    }

    private static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        try { return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase); }
        catch { return string.Equals(left, right, StringComparison.OrdinalIgnoreCase); }
    }

    private static void AddChunks(
        ICollection<SourceChunk> destination,
        string path,
        string text,
        IReadOnlyList<string> terms,
        bool current,
        string? kind)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (string.Equals(kind, "PDF", StringComparison.OrdinalIgnoreCase)
            && text.Contains("\u001epage:", StringComparison.Ordinal))
        {
            AddPdfChunks(destination, path, text, terms);
            return;
        }

        AddTextChunks(destination, path, text, terms, current, locator: null);
    }

    private static void AddPdfChunks(
        ICollection<SourceChunk> destination,
        string path,
        string text,
        IReadOnlyList<string> terms)
    {
        var matches = PdfPageMarkerRegex().Matches(text);
        for (var index = 0; index < matches.Count; index++)
        {
            var page = int.Parse(matches[index].Groups[1].Value);
            var start = matches[index].Index + matches[index].Length;
            var end = index + 1 < matches.Count ? matches[index + 1].Index : text.Length;
            AddTextChunks(destination, path, text[start..end], terms, current: false, $"第 {page} 页");
        }
    }

    private static void AddTextChunks(
        ICollection<SourceChunk> destination,
        string path,
        string text,
        IReadOnlyList<string> terms,
        bool current,
        string? locator)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var start = 0; start < lines.Length;)
        {
            var selected = new List<string>();
            var characters = 0;
            var end = start;
            while (end < lines.Length && characters < TargetChunkCharacters)
            {
                selected.Add(lines[end]);
                characters += lines[end].Length + 1;
                end++;
            }
            var content = string.Join("\n", selected).Trim();
            if (!string.IsNullOrWhiteSpace(content))
            {
                var haystack = (path + "\n" + content).ToLowerInvariant();
                var score = current ? 1.5 : 0;
                foreach (var term in terms)
                    if (haystack.Contains(term, StringComparison.Ordinal)) score += term.Length >= 4 ? 2 : 1;
                destination.Add(new SourceChunk(path, start + 1, end, content, score, locator));
            }
            start = Math.Max(end, start + 1);
        }
    }

    private static IReadOnlyList<string> Tokenize(string query)
    {
        var terms = TokenRegex().Matches(query.ToLowerInvariant())
            .Select(match => match.Value)
            .Where(value => value.Length >= 2 && !value.Any(IsCjk))
            .ToHashSet(StringComparer.Ordinal);
        var cjk = new string(query.Where(IsCjk).ToArray());
        for (var index = 0; index < cjk.Length - 1; index++)
            terms.Add(cjk.Substring(index, 2));
        if (cjk.Length == 1) terms.Add(cjk);
        return terms.ToArray();
    }

    private static bool IsCjk(char value) => value is >= '\u3400' and <= '\u9fff';

    private sealed record SourceChunk(
        string Path,
        int Start,
        int End,
        string Text,
        double Score,
        string? Locator);

    [GeneratedRegex(@"[\p{L}\p{N}_\-.]{2,}")]
    private static partial Regex TokenRegex();

    [GeneratedRegex("\\u001epage:(\\d+)\\u001e")]
    private static partial Regex PdfPageMarkerRegex();
}
