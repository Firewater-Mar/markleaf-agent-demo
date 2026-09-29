using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MarkLeaf.Services.Typography;

public sealed class TypographyEvaluator
{
    public TypographyAssessment Evaluate(string markdown, TypographyTemplate template,
        IReadOnlyList<DomBlockMeasurement>? measurements = null, TypographyConstraints? constraints = null)
    {
        constraints ??= TypographyConstraints.Empty;
        var rules = template.Rules;
        rules.Normalize();
        var blocks = MarkdownAst.Parse(markdown);
        var metrics = (measurements ?? []).GroupBy(x => x.BlockIndex).ToDictionary(x => x.Key, x => x.First());
        var defects = new List<TypographyDefect>();
        var previousHeading = 0;
        var headings = 0;
        foreach (var block in blocks)
        {
            if (block.Kind == MarkdownBlockKind.Heading)
            {
                headings++;
                if (previousHeading > 0 && block.HeadingLevel > previousHeading + 1)
                    Add(block, TypographyDefectType.HeadingLevelJump, DefectSeverity.Error,
                        $"标题从 H{previousHeading} 跳到 H{block.HeadingLevel}。",
                        new Dictionary<string, double> { ["previous"] = previousHeading, ["current"] = block.HeadingLevel });
                previousHeading = block.HeadingLevel;
            }
            if (block.Kind == MarkdownBlockKind.Blank)
            {
                var count = block.EndLine - block.StartLine + 1;
                if (count > rules.ConsecutiveBlankLineLimit)
                    Add(block, TypographyDefectType.ExcessBlankLines, DefectSeverity.Warning,
                        $"连续空行 {count} 行，模板最多允许 {rules.ConsecutiveBlankLineLimit} 行。");
            }
            if (block.Kind == MarkdownBlockKind.Paragraph)
            {
                var length = VisibleLength(block.Text);
                var maxLine = constraints.MaxLineChars ?? rules.MaxLineChars;
                if (length > maxLine * 3)
                    Add(block, TypographyDefectType.LongLine, length > maxLine * 5 ? DefectSeverity.Error : DefectSeverity.Warning,
                        $"段落约 {length} 字，纸面行长/段长影响阅读（只提示，不改写正文）。");
                var next = blocks.ElementAtOrDefault(block.Index + 2);
                if (length <= rules.ShortParagraphChars && next?.Kind == MarkdownBlockKind.Paragraph
                    && VisibleLength(next.Text) <= rules.ShortParagraphChars)
                    Add(block, TypographyDefectType.FragmentedParagraph, DefectSeverity.Warning, "连续短段落较碎片化（只提示，不改写正文）。");
            }
            if (!metrics.TryGetValue(block.Index, out var metric)) continue;
            var ratio = metric.ContentWidth <= 0 ? 0 : metric.Width / metric.ContentWidth * 100;
            if (block.Kind == MarkdownBlockKind.Image && ratio > (constraints.ImageMaxWidth ?? rules.ImageMaxWidth) + .5)
                Add(block, TypographyDefectType.ImageOverflow, DefectSeverity.Error, $"图片占版心 {ratio:F0}%，发生溢出。",
                    new Dictionary<string, double> { ["width_percent"] = ratio });
            if (block.Kind == MarkdownBlockKind.Table && ratio > rules.TableMaxWidth + .5)
                Add(block, TypographyDefectType.TableOverflow, DefectSeverity.Error, $"表格占版心 {ratio:F0}%，发生溢出。",
                    new Dictionary<string, double> { ["width_percent"] = ratio });
            if (block.Kind == MarkdownBlockKind.Paragraph) AddPageDefects(block, metric, rules, defects);
        }
        var wantToc = constraints.GenerateTableOfContents ?? rules.GenerateTableOfContents;
        if (wantToc && headings >= rules.MinimumTocHeadings && !markdown.Contains("<!-- markleaf:toc -->", StringComparison.OrdinalIgnoreCase))
        {
            var first = blocks.First(x => x.Kind == MarkdownBlockKind.Heading);
            Add(first, TypographyDefectType.MissingTableOfContents, DefectSeverity.Info, $"文档有 {headings} 个标题，建议生成目录。");
        }
        var score = Math.Max(0, 100 - defects.Sum(x => x.Severity switch { DefectSeverity.Error => 12, DefectSeverity.Warning => 6, _ => 2 }));
        return new(score, defects, DateTimeOffset.UtcNow, template.Id);

        void Add(MarkdownBlock block, TypographyDefectType type, DefectSeverity severity, string message,
            IReadOnlyDictionary<string, double>? values = null) => defects.Add(new(
                $"{type}:{block.StartLine}:{block.Index}", type, severity,
                new(block.StartLine, 1, block.Length, block.Index), message, values));
    }

