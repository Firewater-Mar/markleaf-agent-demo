using MarkLeaf.Services.ImportExport;
using MarkLeaf.Services.Typography;
using MarkLeaf.AI.Contracts;
using MarkLeaf.Services.AI;
using MarkLeaf.Services.Settings;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class TypographyEngineTests
{
    [TestMethod]
    public void Optional_ai_implementation_is_not_loaded_in_pure_mode()
    {
        Assert.IsFalse(TypographyAiModule.IsLoaded);
    }

    [TestMethod]
    public void Evaluator_combines_ast_and_dom_defects()
    {
        const string markdown = "# Title\n\n\n### Jump\n\n![wide](image.png)";
        var template = TypographyTemplateStore.BuiltIns()[0];
        var image = MarkdownAst.Parse(markdown).Single(x => x.Kind == MarkdownBlockKind.Image);
        var result = new TypographyEvaluator().Evaluate(markdown, template,
            [new(image.Index, "img", 0, 0, 720, 400, 700, 1122, 24)]);
        Assert.IsTrue(result.Defects.Any(x => x.Type == TypographyDefectType.HeadingLevelJump));
        Assert.IsTrue(result.Defects.Any(x => x.Type == TypographyDefectType.ExcessBlankLines));
        Assert.IsTrue(result.Defects.Any(x => x.Type == TypographyDefectType.ImageOverflow));
    }

    [TestMethod]
    public void Feedback_repairs_formatting_without_rewriting_prose()
    {
        const string markdown = "# Title\n\n\n### Jump\n\nBody text remains intact.";
        var result = new TypographyFeedbackEngine().Run(markdown, TypographyTemplateStore.BuiltIns()[1]);
        StringAssert.Contains(result.ProposedMarkdown, "## Jump");
        StringAssert.Contains(result.ProposedMarkdown, "Body text remains intact.");
        Assert.IsGreaterThan(result.InitialAssessment.Score, result.FinalAssessment.Score);
    }

    [TestMethod]
    public void Planner_carries_rejected_change_as_constraint()
    {
        const string markdown = "# A\n\n\n### B";
        var assessment = new TypographyEvaluator().Evaluate(markdown, TypographyTemplateStore.BuiltIns()[0]);
        var tasks = new TypographyPlanner().Plan(assessment,
            new(ForbiddenTaskTypes: new HashSet<TypographyTaskType> { TypographyTaskType.NormalizeHeadings }));
        Assert.IsFalse(tasks.Any(x => x.Type == TypographyTaskType.NormalizeHeadings));
        Assert.IsTrue(tasks.Any(x => x.Type == TypographyTaskType.CollapseBlankLines));
    }

    [TestMethod]
    public void Template_round_trips_editable_snake_case_json()
    {
        var item = TypographyTemplateStore.BuiltIns()[0].CloneAsUserTemplate("比赛模板");
        item.Rules.MaxLineChars = 50;
        item.Rules.BodyFontSizePt = 11.5M;
        item.Rules.LineSpacing = 1.75M;
        var json = TypographyTemplateStore.Serialize(item); var restored = TypographyTemplateStore.Deserialize(json);
        StringAssert.Contains(json, "\"max_line_chars\"");
        StringAssert.Contains(json, "\"body_font_size_pt\"");
        StringAssert.Contains(json, "\"line_spacing\"");
        Assert.AreEqual(50, restored.Rules.MaxLineChars);
        Assert.AreEqual(11.5M, restored.Rules.BodyFontSizePt);
        Assert.AreEqual(1.75M, restored.Rules.LineSpacing);
    }

    [TestMethod]
    public void Restricted_html_import_drops_scripts_and_builds_table()
    {
        const string html = "<script>alert(1)</script><h1>Hello</h1><p>World<br>Next</p><table><tr><th>A</th><th>B</th></tr><tr><td>1</td><td>2</td></tr></table>";
        var markdown = SimpleDocumentImportService.ImportHtml(html);
        StringAssert.Contains(markdown, "# Hello"); StringAssert.Contains(markdown, "| A | B |");
        Assert.IsFalse(markdown.Contains("alert", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Llm_contract_is_data_only_and_bounded()
    {
        var value = NaturalLanguageConstraintParser.ParseJson("{\"target_score\":120,\"max_iterations\":99,\"image_max_width\":80}");
        Assert.AreEqual(100, value.TargetScore); Assert.AreEqual(5, value.MaxIterations); Assert.AreEqual(80, value.ImageMaxWidth);
    }

    [TestMethod]
    public void Diff_exposes_separated_edits_for_individual_acceptance()
    {
        const string markdown = "# A\n\n\n### B\n\nMiddle\n\n\n#### C";
        var template = TypographyTemplateStore.BuiltIns()[0];
        var assessment = new TypographyEvaluator().Evaluate(markdown, template);
        var result = new TypographyExecutor().Execute(markdown, new TypographyPlanner().Plan(assessment), template);

        Assert.IsGreaterThanOrEqualTo(2, result.Changes.Count);
        var partial = TypographyExecutor.ApplyAcceptedChanges(markdown, [result.Changes[0]]);
        Assert.AreNotEqual(markdown, partial);
        Assert.AreNotEqual(result.Markdown, partial);
    }

    [TestMethod]
    public void Generated_toc_is_one_complete_preview_change()
    {
        const string markdown = "# 文档标题\n\n## 第一章\n\n正文\n\n## 第二章\n\n正文";
        var template = TypographyTemplateStore.BuiltIns()[0];
        var task = new TypographyTask(
            "toc", TypographyTaskType.GenerateTableOfContents, 10, "生成目录", []);

        var result = new TypographyExecutor().Execute(markdown, [task], template,
            new TypographyConstraints(GenerateTableOfContents: true));

        Assert.HasCount(1, result.Changes);
        StringAssert.Contains(result.Changes[0].After, "<!-- markleaf:toc -->");
        StringAssert.Contains(result.Changes[0].After, "- [第一章](#第一章)");
        StringAssert.Contains(result.Changes[0].After, "- [第二章](#第二章)");
        StringAssert.Contains(result.Changes[0].After, "<!-- /markleaf:toc -->");
    }

    [TestMethod]
    public void Blank_line_change_preserves_line_cardinality_and_applies_individually()
    {
        const string markdown = "# 标题\n\n\n正文";
        var template = TypographyTemplateStore.BuiltIns()[0];
        var task = new TypographyTask(
            "blank", TypographyTaskType.CollapseBlankLines, 10, "清理 1 处多余空行", []);

        var result = new TypographyExecutor().Execute(markdown, [task], template);

        Assert.HasCount(1, result.Changes);
        var change = result.Changes[0];
        Assert.AreEqual(string.Empty, change.Before);
        Assert.AreEqual(string.Empty, change.After);
        Assert.AreEqual(1, change.BeforeLineCount);
        Assert.AreEqual(0, change.AfterLineCount);
        Assert.AreEqual("# 标题\n\n正文", TypographyExecutor.ApplyAcceptedChanges(markdown, [change]));
    }

    [TestMethod]
    public void Review_undo_restores_snapshot_only_when_applied_document_is_unchanged()
    {
        const string original = "# 标题\n\n\n正文";
        var template = TypographyTemplateStore.BuiltIns()[0];
        var result = new TypographyFeedbackEngine().Run(original, template);
        var changes = result.Iterations.SelectMany(iteration => iteration.Changes).ToArray();
        var proposal = new TypographyReviewProposal(
            result, changes, 7, template.Id, template.Name, SourceWasDirty: true);
        var appliedIds = changes.Select(change => change.Id).ToArray();
        var applied = proposal.ResolveAcceptedMarkdown(appliedIds);

        Assert.AreEqual(original, proposal.RestoreOriginalMarkdown(applied, appliedIds));
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            proposal.RestoreOriginalMarkdown(applied + "\n用户的新编辑", appliedIds));
        StringAssert.Contains(exception.Message, "避免覆盖你的新编辑");
    }

    [TestMethod]
    public void Feedback_runs_first_repair_pass_when_score_already_meets_target()
    {
        const string markdown = "# Title\n\n\n### Skipped heading\n\nBody";
        var template = TypographyTemplateStore.BuiltIns()[0].CloneAsUserTemplate();
        var initial = new TypographyEvaluator().Evaluate(markdown, template);
        template.Rules.TargetScore = initial.Score;

        var result = new TypographyFeedbackEngine().Run(markdown, template);

        Assert.IsGreaterThan(0, result.Iterations.SelectMany(x => x.Changes).Count());
        Assert.AreNotEqual(markdown, result.ProposedMarkdown);
    }

    [TestMethod]
    public void Feedback_rejects_candidate_that_would_lower_score()
    {
        var longTitle = new string('长', 180);
        var markdown = $"# {longTitle}一\n\n## {longTitle}二\n\n## {longTitle}三\n\n## {longTitle}四";
        var template = TypographyTemplateStore.BuiltIns()[0].CloneAsUserTemplate();
        template.Rules.MaxLineChars = 20;
        template.Rules.GenerateTableOfContents = true;
        template.Rules.MinimumTocHeadings = 4;
        var initial = new TypographyEvaluator().Evaluate(markdown, template);
        template.Rules.TargetScore = initial.Score;

        var result = new TypographyFeedbackEngine().Run(markdown, template);

        Assert.AreEqual(FeedbackStopReason.NoImprovement, result.StopReason);
        Assert.AreEqual(markdown, result.ProposedMarkdown);
        Assert.AreEqual(initial.Score, result.FinalAssessment.Score);
        Assert.IsEmpty(result.Iterations);
        Assert.IsNotNull(result.RejectedAssessment);
        Assert.IsLessThan(initial.Score, result.RejectedAssessment.Score);
    }

    [TestMethod]
    public void Explicit_tool_command_keeps_requested_change_even_when_global_score_drops()
    {
        var longTitle = new string('长', 180);
        var markdown = $"# {longTitle}一\n\n## {longTitle}二\n\n## {longTitle}三\n\n## {longTitle}四";
        var template = TypographyTemplateStore.BuiltIns()[0].CloneAsUserTemplate();
        template.Rules.MaxLineChars = 20;
        template.Rules.MinimumTocHeadings = 4;
        var constraints = TypographyAiCoordinator.ApplyExplicitToolScope(
            "生成目录", new TypographyConstraints());

        var result = new TypographyFeedbackEngine().Run(markdown, template, constraints);

        Assert.IsNotEmpty(result.Iterations);
        Assert.IsNotEmpty(result.Iterations.SelectMany(iteration => iteration.Changes));
        Assert.IsNull(result.RejectedAssessment);
        Assert.AreNotEqual(markdown, result.ProposedMarkdown);
        Assert.IsTrue(result.Iterations.SelectMany(iteration => iteration.Changes)
            .All(change => change.Type == TypographyTaskType.GenerateTableOfContents));
    }

    [TestMethod]
    public void Template_draft_can_be_created_from_plain_language_description()
    {
        var template = TypographyTemplateDraftBuilder.Create("我的报告",
            "严格的 A4 学术报告，正文 11.5 pt，1.75 倍行距，每行约 45 字，图片不超过 86%，需要自动目录。",
            TypographyTemplateDraftSource.Description);

        Assert.AreEqual("我的报告", template.Name);
        Assert.AreEqual(45, template.Rules.MaxLineChars);
        Assert.AreEqual(86, template.Rules.ImageMaxWidth);
        Assert.AreEqual(11.5M, template.Rules.BodyFontSizePt);
        Assert.AreEqual(1.75M, template.Rules.LineSpacing);
        Assert.IsTrue(template.Rules.GenerateTableOfContents);
        Assert.IsFalse(template.BuiltIn);
    }

    [TestMethod]
    public void Template_draft_can_infer_rules_from_markdown_sample()
    {
        const string sample = "# Sample\n\n## One\n\n## Two\n\n## Three\n\nA sample paragraph with a stable readable line length.\n\n```csharp\nvar x = 1;\n```";
        var template = TypographyTemplateDraftBuilder.Create("样例模板", sample,
            TypographyTemplateDraftSource.MarkdownSample);

        Assert.AreEqual(64, template.Rules.MaxLineChars);
        Assert.IsTrue(template.Rules.GenerateTableOfContents);
        Assert.AreEqual(4, template.Rules.MinimumTocHeadings);
    }

    [TestMethod]
    public void Ai_template_json_is_whitelisted_and_normalized_locally()
    {
        const string json = """
            {"body_font_family":"仿宋","body_font_size_pt":30,"line_spacing":3.8,
             "max_line_chars":999,"image_max_width":5,"target_score":94,"generate_toc":true,
             "unknown_instruction":"rewrite the document"}
            """;

        var template = TypographyTemplateDraftBuilder.CreateFromAiJson("AI 模板", "保持学术感", json);

        Assert.AreEqual(160, template.Rules.MaxLineChars);
        Assert.AreEqual(10, template.Rules.ImageMaxWidth);
        Assert.AreEqual("仿宋", template.Rules.BodyFontFamily);
        Assert.AreEqual(18M, template.Rules.BodyFontSizePt);
        Assert.AreEqual(2.5M, template.Rules.LineSpacing);
        Assert.AreEqual(94, template.Rules.TargetScore);
        Assert.IsTrue(template.Rules.GenerateTableOfContents);
        Assert.AreEqual("保持学术感", template.Description);
    }

    [TestMethod]
    public async Task Ai_coordinator_sends_only_instruction_and_parses_bounded_constraints()
    {
        var client = new FakeAiClient("{\"max_line_chars\":45,\"max_iterations\":3,\"generate_toc\":true}");
        var settings = new TypographySettings
        {
            AiEnabled = true,
            AiBaseUrl = "https://example.test/v1",
            AiModel = "test-model",
        };

        var constraints = await TypographyAiCoordinator.ParseConstraintsAsync(client, settings,
            new FakeKeyStore("secret"), "图片小一点，保留目录");

        Assert.AreEqual(45, constraints.MaxLineChars);
        Assert.AreEqual(3, constraints.MaxIterations);
        Assert.IsTrue(constraints.GenerateTableOfContents);
        Assert.AreEqual("图片小一点，保留目录", client.LastInput);
        Assert.IsFalse(client.LastInput!.Contains("secret", StringComparison.Ordinal));
    }

    private sealed class FakeAiClient(string response) : ITypographyAiClient
    {
        public string? LastInput { get; private set; }
        public Task TestConnectionAsync(AiConnectionOptions options, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string> CompleteJsonAsync(AiConnectionOptions options, string systemPrompt, string userInput,
            CancellationToken cancellationToken = default)
        {
            LastInput = userInput;
            return Task.FromResult(response);
        }
    }

    private sealed class FakeKeyStore(string key) : IAiApiKeyStore
    {
        public string? Load(string? providerId = null) => key;
        public void Save(string apiKey, string? providerId = null) { }
        public void Delete(string? providerId = null) { }
    }
}
