using MarkLeaf.Services.AI;
using MarkLeaf.Services.Typography;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class TypographyAgentPlanTests
{
    [TestMethod]
    public void Parse_builds_bounded_optimization_plan()
    {
        const string json = """
            {
              "intent": "optimize",
              "understanding": "缩小图片并生成目录。",
              "steps": ["评估页面", "缩放图片", "生成目录", "复评并预览"],
              "image_max_width": 78,
              "generate_toc": true,
              "max_iterations": 3
            }
            """;

        var plan = TypographyAgentPlan.Parse(json);

        Assert.AreEqual(TypographyAgentIntent.Optimize, plan.Intent);
        Assert.AreEqual(78, plan.Constraints.ImageMaxWidth);
        Assert.IsTrue(plan.Constraints.GenerateTableOfContents);
        Assert.AreEqual(3, plan.Constraints.MaxIterations);
        Assert.HasCount(4, plan.Steps);
    }

    [TestMethod]
    public void Parse_limits_steps_and_recognizes_template_intent()
    {
        const string json = """
            {
              "intent": "create_template",
              "understanding": "保存课程论文规则。",
              "template_name": "课程论文",
              "steps": ["一", "二", "三", "四", "五"]
            }
            """;

        var plan = TypographyAgentPlan.Parse(json);

        Assert.AreEqual(TypographyAgentIntent.CreateTemplate, plan.Intent);
        Assert.AreEqual("课程论文", plan.TemplateName);
        Assert.HasCount(4, plan.Steps);
    }

    [TestMethod]
    public void Explicit_toc_command_allows_only_toc_tool()
    {
        var scoped = TypographyAiCoordinator.ApplyExplicitToolScope(
            "生成目录",
            new TypographyConstraints());
        var forbidden = scoped.ForbiddenTaskTypes!;

        Assert.IsTrue(scoped.GenerateTableOfContents);
        Assert.IsTrue(scoped.AllowNonImprovingChanges);
        CollectionAssert.DoesNotContain(forbidden.ToList(), TypographyTaskType.GenerateTableOfContents);
        CollectionAssert.Contains(forbidden.ToList(), TypographyTaskType.CollapseBlankLines);
        Assert.HasCount(Enum.GetValues<TypographyTaskType>().Length - 1, forbidden);
    }

    [TestMethod]
    public void Multiple_explicit_commands_allow_only_named_tools()
    {
        var scoped = TypographyAiCoordinator.ApplyExplicitToolScope(
            "请修复标题层级并生成目录，正文不要改",
            new TypographyConstraints());
        var forbidden = scoped.ForbiddenTaskTypes!;

        CollectionAssert.DoesNotContain(forbidden.ToList(), TypographyTaskType.NormalizeHeadings);
        CollectionAssert.DoesNotContain(forbidden.ToList(), TypographyTaskType.GenerateTableOfContents);
        CollectionAssert.Contains(forbidden.ToList(), TypographyTaskType.CollapseBlankLines);
    }

    [TestMethod]
    public void Broad_optimization_request_keeps_planner_scope()
    {
        var original = new TypographyConstraints(TargetScore: 95);

        var scoped = TypographyAiCoordinator.ApplyExplicitToolScope(
            "全面优化 A4 排版并生成目录",
            original);

        Assert.AreSame(original, scoped);
    }

    [TestMethod]
    public void Html_export_is_routed_to_a_local_tool_without_typography_optimization()
    {
        var matched = TypographyAiCoordinator.TryCreateLocalToolPlan("把当前文档导出为 HTML", out var plan);

        Assert.IsTrue(matched);
        Assert.AreEqual(TypographyAgentIntent.ExportHtml, plan.Intent);
        Assert.IsTrue(plan.Steps.Any(step => step.Contains("保存位置", StringComparison.Ordinal)));
        Assert.IsFalse(plan.Steps.Any(step => step.Contains("评分", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Parse_recognizes_html_export_intent_from_ai_plan()
    {
        const string json = """
            {
              "intent": "export_html",
              "understanding": "导出当前文档。",
              "steps": ["读取文档", "保存 HTML"]
            }
            """;

        var plan = TypographyAgentPlan.Parse(json);

        Assert.AreEqual(TypographyAgentIntent.ExportHtml, plan.Intent);
    }

    [TestMethod]
    [DataRow("导入一个 HTML 文件", TypographyAgentIntent.ImportDocument)]
    [DataRow("导入 TXT", TypographyAgentIntent.ImportDocument)]
    [DataRow("导出为 PDF", TypographyAgentIntent.ExportPdf)]
    [DataRow("导出为 PNG 图片", TypographyAgentIntent.ExportImage)]
    [DataRow("另存为 Markdown", TypographyAgentIntent.ExportMarkdown)]
    [DataRow("打印当前文档", TypographyAgentIntent.Print)]
    [DataRow("检查当前文档的排版", TypographyAgentIntent.Assess)]
    [DataRow("只清理空行", TypographyAgentIntent.Optimize)]
    public void Known_commands_use_explicit_whitelisted_intents(
        string instruction,
        TypographyAgentIntent expected)
    {
        var matched = TypographyAiCoordinator.TryCreateLocalToolPlan(instruction, out var plan);

        Assert.IsTrue(matched);
        Assert.AreEqual(expected, plan.Intent);
    }

    [TestMethod]
    public void Unknown_ai_intent_fails_closed_instead_of_defaulting_to_optimize()
    {
        const string json = """
            {
              "intent": "write_article",
              "understanding": "撰写一篇文章。",
              "steps": ["生成正文"]
            }
            """;

        var plan = TypographyAgentPlan.Parse(json);

        Assert.AreEqual(TypographyAgentIntent.Unsupported, plan.Intent);
        Assert.IsFalse(plan.Steps.Any(step => step.Contains("生成正文", StringComparison.Ordinal)));
        StringAssert.Contains(plan.Understanding, "不会执行任何操作");
    }

    [TestMethod]
    public void Model_cannot_authorize_optimization_for_an_unrelated_instruction()
    {
        var modelPlan = new TypographyAgentPlan(
            TypographyAgentIntent.Optimize,
            "优化文档",
            ["评估", "修改"],
            TypographyConstraints.Empty,
            null);

        var guarded = TypographyAiCoordinator.EnforceInstructionScope("帮我写一首诗", modelPlan);

        Assert.AreEqual(TypographyAgentIntent.Unsupported, guarded.Intent);
    }

    [TestMethod]
    public void Explicit_a4_optimization_remains_authorized()
    {
        var modelPlan = new TypographyAgentPlan(
            TypographyAgentIntent.Optimize,
            "优化 A4 排版",
            ["评估", "修改"],
            TypographyConstraints.Empty,
            null);

        var guarded = TypographyAiCoordinator.EnforceInstructionScope(
            "请优化当前文档的 A4 打印排版",
            modelPlan);

        Assert.AreEqual(TypographyAgentIntent.Optimize, guarded.Intent);
    }

    [TestMethod]
    [DataRow("给我生成适合学术汇报的模板", TypographyAgentIntent.CreateTemplate)]
    [DataRow("帮我看看这份文档哪里排得不舒服", TypographyAgentIntent.Assess)]
    [DataRow("让这份作业打印出来更舒服", TypographyAgentIntent.Optimize)]
    [DataRow("我想拿到一个网页版本", TypographyAgentIntent.ExportHtml)]
    [DataRow("把这个文件变成 PDF 给我", TypographyAgentIntent.ExportPdf)]
    public void Ai_whitelist_accepts_natural_paraphrases_without_exact_command_words(
        string instruction,
        TypographyAgentIntent intent)
    {
        var modelPlan = new TypographyAgentPlan(
            intent,
            "AI 已理解自然语言要求。",
            ["调用对应的本地工具"],
            TypographyConstraints.Empty,
            intent == TypographyAgentIntent.CreateTemplate ? "学术汇报" : null);

        var guarded = TypographyAiCoordinator.EnforceInstructionScope(instruction, modelPlan);

        Assert.AreEqual(intent, guarded.Intent);
    }

    [TestMethod]
    [DataRow("帮我写一篇学术报告", TypographyAgentIntent.Optimize)]
    [DataRow("润色一下正文", TypographyAgentIntent.Optimize)]
    [DataRow("写一份摘要并导出 PDF", TypographyAgentIntent.ExportPdf)]
    public void Prose_authoring_stays_blocked_even_when_model_returns_a_whitelisted_intent(
        string instruction,
        TypographyAgentIntent mistakenIntent)
    {
        var modelPlan = new TypographyAgentPlan(
            mistakenIntent,
            "模型错误地选择了一个工具。",
            ["执行"],
            TypographyConstraints.Empty,
            null);

        var guarded = TypographyAiCoordinator.EnforceInstructionScope(instruction, modelPlan);

        Assert.AreEqual(TypographyAgentIntent.Unsupported, guarded.Intent);
    }

    [TestMethod]
    public void Mixed_writing_and_export_request_is_rejected_before_any_local_tool_runs()
    {
        var matched = TypographyAiCoordinator.TryCreateLocalToolPlan(
            "帮我写一份摘要并导出 PDF", out var plan);

        Assert.IsTrue(matched);
        Assert.AreEqual(TypographyAgentIntent.Unsupported, plan.Intent);
    }
}