    private static void AddPageDefects(MarkdownBlock block, DomBlockMeasurement m, TypographyRules rules, List<TypographyDefect> defects)
    {
        if (m.PageHeight <= 0 || m.LineHeight <= 0 || m.Height < m.LineHeight * 2) return;
        var onPage = ((m.Top % m.PageHeight) + m.PageHeight) % m.PageHeight;
        var firstSpace = m.PageHeight - onPage;
        if (!rules.AllowOrphan && firstSpace > 0 && firstSpace < m.LineHeight * 1.8) Add(TypographyDefectType.OrphanLine, "段落在页尾仅剩一行。");
        var remainder = ((onPage + m.Height) % m.PageHeight + m.PageHeight) % m.PageHeight;
        if (!rules.AllowWidow && m.Height > firstSpace && remainder is > 0 && remainder < m.LineHeight * 1.8) Add(TypographyDefectType.WidowLine, "段落在新页仅有一行。");
        void Add(TypographyDefectType type, string message) => defects.Add(new($"{type}:{block.StartLine}:{block.Index}", type,
            DefectSeverity.Error, new(block.StartLine, 1, block.Length, block.Index), message,
            new Dictionary<string, double> { ["top"] = m.Top, ["height"] = m.Height, ["page_height"] = m.PageHeight }));
    }

    private static int VisibleLength(string value) => value.Count(c => !char.IsWhiteSpace(c) && c is not '*' and not '_' and not '`' and not '[' and not ']' and not '(' and not ')' and not '#');
}

public sealed class TypographyPlanner
{
    public IReadOnlyList<TypographyTask> Plan(TypographyAssessment assessment, TypographyConstraints? constraints = null)
    {
        var forbidden = constraints?.ForbiddenTaskTypes ?? new HashSet<TypographyTaskType>();
        return assessment.Defects.Select(x => (Defect: x, Task: Map(x.Type)))
            .Where(x => x.Task is not null && !forbidden.Contains(x.Task.Value)).GroupBy(x => x.Task!.Value)
            .Select(group =>
            {
                var items = group.Select(x => x.Defect).ToArray();
                var line = items.Min(x => x.Location.Line);
                return new TypographyTask($"{group.Key}:{line}", group.Key,
                    items.Max(x => (int)x.Severity) * 1000 - line, Describe(group.Key, items.Length), items.Select(x => x.Id).ToArray());
            }).OrderByDescending(x => x.Priority).ToArray();
    }
    private static TypographyTaskType? Map(TypographyDefectType type) => type switch
    {
        TypographyDefectType.HeadingLevelJump => TypographyTaskType.NormalizeHeadings,
        TypographyDefectType.ExcessBlankLines => TypographyTaskType.CollapseBlankLines,
        TypographyDefectType.ImageOverflow => TypographyTaskType.ScaleImages,
        TypographyDefectType.TableOverflow => TypographyTaskType.FitTables,
        TypographyDefectType.OrphanLine or TypographyDefectType.WidowLine => TypographyTaskType.RepairPageBreaks,
        TypographyDefectType.MissingTableOfContents => TypographyTaskType.GenerateTableOfContents,
        _ => null, // Never rewrite prose.
    };
    private static string Describe(TypographyTaskType type, int count) => type switch
    {
        TypographyTaskType.NormalizeHeadings => $"修复 {count} 处标题层级",
        TypographyTaskType.CollapseBlankLines => $"清理 {count} 处空行",
        TypographyTaskType.ScaleImages => $"缩放 {count} 张图片",
        TypographyTaskType.FitTables => $"适配 {count} 个表格",
        TypographyTaskType.RepairPageBreaks => $"修复 {count} 处孤行/寡行",
        _ => "生成目录",
    };
}

public sealed partial class TypographyExecutor
{
    [GeneratedRegex(@"^(?<indent>\s*)!\[(?<alt>[^\]]*)\]\((?<target>.+)\)\s*$")]
    private static partial Regex ImageRegex();

