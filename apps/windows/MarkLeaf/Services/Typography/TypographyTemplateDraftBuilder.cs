using System.Text.RegularExpressions;
using System.Text.Json;

namespace MarkLeaf.Services.Typography;

public enum TypographyTemplateDraftSource { Description, MarkdownSample }

public static class TypographyTemplateDraftBuilder
{
    private static readonly Regex NumberAfterLineRegex = new(@"(?:每行|行长|max[_ ]?line[_ ]?chars?)\D{0,12}(\d{2,3})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NumberAfterImageRegex = new(@"(?:图片|image)\D{0,16}(\d{2,3})\s*%", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NumberAfterScoreRegex = new(@"(?:目标分|分数|target[_ ]?score)\D{0,12}(\d{2,3})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ImageWidthInSampleRegex = new(@"max-width\s*:\s*(\d{2,3})%", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BodySizeRegex = new(@"(?:正文|body(?:\s+font)?(?:\s+size)?)\D{0,12}(\d{1,2}(?:\.\d)?)\s*(?:pt|磅|号)?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LineSpacingRegex = new(
        @"(?:(?:行距|line[_ ]?spacing)\s*(?:为|约|[:：])?\s*(?<value>\d(?:\.\d{1,2})?)|(?<value>\d(?:\.\d{1,2})?)\s*(?:倍|x|×)\s*(?:行距|line[_ ]?spacing))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static TypographyTemplate Create(string name, string source, TypographyTemplateDraftSource sourceType)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException("请填写模板名称。");
        if (string.IsNullOrWhiteSpace(source)) throw new InvalidDataException(sourceType == TypographyTemplateDraftSource.Description
            ? "请描述你希望的排版效果。"
            : "请粘贴一段 Markdown 排版样例。");

        var rules = sourceType == TypographyTemplateDraftSource.Description
            ? InferFromDescription(source)
            : InferFromSample(source);
        rules.Normalize();
        return new TypographyTemplate
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name.Trim(),
            Description = sourceType == TypographyTemplateDraftSource.Description
                ? source.Trim()
                : "根据用户提供的 Markdown 样例本地推断。",
            BuiltIn = false,
            Rules = rules,
        };
    }

    public static TypographyTemplate CreateFromAiJson(string name, string description, string json)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException("请填写模板名称。");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.TryGetProperty("rules", out var nested) && nested.ValueKind == JsonValueKind.Object) root = nested;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("AI 返回的规则不是 JSON 对象。");

