using System.Text.RegularExpressions;

namespace MarkLeaf.Services.Proof;

internal static partial class RequirementAnalyzer
{
    private static readonly string[] StrongRequirementWords =
    [
        "应当", "必须", "需要", "要求", "提交", "包含", "说明", "不得", "评分", "分值", "可选",
        "shall", "must", "required", "include", "score", "points", "optional",
    ];

    public static IReadOnlyList<ProofRequirement> Analyze(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var results = new List<ProofRequirement>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = CleanupPrefix(rawLine.Trim());
            if (!LooksLikeRequirement(rawLine, line)) continue;
            var title = BuildTitle(line);
            if (title.Length < 2 || !seen.Add(title)) continue;
            results.Add(new ProofRequirement
            {
                Title = title,
                Description = line,
                Points = TryReadPoints(line),
                Required = !line.Contains("可选", StringComparison.OrdinalIgnoreCase)
                    && !line.Contains("optional", StringComparison.OrdinalIgnoreCase),
            });
            if (results.Count >= 80) break;
        }

        if (results.Count == 0)
        {
            foreach (var paragraph in text.Split(["\r\n\r\n", "\n\n"], StringSplitOptions.RemoveEmptyEntries))
            {
                var line = string.Join(" ", paragraph.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
                if (line.Length is < 4 or > 180) continue;
                var title = BuildTitle(line);
                if (!seen.Add(title)) continue;
                results.Add(new ProofRequirement { Title = title, Description = line });
                if (results.Count >= 20) break;
            }
        }
        return results;
    }

    private static bool LooksLikeRequirement(string rawLine, string line)
    {
        if (line.Length is < 3 or > 220) return false;
        if (rawLine.TrimStart().StartsWith('#')) return true;
        if (BulletPrefixRegex().IsMatch(rawLine)) return true;
        if (TryReadPoints(line) is not null) return true;
        return StrongRequirementWords.Any(word => line.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    private static string CleanupPrefix(string value)
        => BulletPrefixRegex().Replace(value.TrimStart('#', ' '), string.Empty).Trim();

    private static string BuildTitle(string line)
    {
        var withoutPoints = PointsRegex().Replace(line, string.Empty).Trim(' ', '：', ':', '-', '。', '.');
        var separator = withoutPoints.IndexOfAny(['：', ':', '。', '；', ';']);
        var title = separator is > 1 and < 38 ? withoutPoints[..separator] : withoutPoints;
        return title.Length > 44 ? title[..44].Trim() + "…" : title;
    }

    private static int? TryReadPoints(string line)
    {
        var match = PointsRegex().Match(line);
        return match.Success && int.TryParse(match.Groups[1].Value, out var points) ? points : null;
    }

    [GeneratedRegex(@"^\s*(?:[-*+]\s+|\d+[\.、\)]\s*|[（(]?[一二三四五六七八九十]+[)）、\.．]\s*)")]
    private static partial Regex BulletPrefixRegex();

    [GeneratedRegex(@"[（(]?\s*(\d{1,3})\s*(?:分|points?)\s*[)）]?", RegexOptions.IgnoreCase)]
    private static partial Regex PointsRegex();
}