    public TypographyExecutionResult Execute(string markdown, IReadOnlyList<TypographyTask> tasks,
        TypographyTemplate template, TypographyConstraints? constraints = null)
    {
        var current = MarkdownAst.Normalize(markdown);
        var changes = new List<DocumentChange>();
        foreach (var task in tasks)
        {
            var before = current;
            current = task.Type switch
            {
                TypographyTaskType.NormalizeHeadings => NormalizeHeadings(current),
                TypographyTaskType.CollapseBlankLines => CollapseBlanks(current, template.Rules.ConsecutiveBlankLineLimit),
                TypographyTaskType.ScaleImages => ScaleImages(current, constraints?.ImageMaxWidth ?? template.Rules.ImageMaxWidth),
                TypographyTaskType.FitTables => FitTables(current),
                TypographyTaskType.RepairPageBreaks => RepairBreaks(current, task),
                TypographyTaskType.GenerateTableOfContents => GenerateToc(current),
                _ => current,
            };
            if (before != current) changes.AddRange(LineDiff.Create(before, current, task.Type, task.Description));
        }
        return new(Restore(current, markdown), changes);
    }

    public static string ApplyAcceptedChanges(string original, IEnumerable<DocumentChange> changes)
    {
        var value = MarkdownAst.Normalize(original);
        foreach (var change in changes)
        {
            if (change.BeforeLineCount > 0 || change.AfterLineCount > 0)
            {
                value = ApplyLineChange(value, change);
                continue;
            }
            var before = MarkdownAst.Normalize(change.Before); var after = MarkdownAst.Normalize(change.After);
            var index = before.Length == 0 ? LineOffset(value, change.Line) : value.IndexOf(before, LineOffset(value, change.Line), StringComparison.Ordinal);
            if (index < 0 && before.Length > 0) index = value.IndexOf(before, StringComparison.Ordinal);
            if (index >= 0) value = value.Remove(index, before.Length).Insert(index, after);
        }
        return Restore(value, original);
    }

    private static string ApplyLineChange(string value, DocumentChange change)
    {
        var lines = value.Split('\n').ToList();
        var beforeLines = DecodeChangedLines(change.Before, change.BeforeLineCount);
        var afterLines = DecodeChangedLines(change.After, change.AfterLineCount);
        var preferred = Math.Clamp(change.Line - 1, 0, lines.Count);
        var index = beforeLines.Length == 0
            ? preferred
            : FindLineSequence(lines, beforeLines, preferred);
        if (index < 0) return value;
        lines.RemoveRange(index, beforeLines.Length);
        lines.InsertRange(index, afterLines);
        return string.Join('\n', lines);
    }

    private static string[] DecodeChangedLines(string value, int lineCount)
    {
        if (lineCount <= 0) return [];
        var lines = MarkdownAst.Normalize(value).Split('\n');
        if (lines.Length == lineCount) return lines;
        return lines.Concat(Enumerable.Repeat(string.Empty, Math.Max(0, lineCount - lines.Length)))
            .Take(lineCount)
            .ToArray();
    }

    private static int FindLineSequence(IReadOnlyList<string> lines, IReadOnlyList<string> expected, int preferred)
    {
        static bool MatchesAt(IReadOnlyList<string> source, IReadOnlyList<string> target, int at)
        {
            if (at < 0 || at + target.Count > source.Count) return false;
            for (var index = 0; index < target.Count; index++)
                if (!string.Equals(source[at + index], target[index], StringComparison.Ordinal)) return false;
            return true;
        }

        for (var index = preferred; index <= lines.Count - expected.Count; index++)
            if (MatchesAt(lines, expected, index)) return index;
        for (var index = 0; index < preferred && index <= lines.Count - expected.Count; index++)
            if (MatchesAt(lines, expected, index)) return index;
        return -1;
    }

