using System.Text;

namespace MarkLeaf.Services.Proof;

internal static class ProofReportBuilder
{
    public static async Task<IReadOnlyList<string>> ExportPackageAsync(
        string exportDirectory,
        ProofProject project,
        ProofCiResult result,
        string documentName,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(exportDirectory);
        var files = new Dictionary<string, string>
        {
            ["文档体检报告.md"] = BuildCiReport(project, result, documentName),
            ["AI使用说明.md"] = BuildAiDisclosure(project, documentName),
            ["答辩提纲.md"] = BuildDefenseOutline(project, result),
            ["调研工具包.md"] = BuildResearchKit(project),
        };
        var paths = new List<string>();
        foreach (var (name, content) in files)
        {
            var path = Path.Combine(exportDirectory, name);
            await File.WriteAllTextAsync(path, content, cancellationToken).ConfigureAwait(false);
            paths.Add(path);
        }
        return paths;
    }

    private static string BuildCiReport(ProofProject project, ProofCiResult result, string documentName)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# MarkLeaf Agent 文档体检报告");
        builder.AppendLine();
        builder.AppendLine($"- 项目：{project.Title}");
        builder.AppendLine($"- 文档：{documentName}");
        builder.AppendLine($"- 检查时间：{DateTime.Now:yyyy-MM-dd HH:mm}");
        builder.AppendLine($"- 要求覆盖：{result.CoveredRequirementCount}/{result.RequirementCount}");
        builder.AppendLine($"- 证据覆盖：{result.SupportedClaimCount}/{result.ClaimCount}");
        builder.AppendLine();
        foreach (var group in result.Issues.GroupBy(issue => issue.Severity))
        {
            builder.AppendLine($"## {SeverityText(group.Key)}");
            builder.AppendLine();
            foreach (var issue in group)
            {
                var line = issue.Line is { } number ? $"（第 {number} 行）" : string.Empty;
                builder.AppendLine($"- **{issue.Title}**{line}：{issue.Detail}");
            }
            builder.AppendLine();
        }
        builder.AppendLine("> 本报告只检查文档结构、要求覆盖和来源可追溯性，不替代作者、导师或评审人员的专业判断。");
        return builder.ToString();
    }

    private static string BuildAiDisclosure(ProofProject project, string documentName)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# AI 使用说明");
        builder.AppendLine();
        builder.AppendLine($"本文档《{documentName}》在写作过程中使用了 MarkLeaf Agent 辅助。AI 仅根据用户主动导入的资料生成建议，所有写入正文的内容均需由用户确认。");
        builder.AppendLine();
        builder.AppendLine("## 已登记资料");
        builder.AppendLine();
        if (project.Sources.Count == 0) builder.AppendLine("- 未登记外部资料。");
        foreach (var source in project.Sources)
            builder.AppendLine($"- {source.DisplayName}（{source.Kind}，可信度：{source.TrustLevel}）");
        builder.AppendLine();
        builder.AppendLine("## 操作记录");
        builder.AppendLine();
        if (project.AuditTrail.Count == 0) builder.AppendLine("- 尚无 AI 写作操作记录。");
        foreach (var item in project.AuditTrail.OrderBy(item => item.AtUtc))
            builder.AppendLine($"- {item.AtUtc.ToLocalTime():yyyy-MM-dd HH:mm}：{item.Action}。{item.Detail}（人工确认：{(item.HumanConfirmed ? "是" : "否")}）");
        builder.AppendLine();
        builder.AppendLine("## 责任声明");
        builder.AppendLine();
        builder.AppendLine("AI 输出可能存在错误。作者已被提示核对数据、引用、知识产权和适用规则，并对最终提交内容负责。");
        return builder.ToString();
    }

    private static string BuildDefenseOutline(ProofProject project, ProofCiResult result)
    {
        var missing = project.Requirements.Where(requirement => !requirement.IsCovered).Select(requirement => requirement.Title).Take(6).ToArray();
        var builder = new StringBuilder();
        builder.AppendLine($"# {project.Title}答辩提纲");
        builder.AppendLine();
        builder.AppendLine("## 开场（30 秒）");
        builder.AppendLine();
        builder.AppendLine("用一句话说明目标用户、核心问题和解决方案。避免从技术名词开始。");
        builder.AppendLine();
        builder.AppendLine("## 问题与证据（60 秒）");
        builder.AppendLine();
        builder.AppendLine("展示真实调研、现状数据或用户反馈，并指出资料来源。");
        builder.AppendLine();
        builder.AppendLine("## 解决方案与演示（120 秒）");
        builder.AppendLine();
        builder.AppendLine("按照“导入要求、导入资料、生成建议、证据核验、文档体检、导出报告”的顺序演示。");
        builder.AppendLine();
        builder.AppendLine("## 创新与应用价值（60 秒）");
        builder.AppendLine();
        builder.AppendLine("重点解释需求覆盖图、结论与证据绑定、人工确认和开放 Markdown 数据格式。");
        builder.AppendLine();
        builder.AppendLine("## 风险与边界（30 秒）");
        builder.AppendLine();
        builder.AppendLine("主动说明 AI 不判断绝对真伪，不生成虚假调研数据，并保留人工最终决定权。");
        builder.AppendLine();
        builder.AppendLine("## 评委可能追问");
        builder.AppendLine();
        builder.AppendLine($"- 当前要求覆盖率为什么是 {result.CoveragePercent}%？");
        builder.AppendLine($"- 当前证据覆盖率为什么是 {result.EvidencePercent}%？");
        builder.AppendLine("- 与普通 Markdown 编辑器或通用 AI 聊天工具相比，核心差异是什么？");
        builder.AppendLine("- 如何证明 AI 没有编造数据？");
        builder.AppendLine("- 本地模型和云端模型分别会发送哪些内容？");
        foreach (var item in missing) builder.AppendLine($"- 文档尚未覆盖“{item}”，准备如何补充？");
        return builder.ToString();
    }

    private static string BuildResearchKit(ProofProject project)
    {
        var focus = project.Requirements.Select(requirement => requirement.Title).Take(5).ToArray();
        var builder = new StringBuilder();
        builder.AppendLine($"# {project.Title}调研工具包");
        builder.AppendLine();
        builder.AppendLine("> 本工具包只生成调研问题和记录结构，不生成或代填调研结果。");
        builder.AppendLine();
        builder.AppendLine("## 用户访谈提纲");
        builder.AppendLine();
        builder.AppendLine("1. 你最近一次完成类似正式文档是什么时候？");
        builder.AppendLine("2. 哪个步骤耗时最长？请描述当时的具体过程。");
        builder.AppendLine("3. 你如何确认文档中的数字和结论可靠？");
        builder.AppendLine("4. 你是否使用过 AI？哪些结果让你不敢直接采用？");
        builder.AppendLine("5. 如果软件能自动检查要求和证据，你最希望它先解决什么？");
        builder.AppendLine();
        builder.AppendLine("## 可用性测试任务");
        builder.AppendLine();
        builder.AppendLine("1. 导入一份任务要求并确认自动提取结果。");
        builder.AppendLine("2. 导入两份资料，为一条结论找到来源。");
        builder.AppendLine("3. 运行文档体检并修复一个高优先级问题。");
        builder.AppendLine("4. 导出 AI 使用说明并判断是否看得懂。");
        builder.AppendLine();
        builder.AppendLine("## 记录表");
        builder.AppendLine();
        builder.AppendLine("| 编号 | 用户类型 | 完成时间 | 卡点 | 严重程度 | 原话 | 改进建议 |");
        builder.AppendLine("| --- | --- | ---: | --- | --- | --- | --- |");
        builder.AppendLine("| 1 |  |  |  |  |  |  |");
        if (focus.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## 需要重点验证的任务要求");
            builder.AppendLine();
            foreach (var item in focus) builder.AppendLine($"- {item}");
        }
        return builder.ToString();
    }

    private static string SeverityText(ProofIssueSeverity severity) => severity switch
    {
        ProofIssueSeverity.Error => "需要处理",
        ProofIssueSeverity.Warning => "建议检查",
        ProofIssueSeverity.Info => "提示",
        ProofIssueSeverity.Passed => "已通过",
        _ => "检查结果",
    };
}
