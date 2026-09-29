using System.Text.Json.Serialization;

namespace MarkLeaf.Services.Typography;

public enum TypographyDefectType
{
    OrphanLine, WidowLine, HeadingLevelJump, ExcessBlankLines,
    ImageOverflow, TableOverflow, LongLine, FragmentedParagraph, MissingTableOfContents,
}

public enum DefectSeverity { Info = 1, Warning = 2, Error = 3 }
public enum TypographyTaskType { NormalizeHeadings, CollapseBlankLines, ScaleImages, FitTables, RepairPageBreaks, GenerateTableOfContents }
public enum FeedbackStopReason { TargetReached, NoImprovement, MaxIterations, NoTasks }

public sealed record SourceLocation(int Line, int Column, int Length, int BlockIndex);
public sealed record TypographyDefect(string Id, TypographyDefectType Type, DefectSeverity Severity,
    SourceLocation Location, string Message, IReadOnlyDictionary<string, double>? Measurements = null);
public sealed record TypographyAssessment(int Score, IReadOnlyList<TypographyDefect> Defects,
    DateTimeOffset EvaluatedAt, string TemplateId);
public sealed record DomBlockMeasurement(int BlockIndex, string Tag, double Left, double Top, double Width,
    double Height, double ContentWidth, double PageHeight, double LineHeight);
public sealed record TypographyConstraints(int? TargetScore = null, int? MaxIterations = null,
    int? MaxLineChars = null, int? ImageMaxWidth = null, bool? GenerateTableOfContents = null,
    IReadOnlySet<TypographyTaskType>? ForbiddenTaskTypes = null,
    bool AllowNonImprovingChanges = false)
{
    public static TypographyConstraints Empty { get; } = new();
}

public sealed record TypographyTask(string Id, TypographyTaskType Type, int Priority,
    string Description, IReadOnlyList<string> DefectIds);
public sealed record DocumentChange(string Id, TypographyTaskType Type, string Summary,
    int Line, string Before, string After, int BeforeLineCount = 0, int AfterLineCount = 0);
public sealed record TypographyExecutionResult(string Markdown, IReadOnlyList<DocumentChange> Changes);
public sealed record FeedbackIteration(int Number, TypographyAssessment Before, TypographyAssessment After,
    IReadOnlyList<TypographyTask> Tasks, IReadOnlyList<DocumentChange> Changes, string Markdown);
public sealed record FeedbackResult(string OriginalMarkdown, string ProposedMarkdown,
    TypographyAssessment InitialAssessment, TypographyAssessment FinalAssessment,
    IReadOnlyList<FeedbackIteration> Iterations, FeedbackStopReason StopReason,
    TypographyAssessment? RejectedAssessment = null);
public sealed record TypographyReviewProposal(FeedbackResult Result,
    IReadOnlyList<DocumentChange> Changes, long SourceRevision, string TemplateId, string TemplateName,
    bool IsExplicitToolRequest = false, bool SourceWasDirty = false)
{
    public string ResolveAcceptedMarkdown(IReadOnlyCollection<string> acceptedChangeIds)
    {
        var accepted = Changes.Where(change => acceptedChangeIds.Contains(change.Id)).ToArray();
        return accepted.Length == Changes.Count
            ? Result.ProposedMarkdown
            : TypographyExecutor.ApplyAcceptedChanges(Result.OriginalMarkdown, accepted);
    }

    public string RestoreOriginalMarkdown(
        string currentMarkdown,
        IReadOnlyCollection<string> appliedChangeIds)
    {
        var expected = ResolveAcceptedMarkdown(appliedChangeIds);
        if (!string.Equals(currentMarkdown, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "应用后文档已经发生变化。为避免覆盖你的新编辑，本次改动不能直接撤销。");
        }
        return Result.OriginalMarkdown;
    }
}

public sealed class TypographyTemplate
{
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; } = 1;
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("name")] public string Name { get; set; } = "自定义模板";
    [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
    [JsonPropertyName("built_in")] public bool BuiltIn { get; set; }
    [JsonPropertyName("rules")] public TypographyRules Rules { get; set; } = new();

    public TypographyTemplate CloneAsUserTemplate(string? name = null) => new()
    {
        Id = Guid.NewGuid().ToString("N"), Name = name ?? $"{Name} - 副本", Description = Description,
        BuiltIn = false, Rules = Rules.Clone(),
    };
}

