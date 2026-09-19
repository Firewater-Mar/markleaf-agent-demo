using System.Text.RegularExpressions;

namespace MarkLeaf.Services.AI;

internal enum AgentTaskBehavior
{
    ReadOnly,
    PlanOnly,
    EditPreview,
    CreateFilePreview,
}

internal sealed record AgentSectionEdit(
    string TargetHeading,
    string ReplacementMarkdown,
    string? TargetDocumentPath = null,
    string? OriginalDocumentHash = null);

internal sealed record AgentFileDraft(string TargetPath, string DisplayPath, string Markdown);

internal sealed record AgentEditQualityResult(
    bool Passed,
    string Message,
    double ReusedTextRatio);

internal static partial class AgentDocumentEditService
{
    private static readonly string[] EditSignals =
    [
        "改写", "重写", "修改", "修订", "润色", "优化", "补写", "续写", "替换", "应用到正文",
    ];

    private static readonly string[] PlanSignals =
    [
        "先规划", "只做计划", "给出计划", "制定计划", "分步计划", "实施方案",
    ];

    public static AgentTaskBehavior Classify(string request, string mode)
    {
        if (string.Equals(mode, "plan", StringComparison.OrdinalIgnoreCase))
            return AgentTaskBehavior.PlanOnly;
        if (string.Equals(mode, "readonly", StringComparison.OrdinalIgnoreCase))
            return AgentTaskBehavior.ReadOnly;
        if (Regex.IsMatch(request, "(不要|无需|不需要|禁止|请勿).{0,8}(修改|改写|写入|替换|编辑|更改)"))
            return AgentTaskBehavior.ReadOnly;
        // A file-creation request often continues after the file name, for example:
        // “新建答辩提纲.md，根据当前文档生成完整内容”。  Do not require
        // the extension to be the final token in the sentence.
        if (Regex.IsMatch(request, "新建|创建|生成", RegexOptions.IgnoreCase)
            && !string.IsNullOrWhiteSpace(ExtractRequestedMarkdownPath(request)))
            return AgentTaskBehavior.CreateFilePreview;
        if (EditSignals.Any(signal => request.Contains(signal, StringComparison.OrdinalIgnoreCase)))
            return AgentTaskBehavior.EditPreview;
        if (PlanSignals.Any(signal => request.Contains(signal, StringComparison.OrdinalIgnoreCase)))
            return AgentTaskBehavior.PlanOnly;
        return AgentTaskBehavior.ReadOnly;
    }

