using System.Text;
using System.Text.RegularExpressions;

namespace MarkLeaf.Services.AI;

internal sealed record AiSource(
    string Id,
    string DisplayPath,
    int StartLine,
    int EndLine,
    string Content,
    double Score);

internal static class AiKnowledgeRetriever
{
    private const int MaxFileBytes = 2 * 1024 * 1024;
    private const int TargetChunkCharacters = 1800;
    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".svn", "node_modules", "bin", "obj", "dist", "build", ".idea", ".vs",
    };
    private static readonly Regex LatinTokenRegex = new(
        @"[a-z0-9_\-.]+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static async Task<IReadOnlyList<AiSource>> RetrieveAsync(
        string? workspaceRoot,
        string? currentDocumentPath,
        string currentMarkdown,
        string query,
        bool includeCurrentDocument,
        bool includeWorkspace,
        int maxSources,
        CancellationToken cancellationToken = default)
    {
        var chunks = new List<SourceChunk>();
        var normalizedCurrentPath = NormalizePath(currentDocumentPath);

        if (includeCurrentDocument && !string.IsNullOrWhiteSpace(currentMarkdown))
        {
            var displayPath = GetDisplayPath(workspaceRoot, currentDocumentPath, "当前文档");
            chunks.AddRange(CreateChunks(displayPath, currentMarkdown, isCurrentDocument: true));
        }

        if (includeWorkspace && !string.IsNullOrWhiteSpace(workspaceRoot) && Directory.Exists(workspaceRoot))
        {
            foreach (var path in EnumerateKnowledgeFiles(workspaceRoot))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.Equals(NormalizePath(path), normalizedCurrentPath, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var info = new FileInfo(path);
                    if (info.Length > MaxFileBytes) continue;
                    var text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                    var displayPath = Path.GetRelativePath(workspaceRoot, path).Replace('\\', '/');
                    chunks.AddRange(CreateChunks(displayPath, text, isCurrentDocument: false));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // A locked or disappearing workspace file should not block all other sources.
                }
            }
        }

        var queryTerms = Tokenize(query);
        var ranked = chunks
            .Select(chunk => (Chunk: chunk, Score: Score(chunk, queryTerms)))
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Chunk.DisplayPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Chunk.StartLine)
            .Take(Math.Clamp(maxSources, 1, 12))
            .Select((item, index) => new AiSource(
                $"S{index + 1}",
                item.Chunk.DisplayPath,
                item.Chunk.StartLine,
                item.Chunk.EndLine,
                item.Chunk.Content,
                item.Score))
            .ToArray();

        return ranked;
    }

    public static string BuildSourceContext(IEnumerable<AiSource> sources)
    {
        var builder = new StringBuilder();
        foreach (var source in sources)
        {
            builder.AppendLine($"[{source.Id}] {source.DisplayPath}:{source.StartLine}-{source.EndLine}");
            builder.AppendLine(source.Content.Trim());
            builder.AppendLine();
        }
        return builder.ToString();
    }

    private static IEnumerable<string> EnumerateKnowledgeFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            IEnumerable<string> directories;
            IEnumerable<string> files;
            try
            {
                directories = Directory.EnumerateDirectories(directory).ToArray();
                files = Directory.EnumerateFiles(directory).ToArray();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var child in directories)
            {
                if (!IgnoredDirectories.Contains(Path.GetFileName(child))) pending.Push(child);
            }

            foreach (var file in files)
            {
                var extension = Path.GetExtension(file);
                if (extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".markdown", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    yield return file;
                }
            }
        }
    }

    private static IEnumerable<SourceChunk> CreateChunks(
        string displayPath,
        string text,
        bool isCurrentDocument)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var start = 0;
        while (start < lines.Length)
        {
            var builder = new StringBuilder();
            var end = start;
            while (end < lines.Length)
            {
                var nextLength = builder.Length + lines[end].Length + 1;
                if (builder.Length > 0 && nextLength > TargetChunkCharacters) break;
                builder.AppendLine(lines[end]);
                end++;
                if (builder.Length >= TargetChunkCharacters) break;
            }

            var content = builder.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(content))
                yield return new SourceChunk(displayPath, start + 1, end, content, isCurrentDocument);
            start = Math.Max(end, start + 1);
        }
    }

    private static double Score(SourceChunk chunk, IReadOnlySet<string> queryTerms)
    {
        var haystack = $"{chunk.DisplayPath}\n{chunk.Content}".ToLowerInvariant();
        var score = chunk.IsCurrentDocument ? 1.5 : 0;
        foreach (var term in queryTerms)
        {
            var index = 0;
            var occurrences = 0;
            while ((index = haystack.IndexOf(term, index, StringComparison.Ordinal)) >= 0 && occurrences < 8)
            {
                occurrences++;
                index += Math.Max(term.Length, 1);
            }
            score += occurrences * (term.Length >= 4 ? 2 : 1);
        }
        return score;
    }

    private static IReadOnlySet<string> Tokenize(string query)
    {
        var terms = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in LatinTokenRegex.Matches(query.ToLowerInvariant()))
        {
            if (match.Value.Length >= 2) terms.Add(match.Value);
        }

        var cjk = new string(query.Where(IsCjk).ToArray());
        for (var index = 0; index < cjk.Length - 1; index++)
            terms.Add(cjk.Substring(index, 2));
        if (cjk.Length == 1) terms.Add(cjk);
        return terms;
    }

    private static bool IsCjk(char value) => value is >= '\u3400' and <= '\u9fff';

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try { return Path.GetFullPath(path); }
        catch { return path; }
    }

    private static string GetDisplayPath(string? root, string? path, string fallback)
    {
        if (string.IsNullOrWhiteSpace(path)) return fallback;
        if (!string.IsNullOrWhiteSpace(root))
        {
            try { return Path.GetRelativePath(root, path).Replace('\\', '/'); }
            catch { }
        }
        return Path.GetFileName(path);
    }

    private sealed record SourceChunk(
        string DisplayPath,
        int StartLine,
        int EndLine,
        string Content,
        bool IsCurrentDocument);
}