public sealed class TypographyRules
{
    [JsonPropertyName("body_font_family")] public string BodyFontFamily { get; set; } = "宋体";
    [JsonPropertyName("heading_font_family")] public string HeadingFontFamily { get; set; } = "微软雅黑";
    [JsonPropertyName("body_font_size_pt")] public decimal BodyFontSizePt { get; set; } = 10.5M;
    [JsonPropertyName("heading_1_font_size_pt")] public decimal Heading1FontSizePt { get; set; } = 18M;
    [JsonPropertyName("heading_2_font_size_pt")] public decimal Heading2FontSizePt { get; set; } = 15M;
    [JsonPropertyName("heading_3_font_size_pt")] public decimal Heading3FontSizePt { get; set; } = 12M;
    [JsonPropertyName("line_spacing")] public decimal LineSpacing { get; set; } = 1.6M;
    [JsonPropertyName("paragraph_spacing_pt")] public decimal ParagraphSpacingPt { get; set; } = 6M;
    [JsonPropertyName("max_line_chars")] public int MaxLineChars { get; set; } = 42;
    [JsonPropertyName("allow_orphan")] public bool AllowOrphan { get; set; }
    [JsonPropertyName("allow_widow")] public bool AllowWidow { get; set; }
    [JsonPropertyName("image_max_width")] public int ImageMaxWidth { get; set; } = 92;
    [JsonPropertyName("table_max_width")] public int TableMaxWidth { get; set; } = 100;
    [JsonPropertyName("consecutive_blank_line_limit")] public int ConsecutiveBlankLineLimit { get; set; } = 1;
    [JsonPropertyName("short_paragraph_chars")] public int ShortParagraphChars { get; set; } = 18;
    [JsonPropertyName("minimum_toc_headings")] public int MinimumTocHeadings { get; set; } = 4;
    [JsonPropertyName("target_score")] public int TargetScore { get; set; } = 90;
    [JsonPropertyName("max_iterations")] public int MaxIterations { get; set; } = 4;
    [JsonPropertyName("generate_toc")] public bool GenerateTableOfContents { get; set; }
    [JsonPropertyName("keep_headings_with_next")] public bool KeepHeadingsWithNext { get; set; } = true;

    public TypographyRules Clone() => (TypographyRules)MemberwiseClone();
    public void Normalize()
    {
        BodyFontFamily = NormalizeFontFamily(BodyFontFamily, "宋体");
        HeadingFontFamily = NormalizeFontFamily(HeadingFontFamily, "微软雅黑");
        BodyFontSizePt = Math.Clamp(BodyFontSizePt, 8M, 18M);
        Heading1FontSizePt = Math.Clamp(Heading1FontSizePt, 12M, 32M);
        Heading2FontSizePt = Math.Clamp(Heading2FontSizePt, 10M, 28M);
        Heading3FontSizePt = Math.Clamp(Heading3FontSizePt, 9M, 24M);
        LineSpacing = Math.Clamp(LineSpacing, 1M, 2.5M);
        ParagraphSpacingPt = Math.Clamp(ParagraphSpacingPt, 0M, 24M);
        MaxLineChars = Math.Clamp(MaxLineChars, 20, 160);
        ImageMaxWidth = Math.Clamp(ImageMaxWidth, 10, 100);
        TableMaxWidth = Math.Clamp(TableMaxWidth, 40, 100);
        ConsecutiveBlankLineLimit = Math.Clamp(ConsecutiveBlankLineLimit, 0, 5);
        ShortParagraphChars = Math.Clamp(ShortParagraphChars, 1, 100);
        MinimumTocHeadings = Math.Clamp(MinimumTocHeadings, 2, 30);
        TargetScore = Math.Clamp(TargetScore, 50, 100);
        MaxIterations = Math.Clamp(MaxIterations, 1, 5);
    }

    private static string NormalizeFontFamily(string? value, string fallback)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return fallback;
        return normalized.Length <= 80 ? normalized : normalized[..80];
    }
}