    public static bool TryCreateFileDraft(
        string workspaceRoot,
        string request,
        string answer,
        out AgentFileDraft? draft)
    {
        draft = null;
        if (string.IsNullOrWhiteSpace(workspaceRoot)) return false;
        var requestedPath = ExtractRequestedMarkdownPath(request);
        if (string.IsNullOrWhiteSpace(requestedPath)) return false;
        var relative = requestedPath.Trim().Trim('“', '”', '"', '《', '》', ' ')
            .Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(relative)) return false;
        string root;
        string target;
        try
        {
            root = Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            target = Path.GetFullPath(Path.Combine(root, relative));
        }
        catch { return false; }
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return false;
        var markdown = SanitizePortableDraft(ExtractMarkdownFence(answer));
        if (string.IsNullOrWhiteSpace(markdown)) return false;
        draft = new AgentFileDraft(target, relative.Replace('\\', '/'), markdown);
        return true;
    }

    public static string BuildCreateFileGuidance(string request)
    {
        var requestedPath = ExtractRequestedMarkdownPath(request);
        var fileName = Path.GetFileNameWithoutExtension(requestedPath);
        var common = "生成可直接保存和继续编辑的完整 Markdown 文件；只使用项目资料中能够确认的事实。"
            + "缺少原始依据、被标记为冲突或仅由当前文档自述的量化结论，不得当作已证实事实写入。"
            + "正文不得出现临时 [S编号]、核查过程、模型说明或‘待用户确认后再写入’之类的对话文字。";

        if (fileName.Contains("答辩提纲", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("答辩", StringComparison.OrdinalIgnoreCase))
        {
            return common
                + "这是答辩使用的讲述提纲，不是原文目录或项目摘要。"
                + "按‘页面/环节—建议用时—页面内容—讲解重点’组织，建议时长必须标注为可按赛制调整；"
                + "至少覆盖开场与真实问题、目标用户与场景、解决方案、核心工作流、技术实现、创新与价值、现场演示路径、验证现状与局限、后续计划。"
                + "末尾加入可能提问与回答要点，以及答辩前核验清单。"
                + "没有证据的百分比、效率提升幅度和市场结论应省略，不得为了完整而补造。";
        }

        return common + "内容结构应符合目标文件名称所表达的用途，而不是机械复述当前文档目录。";
    }

    private static string SanitizePortableDraft(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;
        var withoutSessionSources = SourceMarkerRegex().Replace(markdown, string.Empty);
        return TrailingWhitespaceRegex().Replace(withoutSessionSources, string.Empty).Trim();
    }

    private static string ExtractRequestedMarkdownPath(string request)
    {
        var quoted = Regex.Match(request, "[“\"《](?<path>[^\"”》\\r\\n]{1,120}\\.(?:md|markdown))[”\"》]", RegexOptions.IgnoreCase);
        if (quoted.Success) return quoted.Groups["path"].Value;

        var located = Regex.Match(
            request,
            "在\\s*(?<dir>[^，。；：\\r\\n]{1,60}?)(?:文件夹|目录)?(?:中|里)\\s*(?:新建|创建|生成)(?:文件)?\\s*(?<file>[^，。；：\\s\\r\\n]{1,80}\\.(?:md|markdown))",
            RegexOptions.IgnoreCase);
        if (located.Success)
        {
            var directory = located.Groups["dir"].Value.Trim().TrimEnd('/', '\\');
            return Path.Combine(directory, located.Groups["file"].Value);
        }

        var direct = Regex.Match(
            request,
            "(?:新建|创建|生成)(?:文件)?\\s*(?<file>[^，。；：\\s\\r\\n]{1,120}\\.(?:md|markdown))",
            RegexOptions.IgnoreCase);
        return direct.Success ? direct.Groups["file"].Value : string.Empty;
    }

    public static bool TryCreateSectionEdit(
        string currentMarkdown,
        string request,
        string answer,
        out AgentSectionEdit? edit)
    {
        edit = null;
        var target = ExtractTarget(request, currentMarkdown);
        var replacement = ExtractMarkdownFence(answer).Trim();
        if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(replacement)) return false;

        var originalHeading = HeadingRegex().Matches(currentMarkdown)
            .Cast<Match>()
            .FirstOrDefault(match => match.Groups[2].Value.Contains(target, StringComparison.OrdinalIgnoreCase));
        if (originalHeading is null) return false;

        var replacementHeading = HeadingRegex().Match(replacement);
        if (!replacementHeading.Success
            || !replacementHeading.Groups[2].Value.Contains(target, StringComparison.OrdinalIgnoreCase))
        {
            replacement = originalHeading.Value.TrimEnd() + "\n\n" + replacement;
        }

        edit = new AgentSectionEdit(originalHeading.Groups[2].Value.Trim(), replacement);
        return true;
    }

    public static bool RequiresMaterialRewrite(string request) =>
        Regex.IsMatch(request, "改写|重写|优化|补写|续写", RegexOptions.IgnoreCase)
        && !Regex.IsMatch(request, "只(?:改|修)(?:错别字|语病|标点)|校对|轻微润色", RegexOptions.IgnoreCase);

    public static AgentEditQualityResult EvaluateSectionEditQuality(
        string currentMarkdown,
        AgentSectionEdit edit)
    {
        if (!TryExtractSection(currentMarkdown, edit.TargetHeading, out var originalSection))
            return new AgentEditQualityResult(false, "没有找到待改写章节，无法比较改写质量。", 1);

        var originalBody = RemoveFirstHeading(originalSection);
        var replacementBody = RemoveFirstHeading(edit.ReplacementMarkdown);
        var original = NormalizeForComparison(originalBody);
        var replacement = NormalizeForComparison(replacementBody);
        if (replacement.Length < 12)
            return new AgentEditQualityResult(false, "新章节内容过短，尚未形成可用的改写。", 1);
        if (string.Equals(original, replacement, StringComparison.Ordinal))
            return new AgentEditQualityResult(false, "预览与原文相同，没有发生实际改写。", 1);

        var meaningfulSentences = SentenceSplitRegex().Split(originalBody)
            .Select(NormalizeForComparison)
            .Where(sentence => sentence.Length >= 10)
            .ToArray();
        var copiedCharacters = meaningfulSentences
            .Where(sentence => replacement.Contains(sentence, StringComparison.Ordinal))
            .Sum(sentence => sentence.Length);
        var sourceCharacters = meaningfulSentences.Sum(sentence => sentence.Length);
        var reusedRatio = sourceCharacters == 0
            ? CalculateNgramContainment(original, replacement)
            : Math.Min(1, copiedCharacters / (double)sourceCharacters);

        if (original.Length >= 40 && reusedRatio >= 0.7)
        {
            return new AgentEditQualityResult(
                false,
                "新稿大段沿用了原文，只做了增补或轻微调整，尚未完成真正的重组改写。",
                reusedRatio);
        }
        if (original.Length >= 80 && replacement.Length < original.Length * 0.5)
        {
            return new AgentEditQualityResult(
                false,
                "新稿删减过多，但用户没有要求压缩或摘要，可能遗漏章节信息。",
                reusedRatio);
        }
        return new AgentEditQualityResult(true, string.Empty, reusedRatio);
    }

    public static bool TryGetTargetSection(
        string currentMarkdown,
        string request,
        out string heading,
        out string section)
    {
        heading = string.Empty;
        section = string.Empty;
        var target = ExtractTarget(request, currentMarkdown);
        if (string.IsNullOrWhiteSpace(target)) return false;
        var match = HeadingRegex().Matches(currentMarkdown)
            .Cast<Match>()
            .FirstOrDefault(candidate => candidate.Groups[2].Value.Contains(target, StringComparison.OrdinalIgnoreCase));
        if (match is null) return false;
        heading = match.Groups[2].Value.Trim();
        return TryExtractSection(currentMarkdown, heading, out section);
    }

    public static string NormalizePreviewFence(string answer)
    {
        var fences = AnyFenceRegex().Matches(answer);
        if (fences.Count != 1) return answer;
        return FenceOpeningRegex().Replace(answer, "```markdown\n", 1);
    }

    private static string ExtractTarget(string request, string markdown)
    {
        var quoted = QuotedTargetRegex().Match(request);
        if (quoted.Success) return quoted.Groups[1].Value.Trim();
        var plain = PlainTargetRegex().Match(request);
        if (plain.Success) return plain.Groups[1].Value.Trim();
        return HeadingRegex().Matches(markdown)
            .Cast<Match>()
            .Select(match => new
            {
                Original = match.Groups[2].Value.Trim(),
                Searchable = NormalizeHeadingForMatch(match.Groups[2].Value),
            })
            .Where(item => item.Searchable.Length >= 2
                && request.Contains(item.Searchable, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.Searchable.Length)
            .Select(item => item.Searchable)
            .FirstOrDefault() ?? string.Empty;
    }

    private static string NormalizeHeadingForMatch(string heading)
    {
        var normalized = HeadingPrefixRegex().Replace(heading.Trim(), string.Empty);
        return normalized.Trim(' ', '\t', '：', ':', '、', '.', '．', '-', '—');
    }

    private static bool TryExtractSection(string markdown, string heading, out string section)
    {
        section = string.Empty;
        var headings = HeadingRegex().Matches(markdown).Cast<Match>().ToArray();
        var index = Array.FindIndex(headings, match =>
            string.Equals(match.Groups[2].Value.Trim(), heading.Trim(), StringComparison.OrdinalIgnoreCase));
        if (index < 0) return false;
        var current = headings[index];
        var level = current.Groups[1].Value.Length;
        var end = markdown.Length;
        for (var nextIndex = index + 1; nextIndex < headings.Length; nextIndex++)
        {
            if (headings[nextIndex].Groups[1].Value.Length <= level)
            {
                end = headings[nextIndex].Index;
                break;
            }
        }
        section = markdown[current.Index..end].Trim();
        return section.Length > 0;
    }

    private static string RemoveFirstHeading(string markdown) =>
        HeadingRegex().Replace(markdown, string.Empty, 1).Trim();

    private static string NormalizeForComparison(string value)
    {
        var withoutSources = SourceMarkerRegex().Replace(value, string.Empty);
        var withoutMarkdown = MarkdownSyntaxRegex().Replace(withoutSources, string.Empty);
        return NonLetterOrDigitRegex().Replace(withoutMarkdown, string.Empty).ToLowerInvariant();
    }

    private static double CalculateNgramContainment(string original, string replacement)
    {
        if (original.Length < 3 || replacement.Length < 3)
            return string.Equals(original, replacement, StringComparison.Ordinal) ? 1 : 0;
        var grams = Enumerable.Range(0, original.Length - 2)
            .Select(index => original.Substring(index, 3))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return grams.Length == 0
            ? 0
            : grams.Count(replacement.Contains) / (double)grams.Length;
    }

    private static string ExtractMarkdownFence(string answer)
    {
        var match = MarkdownFenceRegex().Match(answer);
        if (match.Success) return match.Groups[1].Value;
        // Some compatible models incorrectly label a Markdown preview as css/text.
        // Accept one fenced block only; section validation below still prevents
        // arbitrary prose from becoming an applicable document edit.
        var fallback = AnyFenceRegex().Matches(answer);
        return fallback.Count == 1 ? fallback[0].Groups[1].Value : string.Empty;
    }

    public static bool IsApplyConfirmation(string request) =>
        Regex.IsMatch(request.Trim(), "^(可以|确认|同意|就这样|按这个|应用|写入|替换)(.{0,8})(写入|应用|修改|替换|正文|吧|了)?[。！! ]*$", RegexOptions.IgnoreCase);

    public static bool IsApplyCancellation(string request) =>
        Regex.IsMatch(request.Trim(), "^(取消|不要|放弃|先别|不写入|不应用)(.{0,8})(修改|写入|应用|了)?[。！! ]*$", RegexOptions.IgnoreCase);

    public static bool TryGetExportFormat(string request, out string format)
    {
        format = string.Empty;
        if (!Regex.IsMatch(request, "导出|保存为|另存为", RegexOptions.IgnoreCase)) return false;
        if (Regex.IsMatch(request, "pdf", RegexOptions.IgnoreCase)) format = "pdf";
        else if (Regex.IsMatch(request, "html|网页", RegexOptions.IgnoreCase)) format = "html";
        else if (Regex.IsMatch(request, "docx|word", RegexOptions.IgnoreCase)) format = "docx";
        return format.Length > 0;
    }

    [GeneratedRegex("(?m)^(#{1,6})[ \\t]+(.+?)\\s*$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex("(?:改写|重写|修改|修订|润色|优化|补写|替换)[\\s：:]*[“\\\"《]([^”\\\"》\\r\\n]{2,40})[”\\\"》]")]
    private static partial Regex QuotedTargetRegex();

    [GeneratedRegex("(?:改写|重写|修改|修订|润色|优化|补写|替换)[\\s：:]*([^，。；：\\r\\n]{2,30}?)(?:章节|一节|部分)")]
    private static partial Regex PlainTargetRegex();

    [GeneratedRegex("```(?:markdown|md)?[ \\t]*\\r?\\n([\\s\\S]*?)```", RegexOptions.IgnoreCase)]
    private static partial Regex MarkdownFenceRegex();

    [GeneratedRegex("```[^\\r\\n]*\\r?\\n([\\s\\S]*?)```", RegexOptions.IgnoreCase)]
    private static partial Regex AnyFenceRegex();

    [GeneratedRegex("```[^\\r\\n]*\\r?\\n", RegexOptions.IgnoreCase)]
    private static partial Regex FenceOpeningRegex();

    [GeneratedRegex("\\[S\\d+\\]", RegexOptions.IgnoreCase)]
    private static partial Regex SourceMarkerRegex();

    [GeneratedRegex("[ \\t]+(?=\\r?$)", RegexOptions.Multiline)]
    private static partial Regex TrailingWhitespaceRegex();

    [GeneratedRegex("[`*_>#~|\\-]")]
    private static partial Regex MarkdownSyntaxRegex();

    [GeneratedRegex("[^\\p{L}\\p{N}]+")]
    private static partial Regex NonLetterOrDigitRegex();

    [GeneratedRegex("[。！？!?；;\\r\\n]+")]
    private static partial Regex SentenceSplitRegex();

    [GeneratedRegex("^(?:(?:第[一二三四五六七八九十百\\d]+[章节部分])|(?:[一二三四五六七八九十百]+[、.．])|(?:[（(]?[一二三四五六七八九十百\\d]+[）)]?[、.．:：]?))\\s*")]
    private static partial Regex HeadingPrefixRegex();

}
