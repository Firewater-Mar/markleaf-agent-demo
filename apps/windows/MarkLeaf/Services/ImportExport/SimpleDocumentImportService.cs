using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace MarkLeaf.Services.ImportExport;

public sealed partial class SimpleDocumentImportService
{
    [GeneratedRegex(@"<!--.*?-->|<script\b.*?</script>|<style\b.*?</style>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex UnsafeRegex();
    [GeneratedRegex(@"<(h[1-6]|p|table)\b[^>]*>.*?</\1\s*>|<img\b[^>]*?/?>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex BlockRegex();
    [GeneratedRegex(@"<(?<tag>h[1-6]|p)\b[^>]*>(?<body>.*?)</\k<tag>\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TextRegex();
    [GeneratedRegex("""<img\b[^>]*\bsrc\s*=\s*(?:"(?<dq>[^"]*)"|'(?<sq>[^']*)'|(?<bare>[^\s>]+))[^>]*>""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ImageRegex();
    [GeneratedRegex(@"<tr\b[^>]*>(?<body>.*?)</tr\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex RowRegex();
    [GeneratedRegex(@"<t[hd]\b[^>]*>(?<body>.*?)</t[hd]\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex CellRegex();
    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)] private static partial Regex BreakRegex();
    [GeneratedRegex(@"<[^>]+>")] private static partial Regex TagRegex();

    public string Import(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".txt" => ImportText(File.ReadAllText(path)),
        ".html" or ".htm" => ImportHtml(File.ReadAllText(path)),
        _ => throw new NotSupportedException("仅支持导入 TXT、HTML 和 HTM。"),
    };
    public static string ImportText(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd() + Environment.NewLine;
    public static string ImportHtml(string html)
    {
        var output = new StringBuilder(); html = UnsafeRegex().Replace(html, string.Empty);
        foreach (Match match in BlockRegex().Matches(html))
        {
            var block = match.Value; var text = TextRegex().Match(block);
            if (text.Success)
            {
                var tag = text.Groups["tag"].Value.ToLowerInvariant(); var content = Clean(text.Groups["body"].Value); if (content.Length == 0) continue;
                if (tag[0] == 'h') output.Append(new string('#', tag[1] - '0')).Append(' ');
                output.AppendLine(content).AppendLine(); continue;
            }
            var image = ImageRegex().Match(block);
            if (image.Success)
            {
                var source = image.Groups["dq"].Success ? image.Groups["dq"].Value : image.Groups["sq"].Success ? image.Groups["sq"].Value : image.Groups["bare"].Value;
                output.Append("![](").Append(WebUtility.HtmlDecode(source)).AppendLine(")").AppendLine(); continue;
            }
            if (block.StartsWith("<table", StringComparison.OrdinalIgnoreCase)) AppendTable(output, block);
        }
        return output.ToString().TrimEnd() + Environment.NewLine;
    }
    private static void AppendTable(StringBuilder output, string html)
    {
        var rows = RowRegex().Matches(html).Select(row => CellRegex().Matches(row.Groups["body"].Value)
            .Select(cell => Clean(cell.Groups["body"].Value).Replace("|", "\\|", StringComparison.Ordinal)).ToArray()).Where(row => row.Length > 0).ToArray();
        if (rows.Length == 0) return; var columns = rows.Max(row => row.Length); Append(rows[0]); Append(Enumerable.Repeat("---", columns).ToArray());
        foreach (var row in rows.Skip(1)) Append(row); output.AppendLine();
        void Append(IReadOnlyList<string> row) { output.Append("| "); for (var i = 0; i < columns; i++) output.Append(i < row.Count ? row[i] : string.Empty).Append(" | "); output.AppendLine(); }
    }
    private static string Clean(string value) => WebUtility.HtmlDecode(TagRegex().Replace(BreakRegex().Replace(value, "  \n"), string.Empty)).Trim();
}
