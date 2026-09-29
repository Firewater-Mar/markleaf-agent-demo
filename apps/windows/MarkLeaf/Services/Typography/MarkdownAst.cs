using System.Text.RegularExpressions;

namespace MarkLeaf.Services.Typography;

public enum MarkdownBlockKind { Blank, Heading, Paragraph, Image, Table, Code, Html }
public sealed record MarkdownBlock(int Index, MarkdownBlockKind Kind, int StartLine, int EndLine,
    int StartOffset, int Length, string Text, int HeadingLevel = 0);

public static partial class MarkdownAst
{
    [GeneratedRegex(@"^\s{0,3}(#{1,6})\s+(.+?)\s*#*\s*$")]
    private static partial Regex HeadingRegex();
    [GeneratedRegex(@"^\s*!\[[^\]]*\]\([^\r\n]+\)\s*$")]
    private static partial Regex ImageRegex();

    public static IReadOnlyList<MarkdownBlock> Parse(string markdown)
    {
        var source = Normalize(markdown);
        var lines = source.Split('\n');
        var offsets = new int[lines.Length];
        for (var i = 1; i < lines.Length; i++) offsets[i] = offsets[i - 1] + lines[i - 1].Length + 1;
        var result = new List<MarkdownBlock>();
        var line = 0;
        while (line < lines.Length)
        {
            var start = line;
            var value = lines[line];
            if (IsFence(value))
            {
                line++;
                while (line < lines.Length && !IsFence(lines[line])) line++;
                if (line < lines.Length) line++;
                Add(MarkdownBlockKind.Code, start, Math.Max(start, line - 1));
            }
            else if (string.IsNullOrWhiteSpace(value))
            {
                while (line + 1 < lines.Length && string.IsNullOrWhiteSpace(lines[line + 1])) line++;
                Add(MarkdownBlockKind.Blank, start, line++);
            }
            else if (HeadingRegex().Match(value) is { Success: true } heading)
            {
                Add(MarkdownBlockKind.Heading, start, line++, heading.Groups[1].Length);
            }
            else if (ImageRegex().IsMatch(value) || value.TrimStart().StartsWith("<img", StringComparison.OrdinalIgnoreCase))
                Add(MarkdownBlockKind.Image, start, line++);
            else if (line + 1 < lines.Length && IsTableRow(value) && IsSeparator(lines[line + 1]))
            {
                line += 2;
                while (line < lines.Length && IsTableRow(lines[line]) && !string.IsNullOrWhiteSpace(lines[line])) line++;
                Add(MarkdownBlockKind.Table, start, line - 1);
            }
            else if (value.TrimStart().StartsWith('<')) Add(MarkdownBlockKind.Html, start, line++);
            else
            {
                line++;
                while (line < lines.Length && !string.IsNullOrWhiteSpace(lines[line])
                    && !HeadingRegex().IsMatch(lines[line]) && !ImageRegex().IsMatch(lines[line])
                    && !IsFence(lines[line]) && !(line + 1 < lines.Length && IsTableRow(lines[line]) && IsSeparator(lines[line + 1]))) line++;
                Add(MarkdownBlockKind.Paragraph, start, line - 1);
            }
        }
        return result;

        void Add(MarkdownBlockKind kind, int first, int last, int level = 0)
        {
            var from = offsets[first];
            var to = offsets[last] + lines[last].Length;
            result.Add(new(result.Count, kind, first + 1, last + 1, from, to - from, source[from..to], level));
        }
    }

    public static string Normalize(string value) => (value ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    private static bool IsFence(string value) => value.TrimStart().StartsWith("```", StringComparison.Ordinal) || value.TrimStart().StartsWith("~~~", StringComparison.Ordinal);
    private static bool IsTableRow(string value) => value.Count(c => c == '|') > 0;
    private static bool IsSeparator(string value) => value.Trim().Trim('|').Split('|').All(cell =>
    {
        var text = cell.Trim().Trim(':');
        return text.Length >= 3 && text.All(c => c == '-');
    });
}