    private static string NormalizeHeadings(string value)
    {
        var lines = value.Split('\n'); var previous = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var text = lines[i].TrimStart(); var level = text.TakeWhile(c => c == '#').Count();
            if (level is < 1 or > 6 || text.Length <= level || text[level] != ' ') continue;
            if (previous > 0 && level > previous + 1)
            {
                var indent = lines[i].Length - text.Length;
                lines[i] = lines[i][..indent] + new string('#', previous + 1) + text[level..]; level = previous + 1;
            }
            previous = level;
        }
        return string.Join('\n', lines);
    }
    private static string CollapseBlanks(string value, int limit)
    {
        var output = new List<string>(); var count = 0;
        foreach (var line in value.Split('\n')) { if (string.IsNullOrWhiteSpace(line)) { if (++count > limit) continue; } else count = 0; output.Add(line); }
        return string.Join('\n', output);
    }
    private static string ScaleImages(string value, int width)
    {
        var lines = value.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var m = ImageRegex().Match(lines[i]); if (!m.Success) continue;
            var target = m.Groups["target"].Value.Trim();
            var source = target.StartsWith('<') && target.IndexOf('>') is var angleEnd && angleEnd > 1
                ? target[1..angleEnd]
                : target[..TitleStart(target)];
            lines[i] = $"{m.Groups["indent"].Value}<img src=\"{WebUtility.HtmlEncode(source)}\" alt=\"{WebUtility.HtmlEncode(m.Groups["alt"].Value)}\" style=\"max-width:{width}%;height:auto\" />";
        }
        return string.Join('\n', lines);
    }
    private static string FitTables(string value)
    {
        var lines = value.Split('\n'); var inTable = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var isRow = lines[i].Count(c => c == '|') > 0;
            if (isRow && i + 1 < lines.Length && lines[i + 1].Contains("---", StringComparison.Ordinal)) inTable = true;
            if (!isRow) inTable = false;
            if (!inTable || lines[i].Contains("---", StringComparison.Ordinal)) continue;
            lines[i] = string.Join("|", lines[i].Split('|').Select(cell => InsertWordBreaks(cell, 18)));
        }
        return string.Join('\n', lines);
    }
    private static string InsertWordBreaks(string text, int interval)
    {
        if (text.Length <= interval) return text;
        var b = new StringBuilder(); var run = 0;
        foreach (var c in text) { b.Append(c); if (char.IsWhiteSpace(c)) run = 0; else if (++run == interval) { b.Append("<wbr>"); run = 0; } }
        return b.ToString();
    }
    private static int TitleStart(string target)
    {
        var candidates = new[] { target.IndexOf(" \"", StringComparison.Ordinal), target.IndexOf(" '", StringComparison.Ordinal), target.IndexOf(" (", StringComparison.Ordinal) };
        var found = candidates.Where(x => x > 0).DefaultIfEmpty(target.Length).Min();
        return found;
    }
    private static string RepairBreaks(string value, TypographyTask task)
    {
        var lines = value.Split('\n').ToList();
        foreach (var line in task.DefectIds.Select(x => int.TryParse(x.Split(':').ElementAtOrDefault(1), out var n) ? n : 0).Where(x => x > 1).Distinct().OrderDescending())
            lines.Insert(Math.Clamp(line - 1, 0, lines.Count), "<div class=\"markleaf-page-break\" style=\"break-before:page\"></div>");
        return string.Join('\n', lines);
    }
    private static string GenerateToc(string value)
    {
        if (value.Contains("<!-- markleaf:toc -->", StringComparison.OrdinalIgnoreCase)) return value;
        var headings = MarkdownAst.Parse(value).Where(x => x.Kind == MarkdownBlockKind.Heading).ToArray(); if (headings.Length == 0) return value;
        var b = new StringBuilder("<!-- markleaf:toc -->\n## 目录\n");
        foreach (var h in headings) { var title = h.Text.Trim().TrimStart('#').Trim().TrimEnd('#').Trim(); b.Append(' ', Math.Max(0, h.HeadingLevel - 1) * 2).Append("- [").Append(title).Append("](#").Append(Slug(title)).AppendLine(")"); }
        b.Append("<!-- /markleaf:toc -->"); var first = headings[0]; return value.Insert(first.StartOffset + first.Length, "\n\n" + b);
    }
    private static string Slug(string value) => string.Concat(value.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || c == '-')).Trim().Replace(' ', '-');
    private static int LineOffset(string value, int line) { var at = 0; for (var n = 1; n < line; n++) { var next = value.IndexOf('\n', at); if (next < 0) return value.Length; at = next + 1; } return at; }
    private static string Restore(string value, string original) => original.Contains("\r\n", StringComparison.Ordinal) ? value.Replace("\n", "\r\n", StringComparison.Ordinal) : value;
}

