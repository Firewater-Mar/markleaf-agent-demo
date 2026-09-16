using System.Text.RegularExpressions;

namespace MarkLeaf.Services.Proof;

internal static partial class DocumentCiService
{
    private static readonly string[] ClaimSignals =
    [
        "研究表明", "数据显示", "调查显示", "结果表明", "证明", "提升", "降低", "达到", "超过",
        "用户", "市场", "成本", "效率", "准确率", "增长", "减少", "主要", "显著",
    ];

    public static ProofCiResult Analyze(string markdown, ProofProject project, string workspaceRoot)
    {
        markdown ??= string.Empty;
        var issues = new List<ProofIssue>();
        var claims = new List<ProofClaimAssessment>();
        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var headings = lines
            .Select((line, index) => (Line: line.Trim(), Number: index + 1))
            .Where(item => HeadingRegex().IsMatch(item.Line))
            .Select(item => (Title: HeadingRegex().Replace(item.Line, string.Empty).Trim(), item.Number))
            .ToArray();

        if (string.IsNullOrWhiteSpace(markdown))
            issues.Add(new ProofIssue(ProofIssueSeverity.Error, "结构", "当前文档为空", "先打开或编写一份 Markdown 文档。"));
        else if (headings.Length == 0)
            issues.Add(new ProofIssue(ProofIssueSeverity.Warning, "结构", "没有发现章节标题", "正式文档建议用 Markdown 标题组织结构。"));
        else
            issues.Add(new ProofIssue(ProofIssueSeverity.Passed, "结构", $"识别到 {headings.Length} 个章节", "章节结构可供要求覆盖检查使用。"));

        var coveredRequirements = 0;
        foreach (var requirement in project.Requirements)
        {
            var keywords = ExtractKeywords(requirement.Title + " " + requirement.Description);
            var best = headings
                .Select(heading => (heading, Score: keywords.Count(keyword => heading.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase))))
                .OrderByDescending(item => item.Score)
                .FirstOrDefault();
            var contentMatch = keywords.Count(keyword => markdown.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            requirement.IsCovered = best.Score > 0 || contentMatch >= Math.Min(2, keywords.Count);
            requirement.MatchedHeading = best.Score > 0 ? best.heading.Title : string.Empty;
            if (requirement.IsCovered)
            {
                coveredRequirements++;
                continue;
            }
            issues.Add(new ProofIssue(
                requirement.Required ? ProofIssueSeverity.Error : ProofIssueSeverity.Warning,
                "要求覆盖",
                $"未覆盖：{requirement.Title}",
                requirement.Points is { } points ? $"该项分值为 {points} 分。" : "没有找到对应章节或关键词。"));
        }
        if (project.Requirements.Count > 0 && coveredRequirements == project.Requirements.Count)
            issues.Add(new ProofIssue(ProofIssueSeverity.Passed, "要求覆盖", "所有任务要求均有对应内容", "仍建议人工核对内容质量。"));
        if (project.Requirements.Count == 0)
            issues.Add(new ProofIssue(ProofIssueSeverity.Info, "要求覆盖", "尚未导入任务要求", "导入评分标准、作业要求或文档模板后可检查覆盖情况。"));

        var claimCount = 0;
        var supportedClaimCount = 0;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (!LooksLikeClaim(line)) continue;
            claimCount++;
            var citations = CitationRegex().Matches(line)
                .Select(match => match.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (citations.Length > 0)
            {
                supportedClaimCount++;
                var state = citations.Length > 1 && ConflictRegex().IsMatch(line)
                    ? ProofEvidenceState.PossibleConflict
                    : ReviewSignalRegex().IsMatch(line)
                        ? ProofEvidenceState.NeedsReview
                        : ProofEvidenceState.Linked;
                claims.Add(new ProofClaimAssessment(index + 1, line, state, citations));
                continue;
            }
            claims.Add(new ProofClaimAssessment(index + 1, line, ProofEvidenceState.Missing, []));
            issues.Add(new ProofIssue(
                ProofIssueSeverity.Warning,
                "证据",
                "重要结论可能缺少来源",
                line.Length > 90 ? line[..90] + "…" : line,
                index + 1));
        }

        var evidenceCount = CitationRegex().Matches(markdown).Select(match => match.Value).Distinct().Count();
        if (claimCount > 0 && supportedClaimCount == claimCount)
            issues.Add(new ProofIssue(ProofIssueSeverity.Passed, "证据", "重要结论均带有来源标记", $"共识别 {evidenceCount} 个不同来源标记。"));
        else if (claimCount == 0)
            issues.Add(new ProofIssue(ProofIssueSeverity.Info, "证据", "没有识别到需要核验的重要结论", "包含数据、市场判断或研究结论后会自动检查来源。"));

        foreach (Match match in LinkRegex().Matches(markdown))
        {
            var target = match.Groups[1].Value.Trim();
            if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || target.StartsWith('#')
                || target.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                continue;
            var cleanTarget = target.Trim('<', '>').Split('#')[0];
            if (string.IsNullOrWhiteSpace(cleanTarget)) continue;
            var absolute = Path.GetFullPath(Path.Combine(workspaceRoot, cleanTarget.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(absolute))
                issues.Add(new ProofIssue(ProofIssueSeverity.Error, "资源", "本地链接或图片失效", target));
        }

        var unresolved = CitationRegex().Matches(markdown)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(id => !SourceIdExists(id, project.Sources))
            .ToArray();
        foreach (var id in unresolved)
            issues.Add(new ProofIssue(ProofIssueSeverity.Warning, "引用", $"来源标记 [{id}] 无法追溯", "重新运行 Agent 或在资料中心补充对应来源。"));

        project.LastRun = new ProofRunSummary
        {
            RequirementCount = project.Requirements.Count,
            CoveredRequirementCount = coveredRequirements,
            EvidenceCount = evidenceCount,
            ErrorCount = issues.Count(issue => issue.Severity == ProofIssueSeverity.Error),
            WarningCount = issues.Count(issue => issue.Severity == ProofIssueSeverity.Warning),
            InfoCount = issues.Count(issue => issue.Severity == ProofIssueSeverity.Info),
        };
        return new ProofCiResult(
            issues,
            claims,
            project.Requirements.Count,
            coveredRequirements,
            evidenceCount,
            claimCount,
            supportedClaimCount);
    }

    private static bool LooksLikeClaim(string line)
    {
        if (line.Length is < 16 or > 500 || line.StartsWith('#') || line.StartsWith("```")) return false;
        return NumberRegex().IsMatch(line) || ClaimSignals.Any(signal => line.Contains(signal, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> ExtractKeywords(string text)
    {
        var terms = Regex.Matches(text, @"[\p{L}\p{N}]{2,}")
            .Select(match => match.Value)
            .Where(value => value is not ("必须" or "需要" or "要求" or "包含" or "说明"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToArray();
        if (terms.Length > 0) return terms;
        var compact = new string(text.Where(character => character is >= '\u3400' and <= '\u9fff').ToArray());
        return Enumerable.Range(0, Math.Max(0, compact.Length - 1))
            .Select(index => compact.Substring(index, 2))
            .Distinct()
            .Take(10)
            .ToArray();
    }

    private static bool SourceIdExists(string citationId, IReadOnlyList<ProofSource> sources)
    {
        if (!citationId.StartsWith('S') || !int.TryParse(citationId.AsSpan(1), out var index)) return true;
        return index > 0 && index <= sources.Count;
    }

    [GeneratedRegex(@"^#{1,6}\s+")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"\[(S\d+)\]|\[\^[^\]]+\]")]
    private static partial Regex CitationRegex();

    [GeneratedRegex(@"!?(?:\[[^\]]*\])\(([^)]+)\)")]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"\d+(?:\.\d+)?\s*(?:%|％|万元|元|人|项|次|倍|小时|分钟|年|月|日)")]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"冲突|不一致|相反|然而|但是|但[一-鿿]")]
    private static partial Regex ConflictRegex();

    [GeneratedRegex(@"可能|预计|初步|推测|有望|大约|约为")]
    private static partial Regex ReviewSignalRegex();
}
