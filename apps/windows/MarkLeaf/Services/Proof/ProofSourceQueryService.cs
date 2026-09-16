using MarkLeaf.Services.AI;
using System.Text.RegularExpressions;

namespace MarkLeaf.Services.Proof;

internal static partial class ProofSourceQueryService
{
    public static IReadOnlyList<AiSource> BuildSources(
        ProofProject project,
        string currentMarkdown,
        string currentDocumentName,
        string query,
        int maxSources)
    {
        var terms = TokenRegex().Matches(query.ToLowerInvariant())
            .Select(match => match.Value)
            .Where(value => value.Length >= 2)
            .Distinct()
            .ToArray();
        var chunks = new List<(string Path, int Start, int End, string Text, double Score)>();
        AddChunks(chunks, currentDocumentName, currentMarkdown, terms, current: true);
        foreach (var source in project.Sources.Where(source => !string.IsNullOrWhiteSpace(source.ExtractedText)))
            AddChunks(chunks, source.DisplayName, source.ExtractedText, terms, current: false);

        return chunks
            .OrderByDescending(chunk => chunk.Score)
            .ThenBy(chunk => chunk.Path, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(maxSources, 1, 16))
            .Select((chunk, index) => new AiSource(
                $"S{index + 1}",
                chunk.Path,
                chunk.Start,
                chunk.End,
                chunk.Text,
                chunk.Score))
            .ToArray();
    }

    private static void AddChunks(
        ICollection<(string Path, int Start, int End, string Text, double Score)> destination,
        string path,
        string text,
        IReadOnlyList<string> terms,
        bool current)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var start = 0; start < lines.Length;)
        {
            var selected = new List<string>();
            var characters = 0;
            var end = start;
            while (end < lines.Length && characters < 1800)
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
                destination.Add((path, start + 1, end, content, score));
            }
            start = Math.Max(end, start + 1);
        }
    }

    [GeneratedRegex(@"[\p{L}\p{N}_\-.]{2,}")]
    private static partial Regex TokenRegex();
}