internal static class LineDiff
{
    public static IReadOnlyList<DocumentChange> Create(string before, string after, TypographyTaskType type, string summary)
    {
        var a = before.Split('\n'); var b = after.Split('\n');
        var changes = new List<DocumentChange>(); var i = 0; var j = 0;
        while (i < a.Length || j < b.Length)
        {
            if (i < a.Length && j < b.Length && a[i] == b[j]) { i++; j++; continue; }
            var (skipA, skipB) = FindSync(a, b, i, j);
            if (skipA == 0 && skipB == 0) { skipA = a.Length - i; skipB = b.Length - j; }
            var oldText = string.Join('\n', a.Skip(i).Take(skipA));
            var newText = string.Join('\n', b.Skip(j).Take(skipB));
            changes.Add(new($"{type}:{i + 1}:{Guid.NewGuid():N}", type, summary, i + 1,
                oldText, newText, skipA, skipB));
            i += skipA; j += skipB;
        }
        return changes;
    }

    private static (int SkipA, int SkipB) FindSync(string[] a, string[] b, int fromA, int fromB)
    {
        const int window = 50;
        var bestA = 0; var bestB = 0; var bestCost = int.MaxValue;
        for (var da = 0; da <= window && fromA + da < a.Length; da++)
        for (var db = 0; db <= window && fromB + db < b.Length; db++)
        {
            // Blank lines are not stable synchronization anchors.  Using them here
            // splits a block insertion (notably a generated TOC) into misleading
            // fragments whose preview may show only the closing marker.
            if (da == 0 && db == 0
                || string.IsNullOrWhiteSpace(a[fromA + da])
                || a[fromA + da] != b[fromB + db]) continue;
            var cost = da + db;
            if (cost >= bestCost) continue;
            bestA = da; bestB = db; bestCost = cost;
        }
        return bestCost == int.MaxValue ? (0, 0) : (bestA, bestB);
    }
}

public sealed class TypographyFeedbackEngine
{
    private readonly TypographyEvaluator _evaluator = new(); private readonly TypographyPlanner _planner = new(); private readonly TypographyExecutor _executor = new();
    public FeedbackResult Run(string markdown, TypographyTemplate template, TypographyConstraints? constraints = null,
        IReadOnlyList<DomBlockMeasurement>? measurements = null)
    {
        constraints ??= TypographyConstraints.Empty; var max = Math.Clamp(constraints.MaxIterations ?? template.Rules.MaxIterations, 1, 5);
        var target = constraints.TargetScore ?? template.Rules.TargetScore; var current = markdown;
        var initial = _evaluator.Evaluate(current, template, measurements, constraints); var assessment = initial;
        var iterations = new List<FeedbackIteration>(); var reason = FeedbackStopReason.MaxIterations;
        TypographyAssessment? rejectedAssessment = null;
        for (var i = 1; i <= max; i++)
        {
            // A user explicitly asking for an optimization plan expects outstanding
            // repairable defects to produce changes, even when the initial score is
            // exactly on the template threshold.  The target score stops subsequent
            // feedback rounds; it must not suppress the first repair pass.
            if (assessment.Defects.Count == 0 || i > 1 && assessment.Score >= target)
            {
                reason = FeedbackStopReason.TargetReached;
                break;
            }
            var tasks = _planner.Plan(assessment, constraints); if (tasks.Count == 0) { reason = FeedbackStopReason.NoTasks; break; }
            var execution = _executor.Execute(current, tasks, template, constraints); var after = _evaluator.Evaluate(execution.Markdown, template, null, constraints);
            if (!constraints.AllowNonImprovingChanges && after.Score <= assessment.Score)
            {
                // Keep the last known-good document.  A feedback iteration is an
                // attempted candidate, not an accepted result, until its score is
                // strictly better than the current state.
                rejectedAssessment = after;
                reason = FeedbackStopReason.NoImprovement;
                break;
            }
            iterations.Add(new(i, assessment, after, tasks, execution.Changes, execution.Markdown));
            current = execution.Markdown;
            assessment = after;
        }
        return new(markdown, current, initial, assessment, iterations, reason, rejectedAssessment);
    }
}

public static class NaturalLanguageConstraintParser
{
    public static TypographyConstraints ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
        return new(ReadInt("target_score", 0, 100), ReadInt("max_iterations", 1, 5), ReadInt("max_line_chars", 20, 160),
            ReadInt("image_max_width", 10, 100), ReadBool("generate_toc"));
        int? ReadInt(string name, int min, int max) => root.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? Math.Clamp(n, min, max) : null;
        bool? ReadBool(string name) => root.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;
    }
}