        var rules = new TypographyRules();
        SetString("body_font_family", value => rules.BodyFontFamily = value);
        SetString("heading_font_family", value => rules.HeadingFontFamily = value);
        SetDecimal("body_font_size_pt", value => rules.BodyFontSizePt = value);
        SetDecimal("heading_1_font_size_pt", value => rules.Heading1FontSizePt = value);
        SetDecimal("heading_2_font_size_pt", value => rules.Heading2FontSizePt = value);
        SetDecimal("heading_3_font_size_pt", value => rules.Heading3FontSizePt = value);
        SetDecimal("line_spacing", value => rules.LineSpacing = value);
        SetDecimal("paragraph_spacing_pt", value => rules.ParagraphSpacingPt = value);
        SetInt("max_line_chars", value => rules.MaxLineChars = value);
        SetInt("image_max_width", value => rules.ImageMaxWidth = value);
        SetInt("table_max_width", value => rules.TableMaxWidth = value);
        SetInt("consecutive_blank_line_limit", value => rules.ConsecutiveBlankLineLimit = value);
        SetInt("short_paragraph_chars", value => rules.ShortParagraphChars = value);
        SetInt("minimum_toc_headings", value => rules.MinimumTocHeadings = value);
        SetInt("target_score", value => rules.TargetScore = value);
        SetInt("max_iterations", value => rules.MaxIterations = value);
        SetBool("allow_orphan", value => rules.AllowOrphan = value);
        SetBool("allow_widow", value => rules.AllowWidow = value);
        SetBool("generate_toc", value => rules.GenerateTableOfContents = value);
        SetBool("keep_headings_with_next", value => rules.KeepHeadingsWithNext = value);
        rules.Normalize();
        return new TypographyTemplate
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name.Trim(),
            Description = description.Trim(),
            BuiltIn = false,
            Rules = rules,
        };

        void SetInt(string property, Action<int> apply)
        {
            if (root.TryGetProperty(property, out var value) && value.TryGetInt32(out var number)) apply(number);
        }
        void SetBool(string property, Action<bool> apply)
        {
            if (root.TryGetProperty(property, out var value)
                && value.ValueKind is JsonValueKind.True or JsonValueKind.False) apply(value.GetBoolean());
        }
        void SetDecimal(string property, Action<decimal> apply)
        {
            if (root.TryGetProperty(property, out var value) && value.TryGetDecimal(out var number)) apply(number);
        }
        void SetString(string property, Action<string> apply)
        {
            if (root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                && value.GetString() is { } text) apply(text);
        }
    }

    private static TypographyRules InferFromDescription(string description)
    {
        var text = description.Trim();
        var rules = new TypographyRules();

        if (ContainsAny(text, "学术", "论文", "报告", "严格", "academic"))
        {
            rules.BodyFontFamily = "宋体";
            rules.HeadingFontFamily = "微软雅黑";
            rules.BodyFontSizePt = 10.5M;
            rules.Heading1FontSizePt = 18M;
            rules.Heading2FontSizePt = 15M;
            rules.Heading3FontSizePt = 12M;
            rules.LineSpacing = 1.6M;
            rules.ParagraphSpacingPt = 6M;
            rules.MaxLineChars = 42;
            rules.ImageMaxWidth = 88;
            rules.TargetScore = 94;
            rules.MaxIterations = 5;
            rules.GenerateTableOfContents = true;
        }
        if (ContainsAny(text, "技术", "代码", "笔记", "technical", "code"))
        {
            rules.BodyFontFamily = "微软雅黑";
            rules.HeadingFontFamily = "微软雅黑";
            rules.Heading1FontSizePt = 20M;
            rules.LineSpacing = 1.5M;
            rules.ParagraphSpacingPt = 8M;
            rules.MaxLineChars = 64;
            rules.ImageMaxWidth = 100;
            rules.TableMaxWidth = 100;
            rules.TargetScore = 88;
            rules.AllowOrphan = true;
        }
        if (ContainsAny(text, "简历", "一页", "紧凑", "resume", "compact"))
        {
            rules.BodyFontFamily = "微软雅黑";
            rules.HeadingFontFamily = "微软雅黑";
            rules.BodyFontSizePt = 9.5M;
            rules.Heading1FontSizePt = 22M;
            rules.Heading2FontSizePt = 13M;
            rules.Heading3FontSizePt = 10.5M;
            rules.LineSpacing = 1.35M;
            rules.ParagraphSpacingPt = 4M;
            rules.MaxLineChars = 48;
            rules.ImageMaxWidth = 40;
            rules.ConsecutiveBlankLineLimit = 0;
            rules.ShortParagraphChars = 12;
            rules.GenerateTableOfContents = false;
        }
        if (ContainsAny(text, "阅读", "打印", "A4", "readable", "print"))
        {
            rules.MaxLineChars = Math.Min(rules.MaxLineChars, 46);
            rules.ImageMaxWidth = Math.Min(rules.ImageMaxWidth, 92);
            rules.KeepHeadingsWithNext = true;
        }

        if (ContainsAny(text, "不要目录", "无目录", "不生成目录", "no toc")) rules.GenerateTableOfContents = false;
        else if (ContainsAny(text, "自动目录", "生成目录", "需要目录", "table of contents", "toc")) rules.GenerateTableOfContents = true;
        if (ContainsAny(text, "允许孤行", "allow orphan")) rules.AllowOrphan = true;
        if (ContainsAny(text, "允许寡行", "allow widow")) rules.AllowWidow = true;

        ApplyNumber(NumberAfterLineRegex, text, value => rules.MaxLineChars = value);
        ApplyNumber(NumberAfterImageRegex, text, value => rules.ImageMaxWidth = value);
        ApplyNumber(NumberAfterScoreRegex, text, value => rules.TargetScore = value);
        ApplyDecimal(BodySizeRegex, text, value => rules.BodyFontSizePt = value);
        ApplyDecimal(LineSpacingRegex, text, value => rules.LineSpacing = value);
        return rules;
    }

    private static TypographyRules InferFromSample(string markdown)
    {
        var blocks = MarkdownAst.Parse(markdown);
        var paragraphLengths = blocks
            .Where(block => block.Kind == MarkdownBlockKind.Paragraph)
            .SelectMany(block => block.Text.Split('\n'))
            .Select(line => line.Trim().Length)
            .Where(length => length > 0)
            .Order()
            .ToArray();
        var headings = blocks.Count(block => block.Kind == MarkdownBlockKind.Heading);
        var blankRuns = blocks.Where(block => block.Kind == MarkdownBlockKind.Blank)
            .Select(block => block.EndLine - block.StartLine + 1)
            .DefaultIfEmpty(1)
            .Max();
        var rules = new TypographyRules
        {
            MaxLineChars = paragraphLengths.Length == 0
                ? 42
                : Math.Clamp(paragraphLengths[(int)Math.Floor((paragraphLengths.Length - 1) * 0.85)], 20, 160),
            ConsecutiveBlankLineLimit = Math.Clamp(blankRuns, 0, 2),
            GenerateTableOfContents = headings >= 4,
            MinimumTocHeadings = Math.Clamp(headings, 2, 8),
            TargetScore = 92,
            MaxIterations = 4,
        };
        var imageWidth = ImageWidthInSampleRegex.Match(markdown);
        if (imageWidth.Success && int.TryParse(imageWidth.Groups[1].Value, out var width)) rules.ImageMaxWidth = width;
        if (blocks.Any(block => block.Kind == MarkdownBlockKind.Code)) rules.MaxLineChars = Math.Max(64, rules.MaxLineChars);
        return rules;
    }

    private static bool ContainsAny(string text, params string[] values) =>
        values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));

    private static void ApplyNumber(Regex regex, string text, Action<int> apply)
    {
        var match = regex.Match(text);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var value)) apply(value);
    }

    private static void ApplyDecimal(Regex regex, string text, Action<decimal> apply)
    {
        var match = regex.Match(text);
        var group = match.Groups["value"].Success ? match.Groups["value"] : match.Groups[1];
        if (match.Success && decimal.TryParse(group.Value,
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var value)) apply(value);
    }
}
