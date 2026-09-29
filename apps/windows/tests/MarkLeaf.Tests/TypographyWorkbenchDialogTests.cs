using MarkLeaf.Services.Typography;
using MarkLeaf.Services.AI;
using MarkLeaf.Services.Settings;
using MarkLeaf.UI.Controls;
using MarkLeaf.UI.Dialogs;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class TypographyWorkbenchDialogTests
{
    [STATestMethod]
    public void Workbench_layout_keeps_header_content_and_footer_in_separate_rows()
    {
        using var dialog = CreateDialog();
        dialog.Size = new Size(780, 560);
        dialog.CreateControl();
        dialog.PerformLayout();

        var root = dialog.Controls.OfType<TableLayoutPanel>().Single();
        root.PerformLayout();
        var rows = root.Controls.Cast<Control>().OrderBy(control => control.Top).ToArray();

        Assert.HasCount(4, rows);
        for (var index = 0; index < rows.Length - 1; index++)
            Assert.IsLessThanOrEqualTo(rows[index + 1].Top, rows[index].Bottom,
                $"Layout row {index} overlaps row {index + 1}.");
        Assert.IsLessThanOrEqualTo(150, rows[1].Height, "The toolbar should shrink to its content instead of taking a large blank area.");
        Assert.IsLessThanOrEqualTo(root.ClientSize.Height - root.Padding.Bottom, rows[^1].Bottom);

        var apply = Descendants(dialog).OfType<Button>().Single(x => x.Text == "应用已选改动");
        Assert.IsGreaterThan(0, apply.Height);
        Assert.IsLessThanOrEqualTo(dialog.ClientSize.Height, PositionIn(dialog, apply).Bottom);
    }

    [STATestMethod]
    public void Generate_plan_opens_review_with_visible_checked_items_and_count()
    {
        using var dialog = CreateDialog();
        dialog.CreateControl();
        dialog.PerformLayout();

        typeof(TypographyWorkbenchDialog)
            .GetMethod("Optimize", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(dialog, [null]);

        var changes = Descendants(dialog).OfType<ListView>().Single(x => x.CheckBoxes);
        var tabs = Descendants(dialog).OfType<TabControl>().Single();
        var selection = Descendants(dialog).OfType<Label>().Single(x => x.Text.StartsWith("已选择", StringComparison.Ordinal));
        var projectedScore = Descendants(dialog).OfType<Label>().Single(x => x.Text.StartsWith("应用后预计", StringComparison.Ordinal));

        Assert.IsGreaterThan(0, changes.Items.Count);
        Assert.AreEqual(changes.Items.Count, changes.CheckedItems.Count);
        Assert.AreEqual(1, tabs.SelectedIndex);
        StringAssert.Contains(selection.Text, $"{changes.Items.Count} / {changes.Items.Count}");
        StringAssert.Contains(projectedScore.Text, "/100");
    }

    [STATestMethod]
    public void Review_layout_uses_full_width_change_list_and_wrapped_equal_previews()
    {
        using var dialog = CreateDialog();
        dialog.Size = new Size(900, 650);
        dialog.CreateControl();
        dialog.PerformLayout();
        typeof(TypographyWorkbenchDialog)
            .GetMethod("Optimize", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(dialog, [null]);
        dialog.PerformLayout();

        var changes = Descendants(dialog).OfType<ListView>().Single(x => x.CheckBoxes);
        var previews = Descendants(dialog).OfType<TextBox>().Where(x => x.ReadOnly && x.Multiline).ToArray();
        var changesLayout = (TableLayoutPanel)changes.Parent!;
        var reviewLayout = (TableLayoutPanel)changesLayout.Parent!;
        var comparisonLayout = (TableLayoutPanel)previews[0].Parent!.Parent!;

        Assert.AreEqual(1, changesLayout.ColumnCount);
        Assert.AreEqual(1, changesLayout.RowCount);
        Assert.AreEqual(2, comparisonLayout.ColumnCount);
        Assert.IsTrue(comparisonLayout.ColumnStyles.Cast<ColumnStyle>().All(x => x.SizeType == SizeType.Percent && x.Width == 50));
        Assert.IsTrue(previews.All(x => x.WordWrap && x.ScrollBars == ScrollBars.Vertical));
        Assert.IsTrue(previews.All(x => !string.IsNullOrWhiteSpace(x.Text)));
        Assert.AreEqual(55, reviewLayout.RowStyles[1].Height);
        Assert.AreEqual(45, reviewLayout.RowStyles[2].Height);
    }

    [STATestMethod]
    public void Regressive_plan_is_rejected_and_explained_without_showing_projected_lower_score()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), "MarkLeaf.Tests", Guid.NewGuid().ToString("N"));
        var longTitle = new string('长', 180);
        var markdown = $"# {longTitle}一\n\n## {longTitle}二\n\n## {longTitle}三\n\n## {longTitle}四";
        using var dialog = new TypographyWorkbenchDialog(markdown, new TypographyTemplateStore(dataDirectory),
            Array.Empty<DomBlockMeasurement>(), "academic-report");
        dialog.CreateControl();
        dialog.PerformLayout();

        typeof(TypographyWorkbenchDialog)
            .GetMethod("Optimize", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(dialog, [null]);

        var labels = Descendants(dialog).OfType<Label>().Select(x => x.Text).ToArray();
        var changes = Descendants(dialog).OfType<ListView>().Single(x => x.CheckBoxes);
        var tabs = Descendants(dialog).OfType<TabControl>().Single();

        Assert.IsTrue(labels.Any(x => x.Contains("已自动放弃", StringComparison.Ordinal)));
        Assert.IsFalse(labels.Any(x => x.StartsWith("应用后预计", StringComparison.Ordinal)));
        Assert.AreEqual(0, changes.Items.Count);
        Assert.AreEqual(0, tabs.SelectedIndex);
    }

    [STATestMethod]
    public void Workbench_header_does_not_show_removed_offline_subtitle()
    {
        using var dialog = CreateDialog();
        var labels = Descendants(dialog).OfType<Label>().Select(x => x.Text).ToArray();

        Assert.IsFalse(labels.Contains("离线评估 · 只优化排版，不改写正文"));
    }

    [STATestMethod]
    public void Template_library_offers_visual_description_sample_and_optional_json_modes()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), "MarkLeaf.Tests", Guid.NewGuid().ToString("N"));
        using var dialog = new TypographyTemplateLibraryDialog(new TypographyTemplateStore(dataDirectory), "academic-report");
        dialog.CreateControl();
        dialog.PerformLayout();

        var tabs = Descendants(dialog).OfType<TabControl>().Single();
        var tabNames = tabs.TabPages.Cast<TabPage>().Select(x => x.Text).ToArray();

        CollectionAssert.Contains(tabNames, "可视化编辑");
        CollectionAssert.Contains(tabNames, "从描述 / 样例创建");
        CollectionAssert.Contains(tabNames, "高级 JSON");
        Assert.IsTrue(Descendants(dialog).OfType<Button>().Any(x => x.Text == "生成规则草稿"));
        Assert.IsFalse(Descendants(dialog).OfType<Button>().Any(x => x.Text == "AI 设置…"));
        Assert.IsTrue(Descendants(dialog).OfType<Label>()
            .Any(x => x.Text.Contains("主界面的 AI 模式", StringComparison.Ordinal)));
        Assert.IsTrue(Descendants(dialog).OfType<Label>()
            .Any(x => x.Text.Contains("一级标题", StringComparison.Ordinal)
                && x.Text.Contains("pt", StringComparison.OrdinalIgnoreCase)),
            "The standard preview must expose exact heading point sizes instead of only a miniature sketch.");
        Assert.IsGreaterThanOrEqualTo(6, Descendants(dialog).OfType<NumericUpDown>()
            .Count(number => number.DecimalPlaces > 0),
            "Body, heading, line-spacing, and paragraph-spacing values must be editable with decimals.");

        var preview = Descendants(dialog).Single(x => x.AccessibleName == "模板标准样张");
        StringAssert.Contains(preview.AccessibleDescription, "学术报告");
        var templates = Descendants(dialog).OfType<ListBox>().Single();
        templates.SelectedItem = templates.Items.Cast<TypographyTemplate>()
            .Single(item => item.Id == "technical-notes");
        dialog.PerformLayout();
        StringAssert.Contains(preview.AccessibleDescription, "技术笔记");

        dialog.Size = new Size(900, 600);
        dialog.PerformLayout();
        var visualPage = tabs.TabPages.Cast<TabPage>().Single(page => page.Text == "可视化编辑");
        var scrollHost = Descendants(visualPage).OfType<Panel>().Single(panel => panel.AutoScroll);
        var editorStatus = Descendants(visualPage).OfType<Label>()
            .Single(label => label.Text.Contains("内置模板", StringComparison.Ordinal));
        scrollHost.ScrollControlIntoView(editorStatus);
        scrollHost.PerformLayout();
        Assert.IsLessThanOrEqualTo(PositionIn(scrollHost, editorStatus).Bottom, scrollHost.ClientSize.Height,
            "The visual editor status must be reachable without being clipped at the bottom.");
        Assert.IsFalse(scrollHost.HorizontalScroll.Visible,
            "The visual template editor should use vertical scrolling only.");
    }

    [STATestMethod]
    public void Ai_workspace_is_conversational_bounded_and_supports_multiple_providers()
    {
        using var dialog = CreateDialog();
        var buttons = Descendants(dialog).OfType<Button>().Select(x => x.Text).ToArray();

        CollectionAssert.Contains(buttons, "生成优化方案");
        Assert.IsFalse(buttons.Contains("AI 排版要求…"));
        Assert.HasCount(2, Descendants(dialog).OfType<TabPage>());

        var dataDirectory = Path.Combine(Path.GetTempPath(), "MarkLeaf.Tests", Guid.NewGuid().ToString("N"));
        using var panel = new TypographyAiPanel(
            new TypographySettings(), new FakeKeyStore(), new TypographyTemplateStore(dataDirectory),
            _ => Task.FromResult(CreateReviewProposal()),
            (_, _) => Task.CompletedTask,
            () => Task.CompletedTask, _ => { }, () => { });
        panel.Size = new Size(420, 450);
        panel.CreateControl();
        panel.PerformLayout();

        var provider = Descendants(panel).OfType<ComboBox>()
            .Single(combo => combo.Items.Cast<object>().OfType<AiProviderPreset>().Any());
        var providerNames = provider.Items.Cast<AiProviderPreset>().Select(item => item.Name).ToArray();
        CollectionAssert.Contains(providerNames, "OpenAI");
        CollectionAssert.Contains(providerNames, "DeepSeek");
        CollectionAssert.Contains(providerNames, "Kimi / Moonshot");
        CollectionAssert.Contains(providerNames, "通义千问 / 百炼");
        CollectionAssert.Contains(providerNames, "智谱 GLM");
        var modelPicker = Descendants(panel).OfType<ComboBox>()
            .Single(combo => combo.AccessibleName == "生成模型");
        provider.SelectedItem = provider.Items.Cast<AiProviderPreset>()
            .Single(item => item.Id == "deepseek");
        var deepSeekModels = modelPicker.Items.Cast<AiModelPreset>().Select(item => item.Id).ToArray();
        CollectionAssert.Contains(deepSeekModels, "deepseek-flash");
        CollectionAssert.Contains(deepSeekModels, "deepseek-v4-pro");
        Assert.AreEqual("deepseek-flash", ((AiModelPreset)modelPicker.SelectedItem!).Id);
        Assert.IsTrue(Descendants(panel).OfType<Label>()
            .Any(label => label.Text.Contains("速度快", StringComparison.Ordinal)));
        Assert.IsTrue(Descendants(panel).OfType<Button>().Any(x => x.Text == "连接设置"));
        Assert.IsTrue(Descendants(panel).OfType<Button>()
            .Any(x => x.AccessibleName == "发送要求并让 Agent 规划工具任务"));
        Assert.IsTrue(Descendants(panel).OfType<Button>().Any(x => x.Text == "检查排版"));
        Assert.IsTrue(Descendants(panel).OfType<Button>().Any(x => x.Text == "使用帮助"));
        Assert.IsTrue(Descendants(panel).OfType<Label>()
            .Any(x => x.Text == "文档工作流 Agent"));
        Assert.IsTrue(Descendants(panel).OfType<Label>()
            .Any(x => x.Text == "本地工具就绪"));
        Assert.IsTrue(Descendants(panel).OfType<Label>()
            .Any(x => x.Text.Contains("正文改动始终由你确认", StringComparison.Ordinal)));
        Assert.IsTrue(Descendants(panel).OfType<Label>()
            .Any(x => x.Text.Contains("先说明理解和计划", StringComparison.Ordinal)));
        Assert.IsFalse(Descendants(panel).OfType<Panel>()
            .Any(x => x.BorderStyle == BorderStyle.FixedSingle),
            "AI workspace should not fall back to square bordered panels.");
        Assert.IsTrue(Descendants(panel).OfType<TextBox>()
            .Where(x => x.Multiline)
            .All(x => x.BorderStyle == BorderStyle.None),
            "The conversational composer should use the rounded host instead of a square textbox border.");
        Assert.IsTrue(Descendants(panel).OfType<Button>()
            .All(x => x.FlatAppearance.BorderSize == 0),
            "AI workspace actions should use the rounded owner-drawn button treatment.");
        var welcome = Descendants(panel).OfType<Label>()
            .Single(x => x.Text.StartsWith("我是 MarkLeaf 文档工作流 Agent", StringComparison.Ordinal));
        Assert.IsLessThan(220, PositionIn(panel, welcome).Top,
            $"Welcome message should begin near the top of the workflow, actual top={PositionIn(panel, welcome).Top}.");
        var prompt = Descendants(panel).OfType<TextBox>().Single(x => x.AccessibleName == "告诉 Agent 你想完成什么");
        Assert.IsLessThanOrEqualTo(panel.ClientSize.Height, PositionIn(panel, prompt).Bottom,
            "The composer must remain visible at compact desktop heights.");
        var connectionButton = Descendants(panel).OfType<Button>().Single(x => x.Text == "连接设置");
        connectionButton.PerformClick();
        panel.PerformLayout();
        Assert.IsTrue(provider.Visible, "Connection settings should replace the workflow in place.");
        Assert.IsFalse(prompt.Visible, "The composer should yield its space while connection settings are open.");
        Assert.IsLessThanOrEqualTo(panel.ClientSize.Height, PositionIn(panel, provider).Bottom,
            "The first connection fields must remain reachable at compact desktop heights.");
        var showKey = Descendants(panel).OfType<CheckBox>().Single(x => x.Text == "显示" && x.Visible);
        Assert.IsLessThanOrEqualTo(showKey.Parent!.ClientSize.Height, showKey.Bottom,
            "The API key visibility toggle must not be clipped by its row.");
        Assert.HasCount(1, Descendants(panel).OfType<Label>()
            .Where(x => x.Text == "状态：尚未测试"));
        var visibleKeyToggles = Descendants(panel).OfType<CheckBox>().Where(x => x.Text == "显示" && x.Visible).ToArray();
        Assert.HasCount(1, visibleKeyToggles);
        Assert.IsTrue(visibleKeyToggles.All(toggle => toggle.Bottom <= toggle.Parent!.ClientSize.Height),
            "The API key visibility toggle may not be clipped.");
        var connectionHost = Descendants(panel).OfType<Panel>().Single(x => x.AutoScroll && x.Visible);
        Assert.IsFalse(connectionHost.HorizontalScroll.Visible,
            "Connection settings should reflow without a horizontal scrollbar.");

        var testConnection = Descendants(panel).OfType<Button>().Single(x => x.Text == "测试并保存生成模型");
        using var buttonBitmap = new Bitmap(testConnection.Width, testConnection.Height);
        testConnection.DrawToBitmap(buttonBitmap, testConnection.ClientRectangle);
        var corner = buttonBitmap.GetPixel(0, 0);
        Assert.IsTrue(corner.R > 180 && corner.G > 180 && corner.B > 180,
            $"Rounded button corners must be painted from the parent surface, actual color={corner}.");
    }

    [STATestMethod]
    public void Ai_connection_text_fields_accept_standard_paste_shortcuts()
    {
        const string secret = "test-key-from-clipboard";
        using var textBox = new TextBox();
        try
        {
            Clipboard.SetText(secret);

            var handled = TypographyAiPanel.TryHandlePasteShortcut(textBox, Keys.Control | Keys.V);

            Assert.IsTrue(handled);
            Assert.AreEqual(secret, textBox.Text);
        }
        finally
        {
            Clipboard.Clear();
        }
    }

    [STATestMethod]
    public void Ai_connection_fields_keep_descenders_visible_when_font_scales()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), "MarkLeaf.Tests", Guid.NewGuid().ToString("N"));
        using var panel = new TypographyAiPanel(
            new TypographySettings(), new FakeKeyStore(), new TypographyTemplateStore(dataDirectory),
            _ => Task.FromResult(CreateReviewProposal()),
            (_, _) => Task.CompletedTask,
            () => Task.CompletedTask, _ => { }, () => { });
        panel.Size = new Size(420, 620);
        panel.CreateControl();
        Descendants(panel).OfType<Button>().Single(x => x.Text == "连接设置").PerformClick();
        using var largeFont = new Font("Segoe UI", 13F);
        panel.Font = largeFont;
        panel.PerformLayout();

        Control[] fields =
        [
            Descendants(panel).OfType<ComboBox>()
                .Single(combo => combo.Items.Cast<object>().OfType<AiProviderPreset>().Any()),
            Descendants(panel).OfType<ComboBox>().Single(combo => combo.AccessibleName == "生成模型"),
            GetField(panel, "_baseUrl"),
            GetField(panel, "_apiKey"),
        ];
        foreach (var field in fields)
        {
            var host = field.Parent!;
            Assert.IsGreaterThanOrEqualTo(field.PreferredSize.Height + host.Padding.Vertical, host.Height,
                $"The rounded host for {field.AccessibleName ?? field.GetType().Name} must grow with the font.");
            Assert.IsGreaterThanOrEqualTo(field.Bottom + host.Padding.Bottom, host.ClientSize.Height,
                $"The lower edge of {field.AccessibleName ?? field.GetType().Name} must remain visible. " +
                $"Field={field.Bounds}, preferred={field.PreferredSize}, host={host.ClientSize}, " +
                $"minimum={host.MinimumSize}, padding={host.Padding}, font={field.Font.SizeInPoints}pt.");
        }
    }

    [TestMethod]
    public void Ai_model_id_rejects_display_names_with_spaces_and_gives_a_recovery_hint()
    {
        var exception = Assert.ThrowsExactly<InvalidDataException>(
            () => TypographyAiPanel.ValidateModelId("deepseek v4 pro"));

        StringAssert.Contains(exception.Message, "不能包含空格");
        StringAssert.Contains(exception.Message, "deepseek-v4-pro");
    }

    [TestMethod]
    public void Ai_model_id_accepts_and_trims_api_identifier()
    {
        Assert.AreEqual("deepseek-v4-pro", TypographyAiPanel.ValidateModelId("  deepseek-v4-pro  "));
    }

    [TestMethod]
    public void Every_built_in_provider_default_model_is_available_as_a_friendly_option()
    {
        foreach (var provider in AiProviderCatalog.All.Where(item => item.Id != "custom"))
        {
            Assert.IsTrue(provider.Models.Any(model => model.Id == provider.DefaultModel),
                $"{provider.Name} should expose its default model as a selectable option.");
            Assert.IsTrue(provider.Models.All(model => !string.IsNullOrWhiteSpace(model.Description)),
                $"{provider.Name} model options should explain their intended use.");
        }
    }

    [TestMethod]
    public void Workspace_status_distinguishes_generation_model_and_local_execution()
    {
        Assert.AreEqual("本地工具就绪",
            TypographyAiPanel.ComposeWorkspaceStatus("DeepSeek", false));
        Assert.AreEqual("AI 已连接（DeepSeek）· 本地工具就绪",
            TypographyAiPanel.ComposeWorkspaceStatus("DeepSeek", true));
    }

    [STATestMethod]
    public void Agent_review_renders_inline_with_selectable_changes_and_no_popup_action()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), "MarkLeaf.Tests", Guid.NewGuid().ToString("N"));
        using var panel = new TypographyAiPanel(
            new TypographySettings(), new FakeKeyStore(), new TypographyTemplateStore(dataDirectory),
            _ => Task.FromResult(CreateReviewProposal()),
            (_, _) => Task.CompletedTask,
            () => Task.CompletedTask, _ => { }, () => { });
        panel.Size = new Size(420, 620);
        panel.CreateControl();

        typeof(TypographyAiPanel)
            .GetMethod("AddInlineReview", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(panel, [CreateReviewProposal()]);
        panel.PerformLayout();

        Assert.IsTrue(Descendants(panel).OfType<Label>()
            .Any(label => label.Text == "1 项改动等待确认"));
        Assert.IsTrue(Descendants(panel).OfType<Label>()
            .Any(label => label.Text.Contains("本地排版分", StringComparison.Ordinal)
                && label.Text.Contains("不评价正文", StringComparison.Ordinal)));
        Assert.IsTrue(Descendants(panel).OfType<Button>()
            .Any(button => button.Text == "应用 1 项改动"));
        Assert.IsTrue(Descendants(panel).OfType<Button>()
            .Any(button => button.Text == "全部拒绝"));
        Assert.IsFalse(Descendants(panel).OfType<Button>()
            .Any(button => button.Text.Contains("打开", StringComparison.Ordinal)
                || button.Text.Contains("生成并审阅", StringComparison.Ordinal)));
        Assert.HasCount(1, Descendants(panel).OfType<CheckBox>()
            .Where(check => check.AccessibleName?.StartsWith("应用改动：", StringComparison.Ordinal) == true));
        Assert.HasCount(2, Descendants(panel).OfType<TextBox>()
            .Where(box => box.ReadOnly && box.AccessibleName?.Contains("修改", StringComparison.Ordinal) == true));
        Assert.IsTrue(Descendants(panel).OfType<TextBox>()
            .Where(box => box.ReadOnly && box.AccessibleName?.Contains("修改", StringComparison.Ordinal) == true)
            .All(box => box.ScrollBars == ScrollBars.None));
        var conversation = Descendants(panel).OfType<FlowLayoutPanel>()
            .Single(layout => layout.AccessibleName == "Agent 会话记录");
        Assert.IsFalse(conversation.HorizontalScroll.Visible,
            "Inline review must stay inside the AI sidebar without horizontal scrolling.");
        Assert.IsFalse(Descendants(panel).OfType<LinkLabel>()
            .Any(link => link.Text.Contains("展开", StringComparison.Ordinal)));
    }

    [STATestMethod]
    public void Blank_line_review_distinguishes_one_blank_line_from_no_content()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), "MarkLeaf.Tests", Guid.NewGuid().ToString("N"));
        var original = CreateReviewProposal();
        var blankChange = original.Changes[0] with
        {
            Before = string.Empty,
            After = string.Empty,
            BeforeLineCount = 1,
            AfterLineCount = 0,
        };
        var proposal = original with { Changes = [blankChange] };
        using var panel = new TypographyAiPanel(
            new TypographySettings(), new FakeKeyStore(), new TypographyTemplateStore(dataDirectory),
            _ => Task.FromResult(proposal),
            (_, _) => Task.CompletedTask,
            () => Task.CompletedTask, _ => { }, () => { });
        panel.CreateControl();

        typeof(TypographyAiPanel)
            .GetMethod("AddInlineReview", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(panel, [proposal]);

        var previews = Descendants(panel).OfType<TextBox>()
            .Where(box => box.ReadOnly && box.AccessibleName?.Contains("修改", StringComparison.Ordinal) == true)
            .ToArray();
        Assert.HasCount(2, previews);
        Assert.AreEqual("⟦空行⟧", previews.Single(box => box.AccessibleName!.EndsWith("修改前", StringComparison.Ordinal)).Text);
        Assert.AreEqual("⟦无内容⟧", previews.Single(box => box.AccessibleName!.EndsWith("修改后", StringComparison.Ordinal)).Text);
    }

    [STATestMethod]
    public void Applied_agent_review_exposes_one_click_undo_with_the_applied_change_ids()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), "MarkLeaf.Tests", Guid.NewGuid().ToString("N"));
        IReadOnlyCollection<string>? undoneIds = null;
        using var panel = new TypographyAiPanel(
            new TypographySettings(), new FakeKeyStore(), new TypographyTemplateStore(dataDirectory),
            _ => Task.FromResult(CreateReviewProposal()),
            (_, _) => Task.CompletedTask,
            () => Task.CompletedTask, _ => { }, () => { },
            (_, ids) =>
            {
                undoneIds = ids.ToArray();
                return Task.CompletedTask;
            });
        panel.CreateControl();
        var proposal = CreateReviewProposal();
        typeof(TypographyAiPanel)
            .GetMethod("AddInlineReview", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(panel, [proposal]);

        var apply = Descendants(panel).OfType<Button>().Single(button => button.Text == "应用 1 项改动");
        var undo = Descendants(panel).OfType<Button>().Single(button => button.Text == "撤销本次应用");
        Assert.IsFalse(undo.Visible);

        apply.PerformClick();
        Assert.IsTrue(undo.Visible);
        Assert.IsTrue(undo.Enabled);

        undo.PerformClick();
        CollectionAssert.AreEqual(proposal.Changes.Select(change => change.Id).ToArray(), undoneIds!.ToArray());
        Assert.IsFalse(undo.Visible);
        Assert.IsTrue(Descendants(panel).OfType<Label>()
            .Any(label => label.Text.Contains("已撤销本次应用", StringComparison.Ordinal)));
    }

    [STATestMethod]
    public async Task Capability_question_is_answered_locally_with_examples_and_boundaries()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), "MarkLeaf.Tests", Guid.NewGuid().ToString("N"));
        using var panel = new TypographyAiPanel(
            new TypographySettings(), new FakeKeyStore(), new TypographyTemplateStore(dataDirectory),
            _ => Task.FromResult(CreateReviewProposal()),
            (_, _) => Task.CompletedTask,
            () => Task.CompletedTask, _ => { }, () => { });
        panel.CreateControl();
        var prompt = Descendants(panel).OfType<TextBox>()
            .Single(box => box.AccessibleName == "告诉 Agent 你想完成什么");
        prompt.Text = "你能做什么？";

        var task = (Task)typeof(TypographyAiPanel)
            .GetMethod("SendAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(panel, null)!;
        await task;

        var messages = Descendants(panel).OfType<Label>().Select(label => label.Text).ToArray();
        Assert.IsTrue(messages.Any(text => text.Contains("只生成目录", StringComparison.Ordinal)));
        Assert.IsTrue(messages.Any(text => text.Contains("不会生成或改写正文", StringComparison.Ordinal)));
    }

    [STATestMethod]
    public async Task Html_export_command_calls_local_export_without_generating_typography_review()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), "MarkLeaf.Tests", Guid.NewGuid().ToString("N"));
        var optimizationCalls = 0;
        var exportCalls = 0;
        const string exportedPath = @"C:\Exports\report.html";
        using var panel = new TypographyAiPanel(
            new TypographySettings(), new FakeKeyStore(), new TypographyTemplateStore(dataDirectory),
            _ =>
            {
                optimizationCalls++;
                return Task.FromResult(CreateReviewProposal());
            },
            (_, _) => Task.CompletedTask,
            () => Task.CompletedTask, _ => { }, () => { },
            runLocalDocumentTool: intent =>
            {
                Assert.AreEqual(TypographyAgentIntent.ExportHtml, intent);
                exportCalls++;
                return Task.FromResult(AgentLocalToolResult.Success($"HTML 已导出到：\n{exportedPath}"));
            });
        panel.CreateControl();
        var prompt = Descendants(panel).OfType<TextBox>()
            .Single(box => box.AccessibleName == "告诉 Agent 你想完成什么");
        prompt.Text = "导出为 HTML";

        var task = (Task)typeof(TypographyAiPanel)
            .GetMethod("SendAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(panel, null)!;
        await task;

        Assert.AreEqual(1, exportCalls);
        Assert.AreEqual(0, optimizationCalls);
        var messages = Descendants(panel).OfType<Label>().Select(label => label.Text).ToArray();
        Assert.IsTrue(messages.Any(text => text.Contains(exportedPath, StringComparison.Ordinal)));
        Assert.IsFalse(messages.Any(text => text.Contains("本地排版分", StringComparison.Ordinal)));
    }

    [STATestMethod]
    public void Long_diff_preview_expands_fully_without_nested_or_horizontal_scroll()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), "MarkLeaf.Tests", Guid.NewGuid().ToString("N"));
        var original = CreateReviewProposal();
        var longToc = string.Join('\n', Enumerable.Range(1, 30)
            .Select(index => $"- [第 {index} 章 一个很长但必须自动换行的标题](#第-{index}-章-一个很长但必须自动换行的标题)"));
        var longChange = original.Changes[0] with { After = longToc, Summary = "生成目录" };
        var proposal = original with { Changes = [longChange] };
        using var panel = new TypographyAiPanel(
            new TypographySettings(), new FakeKeyStore(), new TypographyTemplateStore(dataDirectory),
            _ => Task.FromResult(proposal),
            (_, _) => Task.CompletedTask,
            () => Task.CompletedTask, _ => { }, () => { });
        panel.Size = new Size(420, 700);
        panel.CreateControl();

        typeof(TypographyAiPanel)
            .GetMethod("AddInlineReview", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(panel, [proposal]);
        panel.PerformLayout();

        var after = Descendants(panel).OfType<TextBox>()
            .Single(box => box.AccessibleName == "生成目录修改后");
        var conversation = Descendants(panel).OfType<FlowLayoutPanel>()
            .Single(layout => layout.AccessibleName == "Agent 会话记录");
        Assert.IsGreaterThan(300, after.Parent!.Height);
        Assert.AreEqual(ScrollBars.None, after.ScrollBars);
        Assert.IsFalse(conversation.HorizontalScroll.Visible);
        var requiredScrollHeight = conversation.Padding.Vertical + 16
            + conversation.Controls.Cast<Control>()
                .Where(control => control.Visible)
                .Sum(control => control.Height + control.Margin.Vertical);
        Assert.IsGreaterThanOrEqualTo(requiredScrollHeight, conversation.AutoScrollMinSize.Height,
            "The vertical scroll range must include the full height of every conversation card.");
        Assert.IsGreaterThan(conversation.ClientSize.Height, conversation.AutoScrollMinSize.Height);
        Assert.IsFalse(Descendants(panel).OfType<LinkLabel>()
            .Any(link => link.Text.Contains("展开", StringComparison.Ordinal)));
    }

    [STATestMethod]
    public void Explicit_tool_review_does_not_present_global_score_as_success_criterion()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), "MarkLeaf.Tests", Guid.NewGuid().ToString("N"));
        using var panel = new TypographyAiPanel(
            new TypographySettings(), new FakeKeyStore(), new TypographyTemplateStore(dataDirectory),
            _ => Task.FromResult(CreateReviewProposal()),
            (_, _) => Task.CompletedTask,
            () => Task.CompletedTask, _ => { }, () => { });
        panel.CreateControl();
        var original = CreateReviewProposal();
        var explicitProposal = original with { IsExplicitToolRequest = true };

        typeof(TypographyAiPanel)
            .GetMethod("AddInlineReview", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(panel, [explicitProposal]);

        var labels = Descendants(panel).OfType<Label>().Select(label => label.Text).ToArray();
        Assert.IsTrue(labels.Any(text => text.Contains("仅执行你指定的工具", StringComparison.Ordinal)));
        Assert.IsFalse(labels.Any(text => text.Contains("评分", StringComparison.Ordinal)));
    }

    private static TextBox GetField(TypographyAiPanel panel, string name) =>
        (TextBox)typeof(TypographyAiPanel)
            .GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(panel)!;

    private sealed class FakeKeyStore : IAiApiKeyStore
    {
        public string? Load(string? providerId = null) => null;
        public void Save(string apiKey, string? providerId = null) { }
        public void Delete(string? providerId = null) { }
    }

    private static TypographyWorkbenchDialog CreateDialog()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), "MarkLeaf.Tests", Guid.NewGuid().ToString("N"));
        const string markdown = "# Title\n\n\n### Skipped heading\n\nBody text stays unchanged.";
        return new TypographyWorkbenchDialog(markdown, new TypographyTemplateStore(dataDirectory),
            Array.Empty<DomBlockMeasurement>(), "academic-report");
    }

    private static TypographyReviewProposal CreateReviewProposal()
    {
        const string original = "# 标题\n\n\n正文";
        const string proposed = "# 标题\n\n正文";
        var defect = new TypographyDefect(
            "blank:3", TypographyDefectType.ExcessBlankLines, DefectSeverity.Warning,
            new SourceLocation(3, 1, 1, 1), "连续空行 2 行，模板最多允许 1 行。");
        var initial = new TypographyAssessment(94, [defect], DateTimeOffset.UtcNow, "academic-report");
        var final = new TypographyAssessment(100, [], DateTimeOffset.UtcNow, "academic-report");
        var task = new TypographyTask(
            "collapse:3", TypographyTaskType.CollapseBlankLines, 100, "清理 1 处空行", [defect.Id]);
        var change = new DocumentChange(
            "change:blank:3", TypographyTaskType.CollapseBlankLines, "清理 1 处多余空行", 3, "\n\n", "\n");
        var iteration = new FeedbackIteration(1, initial, final, [task], [change], proposed);
        var result = new FeedbackResult(
            original, proposed, initial, final, [iteration], FeedbackStopReason.TargetReached);
        return new TypographyReviewProposal(result, [change], 1, "academic-report", "学术报告");
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private static Rectangle PositionIn(Control ancestor, Control child)
    {
        var position = child.Location;
        for (var parent = child.Parent; parent is not null && parent != ancestor; parent = parent.Parent)
            position.Offset(parent.Location);
        return new Rectangle(position, child.Size);
    }

}
