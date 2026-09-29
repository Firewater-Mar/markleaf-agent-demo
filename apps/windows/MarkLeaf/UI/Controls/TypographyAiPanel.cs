using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using MarkLeaf.AI.Contracts;
using MarkLeaf.Services.AI;
using MarkLeaf.Services.Settings;
using MarkLeaf.Services.Typography;

namespace MarkLeaf.UI.Controls;

/// <summary>
/// A bounded conversational Agent workspace for Markdown, A4 typography, and print tasks.
/// Showing this control never loads the optional AI implementation or accesses the network.
/// </summary>
internal sealed class TypographyAiPanel : UserControl
{
    private static readonly Color Canvas = Color.FromArgb(250, 249, 246);
    private static readonly Color Surface = Color.White;
    private static readonly Color Border = Color.FromArgb(220, 225, 221);
    private static readonly Color TextPrimary = Color.FromArgb(27, 38, 31);
    private static readonly Color TextMuted = Color.FromArgb(88, 101, 92);
    private static readonly Color Accent = Color.FromArgb(24, 110, 76);
    private static readonly Color AccentSoft = Color.FromArgb(230, 243, 236);
    private static readonly Color UserSurface = Color.FromArgb(235, 244, 255);
    private static readonly Color Error = Color.FromArgb(170, 48, 48);

    private readonly TypographySettings _settings;
    private readonly IAiApiKeyStore _keyStore;
    private readonly TypographyTemplateStore _templateStore;
    private readonly Func<TypographyConstraints, Task<TypographyReviewProposal>> _prepareOptimization;
    private readonly Func<TypographyReviewProposal, IReadOnlyCollection<string>, Task> _applyOptimization;
    private readonly Func<TypographyReviewProposal, IReadOnlyCollection<string>, Task> _undoOptimization;
    private readonly Func<Task> _openLocalAssessment;
    private readonly Func<TypographyAgentIntent, Task<AgentLocalToolResult>> _runLocalDocumentTool;
    private readonly Action<TypographyTemplate> _templateCreated;
    private readonly Action _saveSettings;

    private readonly BufferedFlowLayoutPanel _conversation = new()
    {
        Dock = DockStyle.Fill,
        AutoScroll = true,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        BackColor = Surface,
        Padding = new Padding(0, 8, 4, 6),
        Margin = Padding.Empty,
        TabStop = true,
        AccessibleName = "Agent 会话记录",
    };
    private readonly TextBox _prompt = new()
    {
        Dock = DockStyle.Fill,
        Multiline = false,
        AcceptsReturn = false,
        ScrollBars = ScrollBars.None,
        BorderStyle = BorderStyle.None,
        BackColor = Surface,
        Font = new Font("Segoe UI", 10F),
        Margin = new Padding(0, 10, 0, 0),
        AccessibleName = "告诉 Agent 你想完成什么",
    };
    private readonly ComboBox _provider = DropDown();
    private readonly ComboBox _model = ModelDropDown();
    private readonly Label _modelHint = BodyLabel("选择推荐模型，也可以直接输入服务商提供的模型 ID。");
    private readonly TextBox _baseUrl = TextField();
    private readonly TextBox _apiKey = new()
    {
        Dock = DockStyle.Fill,
        UseSystemPasswordChar = true,
        BorderStyle = BorderStyle.None,
        BackColor = Surface,
    };
    private readonly CheckBox _showKey = new()
    {
        Text = "显示",
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(10, 8, 0, 0),
    };
    private readonly Label _connectionSummary = BodyLabel(string.Empty);
    private readonly Label _workspaceStatus = new()
    {
        Text = "本地工具就绪",
        AutoSize = true,
        ForeColor = TextMuted,
        Margin = Padding.Empty,
        AccessibleName = "工作区状态：本地工具就绪",
        AccessibleRole = AccessibleRole.StaticText,
    };
    private readonly Label _generationTestStatus = ConnectionStatusLabel("状态：尚未测试");
    private readonly RoundedPanel _connectionHost = new()
    {
        Dock = DockStyle.Fill,
        AutoScroll = true,
        BackColor = Surface,
        BorderColor = Color.Transparent,
        BorderWidth = 0,
        Radius = 0,
        Padding = Padding.Empty,
        Visible = false,
        Margin = new Padding(0, 10, 0, 0),
    };
    private readonly List<Control> _messageCards = [];
    private readonly List<(string Role, string Text)> _agentContext = [];
    private readonly HashSet<TypographyTaskType> _rejectedTaskTypes = [];
    private readonly Button _send;
    private readonly Button _test;
    private readonly Button _connectionToggle;
    private readonly Button _localCheck;
    private readonly Control _suggestionsHost;
    private readonly Control _composerHost;
    private bool _loading;
    private bool _connectionExpanded;
    private bool _conversationStarted;
    private bool _refreshingConversationExtent;
    private bool _conversationExtentRefreshPending;

    public TypographyAiPanel(
        TypographySettings settings,
        IAiApiKeyStore keyStore,
        TypographyTemplateStore templateStore,
        Func<TypographyConstraints, Task<TypographyReviewProposal>> prepareOptimization,
        Func<TypographyReviewProposal, IReadOnlyCollection<string>, Task> applyOptimization,
        Func<Task> openLocalAssessment,
        Action<TypographyTemplate> templateCreated,
        Action saveSettings,
        Func<TypographyReviewProposal, IReadOnlyCollection<string>, Task>? undoOptimization = null,
        Func<TypographyAgentIntent, Task<AgentLocalToolResult>>? runLocalDocumentTool = null)
    {
        _settings = settings;
        _keyStore = keyStore;
        _templateStore = templateStore;
        _prepareOptimization = prepareOptimization;
        _applyOptimization = applyOptimization;
        _undoOptimization = undoOptimization ?? ((_, _) => Task.FromException(
            new InvalidOperationException("当前环境不支持撤销 Agent 改动。")));
        _openLocalAssessment = openLocalAssessment;
        _runLocalDocumentTool = runLocalDocumentTool ?? (intent =>
            Task.FromException<AgentLocalToolResult>(
                new InvalidOperationException($"当前环境不支持工具“{intent}”。")));
        _templateCreated = templateCreated;
        _saveSettings = saveSettings;

        Dock = DockStyle.Fill;
        BackColor = Surface;
        ForeColor = TextPrimary;
        Font = new Font("Segoe UI", 9F);
        Padding = new Padding(22, 18, 20, 16);
        AccessibleName = "MarkLeaf 文档工作流 Agent";
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

        _send = SendButton();
        _send.AccessibleName = "发送要求并让 Agent 规划工具任务";
        _test = SecondaryButton("测试并保存生成模型");
        _connectionToggle = QuietButton("连接设置");
        _localCheck = QuietButton("检查排版");

        _send.Click += async (_, _) => await SendAsync();
        _localCheck.Click += async (_, _) => await RunLocalAssessmentAsync();
        _test.Click += async (_, _) => await TestConnectionAsync();
        _connectionToggle.Click += (_, _) => SetConnectionExpanded(!_connectionExpanded);
        _showKey.CheckedChanged += (_, _) => _apiKey.UseSystemPasswordChar = !_showKey.Checked;
        _provider.SelectedIndexChanged += (_, _) => ProviderChanged();
        _model.SelectedIndexChanged += (_, _) => RefreshModelHint();
        _model.TextChanged += (_, _) => RefreshModelHint();
        _prompt.KeyDown += OnPromptKeyDown;

        _suggestionsHost = BuildSuggestions();
        _composerHost = BuildComposer();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = Padding.Empty,
            BackColor = Surface,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(_conversation, 0, 1);
        root.Controls.Add(_connectionHost, 0, 1);
        root.Controls.Add(_suggestionsHost, 0, 2);
        root.Controls.Add(_composerHost, 0, 3);
        Controls.Add(root);

        _provider.DisplayMember = nameof(AiProviderPreset.Name);
        _provider.Items.AddRange(AiProviderCatalog.All.Cast<object>().ToArray());
        _model.DisplayMember = nameof(AiModelPreset.DisplayText);
        _connectionHost.Controls.Add(BuildConnectionSettings());
        LoadSettings();

        _conversation.SizeChanged += (_, _) => ResizeMessageCards();
        SizeChanged += (_, _) => ResizeMessageCards();
        AddWelcomeMessage();
    }

    public void RefreshContext()
    {
        RefreshConnectionSummary();
        if (_conversationStarted)
            AddActivity("已切换到当前文档。你可以继续提出排版、模板或打印任务。");
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        // MainForm also owns editor commands such as Paste.  When focus is inside
        // this native settings surface, consume paste here so the editor command
        // router cannot swallow Ctrl+V before the TextBox receives it.
        var focused = FindFocusedControl(this);
        if (TryHandlePasteShortcut(focused, keyData)) return true;
        return base.ProcessCmdKey(ref message, keyData);
    }

    internal static bool TryHandlePasteShortcut(Control? focused, Keys keyData)
    {
        if (focused is TextBoxBase textBox && !textBox.ReadOnly
            && keyData is (Keys.Control | Keys.V) or (Keys.Shift | Keys.Insert))
        {
            try
            {
                textBox.Paste();
                return true;
            }
            catch (ExternalException)
            {
                // The clipboard can be temporarily locked by another process.
                // Let normal WinForms processing retry instead of losing the key.
            }
        }
        return false;
    }

    private static Control? FindFocusedControl(Control root)
    {
        var current = root;
        while (current is ContainerControl container && container.ActiveControl is { } active)
            current = active;
        if (current != root && current.Focused) return current;
        return Descendants(root).FirstOrDefault(control => control.Focused);
    }

    private Control BuildHeader()
    {
        var headingLine = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Margin = Padding.Empty,
            BackColor = Surface,
        };
        headingLine.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headingLine.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        headingLine.Controls.Add(new Label
        {
            Text = "文档工作流 Agent",
            AutoSize = true,
            Font = new Font("Segoe UI", 15F, FontStyle.Bold),
            ForeColor = TextPrimary,
            Margin = Padding.Empty,
        }, 0, 0);

        var ready = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(12, 5, 0, 0),
            BackColor = Surface,
        };
        ready.Controls.Add(new StatusDot { Margin = new Padding(0, 4, 6, 0) });
        ready.Controls.Add(_workspaceStatus);
        headingLine.Controls.Add(_connectionToggle, 1, 0);

        var scope = new Label
        {
            Text = "只处理 Markdown、排版、模板与打印；正文改动始终由你确认",
            AutoSize = true,
            ForeColor = TextMuted,
            Font = new Font("Segoe UI", 8.5F),
            Margin = new Padding(0, 5, 0, 0),
        };

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 4,
            Margin = new Padding(0, 0, 0, 8),
            BackColor = Surface,
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.Controls.Add(headingLine, 0, 0);
        header.Controls.Add(ready, 0, 1);
        header.Controls.Add(scope, 0, 2);
        header.Controls.Add(new Panel
        {
            Dock = DockStyle.Top,
            Height = 1,
            BackColor = Border,
            Margin = new Padding(0, 12, 0, 0),
        }, 0, 3);
        return header;
    }

    private Control BuildSuggestions()
    {
        var row = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0, 8, 0, 7),
        };
        _localCheck.Padding = new Padding(4, 3, 8, 3);
        row.Controls.Add(_localCheck);
        AddSuggestion("优化 A4", "在不改写正文的前提下优化当前文档的 A4 打印排版，并让我逐项确认改动。", row);
        AddSuggestion("生成目录", "检查标题结构，并在适合时生成目录，其他正文不要修改。", row);
        AddSuggestion("使用帮助", "你能做什么？", row);
        return row;
    }

    private void AddSuggestion(string text, string prompt, Control host)
    {
        var button = QuietButton(text);
        button.MinimumSize = new Size(0, 32);
        button.Padding = new Padding(8, 3, 8, 3);
        button.Click += (_, _) =>
        {
            _prompt.Text = prompt;
            _prompt.Focus();
            _prompt.SelectionStart = _prompt.TextLength;
        };
        host.Controls.Add(button);
    }

    private Control BuildComposer()
    {
        var composerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(14, 10, 9, 9),
            Margin = Padding.Empty,
            BackColor = Surface,
        };
        composerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        composerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        composerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        composerLayout.Controls.Add(_prompt, 0, 0);
        _send.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
        composerLayout.Controls.Add(_send, 1, 0);

        var composer = new RoundedPanel
        {
            Dock = DockStyle.Top,
            Height = 72,
            Radius = 14,
            BorderColor = Border,
            BorderWidth = 1,
            BackColor = Surface,
            Margin = new Padding(0, 0, 0, 8),
        };
        composer.Controls.Add(composerLayout);
        _prompt.HandleCreated += (_, _) => NativeCueBanner.Set(_prompt, "继续描述要求…");
        return composer;
    }

    private Control BuildConnectionSection()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Padding = new Padding(10, 8, 10, 8),
            Margin = Padding.Empty,
            BackColor = Surface,
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _connectionSummary.Margin = new Padding(0, 5, 8, 0);
        header.Controls.Add(_connectionSummary, 0, 0);
        header.Controls.Add(_localCheck, 1, 0);

        var divider = new Panel
        {
            Dock = DockStyle.Top,
            Height = 1,
            BackColor = Border,
            Margin = new Padding(0, 0, 0, 4),
        };
        var section = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0, 0, 0, 6),
            BackColor = Canvas,
        };
        section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        section.Controls.Add(divider, 0, 0);
        section.Controls.Add(header, 0, 1);
        section.Controls.Add(_connectionHost, 0, 2);
        return section;
    }

    private Control BuildConnectionSettings()
    {
        var title = new Label
        {
            Text = "连接 AI 服务",
            AutoSize = true,
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            ForeColor = TextPrimary,
            Margin = new Padding(0, 0, 0, 6),
        };
        var explanation = BodyLabel(
            "生成模型只负责理解复杂要求和模板描述；任务路由、评估、修改、复评和 Diff 均由 MarkLeaf 本地工具完成。");
        explanation.Margin = new Padding(0, 0, 0, 10);

        var form = OneColumnLayout(15);
        form.Dock = DockStyle.Top;
        form.Padding = new Padding(4, 4, 12, 14);
        form.BackColor = Surface;
        form.Controls.Add(title, 0, 0);
        form.Controls.Add(explanation, 0, 1);
        form.Controls.Add(SectionLabel("生成模型 · 复杂理解与模板生成"), 0, 2);
        form.Controls.Add(FieldLabel("服务商"), 0, 3);
        form.Controls.Add(WrapField(_provider), 0, 4);
        form.Controls.Add(FieldLabel("模型"), 0, 5);
        form.Controls.Add(WrapField(_model), 0, 6);
        _modelHint.Margin = new Padding(2, 0, 0, 5);
        form.Controls.Add(_modelHint, 0, 7);
        form.Controls.Add(FieldLabel("接口地址"), 0, 8);
        form.Controls.Add(WrapField(_baseUrl), 0, 9);
        form.Controls.Add(FieldLabel("API Key"), 0, 10);
        form.Controls.Add(BuildKeyRow(_apiKey, _showKey), 0, 11);
        form.Controls.Add(BuildActionRow(_test), 0, 12);
        form.Controls.Add(_generationTestStatus, 0, 13);

        var privacy = BuildPrivacyNote();
        privacy.Margin = new Padding(0, 10, 0, 0);
        form.Controls.Add(privacy, 0, 14);
        form.SizeChanged += (_, _) =>
        {
            var textWidth = Math.Max(220, form.ClientSize.Width - form.Padding.Horizontal - 8);
            explanation.MaximumSize = new Size(textWidth, 0);
            _modelHint.MaximumSize = new Size(textWidth, 0);
            _generationTestStatus.MaximumSize = new Size(textWidth, 0);
            privacy.MaximumSize = new Size(textWidth, 0);
            _connectionHost.AutoScrollMinSize = new Size(0, form.PreferredSize.Height + 20);
        };
        return form;
    }

    private static Control BuildKeyRow(TextBox keyField, CheckBox showKey)
    {
        var keyRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            BackColor = Surface,
        };
        keyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        keyRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        keyRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        keyRow.Controls.Add(WrapField(keyField), 0, 0);
        keyRow.Controls.Add(showKey, 1, 0);
        return keyRow;
    }

    private static Label SectionLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
        ForeColor = Accent,
        Margin = new Padding(0, 14, 0, 4),
    };

    private static Label BuildPrivacyNote() => new()
    {
        Text = "隐私：只发送本次会话中的要求与工具结果摘要，不上传当前文档正文。API Key 保存在 Windows 凭据管理器中。",
        AutoSize = true,
        ForeColor = TextMuted,
        Margin = new Padding(2, 0, 2, 0),
    };

    private void AddWelcomeMessage()
    {
        AddAgentMessage(
            "我是 MarkLeaf 文档工作流 Agent。你可以直接说“检查这篇报告”“让图片适合 A4”或“做一套课程论文模板”。\n\n" +
            "我会先说明理解和计划。你不需要记固定命令：AI 会理解自然表达，再映射到受控的本地工具；任何文档改动都要经过你的差异审阅。",
            "Agent");
    }

    private static bool IsCapabilitiesQuestion(string instruction)
    {
        var normalized = instruction.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);
        string[] phrases =
        [
            "你能做什么", "可以做什么", "能干什么", "支持什么", "有哪些功能",
            "怎么使用", "怎么用", "使用帮助", "帮助",
        ];
        return phrases.Any(phrase => normalized.Contains(phrase, StringComparison.OrdinalIgnoreCase));
    }

    private void AddCapabilitiesMessage()
    {
        AddAgentMessage(
            "你可以直接这样说：\n" +
            "• “检查当前文档的排版问题”\n" +
            "• “在不改正文的情况下优化 A4 打印排版”\n" +
            "• “只生成目录”或“修复标题层级”\n" +
            "• “清理多余空行”“缩小溢出的图片”“让表格适合 A4”\n" +
            "• “处理孤行寡行和分页”\n" +
            "• “导入 HTML/TXT”“导出 HTML/PDF/图片/Markdown”\n" +
            "• “打印当前文档”\n" +
            "• “按这段描述创建一套课程论文排版模板”\n\n" +
            "我也能解释本地检查发现的问题。所有正文改动都会先给你看 Diff；我不会生成或改写正文内容。",
            "Agent 能力");
    }

    private void OnPromptKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.KeyCode != Keys.Enter || eventArgs.Shift) return;
        eventArgs.SuppressKeyPress = true;
        _ = SendAsync();
    }

    private async Task SendAsync()
    {
        var instruction = _prompt.Text.Trim();
        if (string.IsNullOrWhiteSpace(instruction))
        {
            AddAgentMessage("请先说出你希望检查、优化或保存为什么模板。", "需要你的说明", error: true);
            _prompt.Focus();
            return;
        }

        AddUserMessage(instruction);
        _prompt.Clear();
        _conversationStarted = true;
        if (IsCapabilitiesQuestion(instruction))
        {
            AddCapabilitiesMessage();
            Remember("用户", instruction);
            Remember("Agent", "已说明可调用的本地文档工具和能力边界。");
            return;
        }

        if (TypographyAiCoordinator.TryCreateLocalToolPlan(instruction, out var localPlan))
        {
            try
            {
                SetBusy(true);
                AddActivity("已识别为本地文档工具，无需调用 AI 模型。");
                AddPlanMessage(localPlan);
                Remember("用户", instruction);
                Remember("Agent", localPlan.Understanding);
                await ExecutePlanAsync(localPlan, instruction);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                AddAgentMessage($"本地工具没有完成：{ex.Message}", "工具结果", error: true);
            }
            finally { SetBusy(false); }
            return;
        }
        Label? progress = null;
        try
        {
            SetBusy(true);
            progress = AddActivity("正在理解目标并规划可调用的文档工具…");
            _ = SaveConfiguration();
            var plan = await TypographyAiCoordinator.PlanWorkflowAsync(
                TypographyAiModule.Load(), _settings, _keyStore, BuildPlanningInput(instruction));
            plan = TypographyAiCoordinator.EnforceInstructionScope(instruction, plan);
            progress.Text = "计划已生成，准备调用本地工具。";
            AddPlanMessage(plan);
            Remember("用户", instruction);
            Remember("Agent", plan.Understanding);
            await ExecutePlanAsync(plan, instruction);
        }
        catch (Exception ex) when (IsExpectedAiError(ex))
        {
            if (ex is InvalidDataException or InvalidOperationException) SetConnectionExpanded(true);
            if (progress is not null) progress.Text = "任务未执行。";
            AddAgentMessage($"没有完成这次任务：{ex.Message}\n请检查连接设置后重试；离线检查仍然可用。", "Agent", error: true);
        }
        finally { SetBusy(false); }
    }

    private void AddPlanMessage(TypographyAgentPlan plan)
    {
        AddAgentMessage(plan.Understanding, "Agent");
        AddWorkflowPlan(plan.Steps);
    }

    private async Task ExecutePlanAsync(TypographyAgentPlan plan, string instruction)
    {
        switch (plan.Intent)
        {
            case TypographyAgentIntent.Assess:
                var assessmentProgress = AddActivity("正在执行 · 读取 Markdown AST、测量页面并评估排版质量");
                var assessmentOnly = new TypographyConstraints(ForbiddenTaskTypes:
                    Enum.GetValues<TypographyTaskType>().ToHashSet());
                var assessmentProposal = await _prepareOptimization(assessmentOnly);
                assessmentProgress.Text = "已完成 · 本地排版分和问题清单已返回侧栏";
                AddInlineAssessment(assessmentProposal);
                Remember("工具", "已在对话中展示本地排版分与缺陷；文档正文未发送给 AI。");
                break;

            case TypographyAgentIntent.CreateTemplate:
                AddActivity("调用工具：提取规则、校验取值并写入本地模板库。");
                var item = await TypographyAiCoordinator.CreateTemplateAsync(
                    TypographyAiModule.Load(), _settings, _keyStore,
                    plan.TemplateName ?? "我的 Agent 排版模板", instruction,
                    TypographyTemplateDraftSource.Description);
                item = _templateStore.Save(item);
                _templateCreated(item);
                AddAgentMessage($"模板“{item.Name}”已经保存并设为当前模板。规则仍可在模板库中继续编辑。", "工具结果");
                Remember("工具", $"已保存规则模板“{item.Name}”。");
                break;

            case TypographyAgentIntent.ExportHtml:
            case TypographyAgentIntent.ExportPdf:
            case TypographyAgentIntent.ExportImage:
            case TypographyAgentIntent.ExportMarkdown:
            case TypographyAgentIntent.ImportDocument:
            case TypographyAgentIntent.Print:
                var toolName = LocalToolName(plan.Intent);
                var toolProgress = AddActivity($"正在执行 · {toolName}");
                var toolResult = await _runLocalDocumentTool(plan.Intent);
                toolProgress.Text = toolResult.Completed
                    ? $"已完成 · {toolName}"
                    : $"已取消 · {toolName}未执行";
                AddAgentMessage(toolResult.Message, "工具结果", error: false);
                Remember("工具", toolResult.Message);
                break;

            case TypographyAgentIntent.Unsupported:
                AddActivity("未执行 · 没有调用任何文档工具");
                AddAgentMessage(
                    "我没有找到与这条要求完全匹配的安全工具，所以没有进入 A4 优化，也没有修改文档。\n" +
                    "你可以换一种自然说法，或点“使用帮助”查看能力范围；连接 AI 后不需要背固定命令。",
                    "需要更明确的指令");
                Remember("Agent", "未识别的要求已安全停止，没有调用排版优化或其他工具。");
                break;

            default:
                var scopedConstraints = TypographyAiCoordinator.ApplyExplicitToolScope(
                    instruction, plan.Constraints);
                var constraints = IncludeRejectedTasks(scopedConstraints);
                var progress = AddActivity("正在执行 · 本地评估、修改、复评并生成 Diff");
                var proposal = await _prepareOptimization(constraints);
                progress.Text = proposal.Changes.Count > 0
                    ? $"已完成 · {proposal.Changes.Count} 项改动等待侧栏确认"
                    : proposal.IsExplicitToolRequest
                        ? "已完成 · 指定操作没有产生可应用的改动"
                        : "已完成 · 当前没有可安全应用的改动";
                AddInlineReview(proposal);
                Remember("工具", proposal.Changes.Count > 0
                    ? $"已在对话中生成 {proposal.Changes.Count} 项本地排版改动，等待用户逐项确认。"
                    : "本地排版闭环未找到可安全提升排版分的自动改动。");
                break;
        }
    }

    private static string LocalToolName(TypographyAgentIntent intent) => intent switch
    {
        TypographyAgentIntent.ExportHtml => "导出 HTML",
        TypographyAgentIntent.ExportPdf => "导出 PDF",
        TypographyAgentIntent.ExportImage => "导出图片",
        TypographyAgentIntent.ExportMarkdown => "另存为 Markdown",
        TypographyAgentIntent.ImportDocument => "导入并转换文档",
        TypographyAgentIntent.Print => "打开打印界面",
        _ => "运行本地文档工具",
    };

    private TypographyConstraints IncludeRejectedTasks(TypographyConstraints constraints)
    {
        if (_rejectedTaskTypes.Count == 0) return constraints;
        var forbidden = new HashSet<TypographyTaskType>(
            constraints.ForbiddenTaskTypes ?? new HashSet<TypographyTaskType>());
        forbidden.UnionWith(_rejectedTaskTypes);
        _rejectedTaskTypes.Clear();
        return constraints with { ForbiddenTaskTypes = forbidden };
    }

    private void AddInlineAssessment(TypographyReviewProposal proposal)
    {
        var assessment = proposal.Result.InitialAssessment;
        var card = OneColumnLayout(Math.Max(3, assessment.Defects.Count + 2));
        card.Dock = DockStyle.Fill;
        card.Padding = new Padding(2, 10, 2, 8);
        card.BackColor = Surface;
        card.Controls.Add(new Label
        {
            Text = $"本地排版分 {assessment.Score}/100",
            AutoSize = true,
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            ForeColor = assessment.Score >= 90 ? Accent : Color.FromArgb(148, 84, 23),
            Margin = new Padding(0, 0, 0, 4),
        }, 0, 0);
        card.Controls.Add(new Label
        {
            Text = $"依据“{proposal.TemplateName}”规则 · {assessment.Defects.Count} 个问题 · 不评价正文内容",
            AutoSize = true,
            ForeColor = TextMuted,
            Margin = new Padding(0, 0, 0, 8),
        }, 0, 1);

        if (assessment.Defects.Count == 0)
        {
            card.Controls.Add(new Label
            {
                Text = "没有发现需要处理的排版问题。",
                AutoSize = true,
                ForeColor = Accent,
                Margin = new Padding(0, 4, 0, 4),
            }, 0, 2);
        }
        else
        {
            for (var index = 0; index < assessment.Defects.Count; index++)
            {
                var defect = assessment.Defects[index];
                var severity = defect.Severity switch
                {
                    DefectSeverity.Error => "严重",
                    DefectSeverity.Warning => "警告",
                    _ => "建议",
                };
                card.Controls.Add(new Label
                {
                    Text = $"{severity} · 第 {defect.Location.Line} 行 · {defect.Message}",
                    AutoSize = true,
                    ForeColor = defect.Severity == DefectSeverity.Error ? Error : TextPrimary,
                    Margin = new Padding(0, 4, 0, 5),
                    UseMnemonic = false,
                }, 0, index + 2);
            }
        }

        AddConversationControl(card, "侧栏排版质量评估", new Padding(0, 0, 0, 10));
    }

    private string BuildPlanningInput(string currentInstruction)
    {
        if (_agentContext.Count == 0) return currentInstruction;
        var history = string.Join(Environment.NewLine,
            _agentContext.TakeLast(6).Select(turn => $"{turn.Role}：{turn.Text}"));
        return $"""
            以下是本次 Agent 会话的最近上下文，只用于理解省略或追加条件，不包含文档正文：
            {history}

            用户当前要求：{currentInstruction}
            """;
    }

    private void Remember(string role, string text)
    {
        _agentContext.Add((role, text));
        if (_agentContext.Count > 12) _agentContext.RemoveRange(0, _agentContext.Count - 12);
    }

    private async Task RunLocalAssessmentAsync()
    {
        try
        {
            AddUserMessage("离线检查当前文档");
            SetBusy(true);
            AddActivity("未连接 AI；正在直接调用本地排版评估工具。");
            await _openLocalAssessment();
            AddAgentMessage("离线检查完成。整个过程没有访问网络。", "本地工具");
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
        {
            AddAgentMessage($"本地检查未完成：{ex.Message}", "本地工具", error: true);
        }
        finally { SetBusy(false); }
    }

    private void AddUserMessage(string text) => AddMessage("你", text, UserSurface, TextPrimary, false);
    private void AddAgentMessage(string text, string role, bool error = false) =>
        AddMessage(role, text, Surface, error ? Error : TextPrimary, false);
    private Label AddActivity(string text) => AddWorkflowActivity(text);

    private Label AddMessage(string role, string text, Color background, Color foreground, bool compact)
    {
        var roleLabel = new Label
        {
            Text = role,
            AutoSize = true,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
            ForeColor = role == "你" ? Color.FromArgb(37, 91, 145) : Accent,
            Margin = Padding.Empty,
        };
        var body = new Label
        {
            Text = text,
            AutoSize = true,
            Font = new Font("Segoe UI", compact ? 9F : 9.5F),
            ForeColor = foreground,
            Margin = new Padding(0, 4, 0, 0),
            UseMnemonic = false,
        };
        var isUser = role == "你";
        var layout = OneColumnLayout(isUser ? 1 : 2);
        layout.Dock = DockStyle.Top;
        layout.Padding = isUser
            ? new Padding(14, 11, 14, 12)
            : new Padding(2, compact ? 5 : 9, 8, compact ? 7 : 12);
        layout.BackColor = background;
        if (isUser)
            layout.Controls.Add(body, 0, 0);
        else
        {
            layout.Controls.Add(roleLabel, 0, 0);
            layout.Controls.Add(body, 0, 1);
        }

        var card = new RoundedPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = background,
            Radius = isUser ? 13 : 0,
            BorderWidth = 0,
            Margin = isUser ? new Padding(46, 2, 0, 12) : new Padding(0, 0, 0, 5),
            Padding = Padding.Empty,
            AccessibleName = $"{role}消息",
        };
        card.Controls.Add(layout);
        _messageCards.Add(card);
        _conversation.Controls.Add(card);
        ResizeMessageCards();
        if (IsHandleCreated) BeginInvoke(() => _conversation.ScrollControlIntoView(card));
        return body;
    }

    private void AddWorkflowPlan(IReadOnlyList<string> steps)
    {
        var layout = OneColumnLayout(Math.Max(2, steps.Count + 1));
        layout.Dock = DockStyle.Fill;
        layout.Padding = new Padding(2, 4, 4, 8);
        layout.BackColor = Surface;
        layout.Controls.Add(new Label
        {
            Text = "执行计划",
            AutoSize = true,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = TextPrimary,
            Margin = new Padding(0, 0, 0, 7),
        }, 0, 0);
        for (var index = 0; index < steps.Count; index++)
            layout.Controls.Add(CreateWorkflowRow(steps[index], index == 0).Host, 0, index + 1);

        AddConversationControl(layout, "Agent 工作流", new Padding(0, 0, 0, 8));
    }

    private Label AddWorkflowActivity(string text)
    {
        var row = CreateWorkflowRow(text, completed: false);
        row.Label.TextChanged += (_, _) =>
        {
            row.Marker.Completed = row.Label.Text.Contains("完成", StringComparison.Ordinal)
                || row.Label.Text.Contains("成功", StringComparison.Ordinal)
                || row.Label.Text.Contains("已生成", StringComparison.Ordinal)
                || row.Label.Text.Contains("已准备", StringComparison.Ordinal);
            row.Marker.Invalidate();
        };
        AddConversationControl(row.Host, "工具执行", new Padding(0, 0, 0, 5));
        return row.Label;
    }

    private void AddInlineReview(TypographyReviewProposal proposal)
    {
        if (proposal.Changes.Count == 0)
        {
            var message = proposal.IsExplicitToolRequest
                ? "指定操作没有产生改动；文档可能已经满足要求，或当前内容不具备执行条件。"
                : proposal.Result.RejectedAssessment is { } rejected
                ? $"候选方案会让本地排版分从 {proposal.Result.InitialAssessment.Score} 降到 {rejected.Score}，已自动放弃，文档没有修改。"
                : $"当前本地排版分 {proposal.Result.FinalAssessment.Score}/100，没有找到能安全提升排版分的自动改动，文档没有修改。";
            AddAgentMessage(message, "工具结果");
            return;
        }

        var review = OneColumnLayout(8);
        review.Dock = DockStyle.Fill;
        review.Padding = new Padding(2, 12, 2, 4);
        review.BackColor = Surface;
        review.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Border, Margin = new Padding(0, 0, 0, 14) }, 0, 0);
        review.Controls.Add(new Label
        {
            Text = $"{proposal.Changes.Count} 项改动等待确认",
            AutoSize = true,
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            ForeColor = TextPrimary,
            Margin = new Padding(0, 0, 0, 5),
        }, 0, 1);
        var score = new Label
        {
            Text = proposal.IsExplicitToolRequest
                ? $"{proposal.TemplateName} · 仅执行你指定的工具"
                : $"本地排版分 {proposal.Result.InitialAssessment.Score} → {proposal.Result.FinalAssessment.Score} · 依据“{proposal.TemplateName}”规则 · 不评价正文",
            AutoSize = true,
            ForeColor = TextMuted,
            Margin = new Padding(0, 0, 0, 8),
        };
        review.Controls.Add(score, 0, 2);

        var selection = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = TextPrimary,
            Margin = new Padding(0, 4, 0, 5),
        };
        var selectAll = new CheckBox
        {
            Text = "全选",
            AutoSize = true,
            ThreeState = false,
            Checked = true,
            ForeColor = Accent,
            Margin = new Padding(0, 0, 0, 5),
            AccessibleName = "选择或取消全部排版改动",
        };
        var selectionHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Margin = Padding.Empty,
            BackColor = Surface,
        };
        selectionHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        selectionHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        selectionHeader.Controls.Add(selection, 0, 0);
        selectionHeader.Controls.Add(selectAll, 1, 0);
        review.Controls.Add(selectionHeader, 0, 3);

        var changeList = OneColumnLayout(proposal.Changes.Count);
        changeList.Padding = new Padding(0, 0, 0, 6);
        var checks = new List<CheckBox>();
        var before = DiffPreviewBox();
        var after = DiffPreviewBox();
        foreach (var change in proposal.Changes)
        {
            var check = new CheckBox
            {
                Text = $"第 {change.Line} 行 · {change.Summary}",
                Checked = true,
                AutoSize = true,
                Dock = DockStyle.Top,
                ForeColor = TextPrimary,
                Margin = new Padding(0, 4, 0, 5),
                Tag = change,
                AccessibleName = $"应用改动：{change.Summary}",
            };
            check.Click += (_, _) => ShowInlineChange(change, before, after);
            checks.Add(check);
            changeList.Controls.Add(check);
        }
        review.Controls.Add(changeList, 0, 4);

        var preview = OneColumnLayout(4);
        preview.BackColor = Surface;
        preview.Controls.Add(FieldLabel("修改前"), 0, 0);
        preview.Controls.Add(WrapDiffPreview(before), 0, 1);
        preview.Controls.Add(FieldLabel("修改后"), 0, 2);
        preview.Controls.Add(WrapDiffPreview(after), 0, 3);
        review.Controls.Add(preview, 0, 5);

        var state = new Label
        {
            Text = "只有勾选并确认的改动才会写回文档。",
            AutoSize = true,
            ForeColor = TextMuted,
            Margin = new Padding(0, 8, 0, 8),
            AccessibleRole = AccessibleRole.StaticText,
        };
        review.Controls.Add(state, 0, 6);

        var apply = PrimaryButton("应用已选改动");
        apply.AccessibleName = "应用侧栏中已勾选的排版改动";
        var reject = SecondaryButton("全部拒绝");
        reject.AccessibleName = "拒绝本次全部排版改动";
        var undo = SecondaryButton("撤销本次应用");
        undo.AccessibleName = "撤销这张差异卡片中刚刚应用的改动";
        undo.Visible = false;
        undo.Enabled = false;
        var actions = BuildActionRow(reject, apply, undo);
        actions.Margin = Padding.Empty;
        review.Controls.Add(actions, 0, 7);

        var synchronizing = false;
        string[] appliedChangeIds = [];
        void UpdateSelection()
        {
            var selected = checks.Count(item => item.Checked);
            selection.Text = $"已选择 {selected} / {checks.Count} 项";
            apply.Enabled = selected > 0;
            apply.Text = selected > 0 ? $"应用 {selected} 项改动" : "未选择改动";
            synchronizing = true;
            selectAll.Checked = selected == checks.Count;
            synchronizing = false;
        }

        foreach (var check in checks) check.CheckedChanged += (_, _) => UpdateSelection();
        selectAll.CheckStateChanged += (_, _) =>
        {
            if (synchronizing) return;
            foreach (var check in checks) check.Checked = selectAll.Checked;
            UpdateSelection();
        };

        void FinishReview(string message)
        {
            foreach (var check in checks) check.Enabled = false;
            selectAll.Enabled = false;
            apply.Enabled = false;
            reject.Enabled = false;
            state.Text = message;
            state.ForeColor = Accent;
            state.AccessibleName = message;
        }

        apply.Click += async (_, _) =>
        {
            var accepted = checks.Where(item => item.Checked)
                .Select(item => (DocumentChange)item.Tag!)
                .ToArray();
            var rejected = checks.Where(item => !item.Checked)
                .Select(item => (DocumentChange)item.Tag!)
                .ToArray();
            apply.Enabled = false;
            reject.Enabled = false;
            state.Text = "正在把已选改动写回文档…";
            try
            {
                SetBusy(true);
                appliedChangeIds = accepted.Select(change => change.Id).ToArray();
                await _applyOptimization(proposal, appliedChangeIds);
                foreach (var change in rejected) _rejectedTaskTypes.Add(change.Type);
                FinishReview(rejected.Length == 0
                    ? $"已应用全部 {accepted.Length} 项改动。"
                    : $"已应用 {accepted.Length} 项；拒绝的 {rejected.Length} 项会作为后续规划约束。");
                apply.Visible = false;
                reject.Visible = false;
                undo.Visible = true;
                undo.Enabled = true;
                AddActivity($"已完成 · {accepted.Length} 项改动已写回当前文档");
                AddAgentMessage("已按你的勾选结果更新文档。如需恢复，可在这张卡片中点击“撤销本次应用”。", "工具结果");
                Remember("工具", $"用户接受 {accepted.Length} 项改动、拒绝 {rejected.Length} 项；拒绝类型已加入后续规划约束。");
            }
            catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
            {
                apply.Enabled = checks.Any(item => item.Checked);
                reject.Enabled = true;
                state.Text = $"没有应用：{ex.Message}";
                state.ForeColor = Error;
            }
            finally { SetBusy(false); }
        };

        undo.Click += async (_, _) =>
        {
            undo.Enabled = false;
            state.Text = "正在撤销本次应用…";
            state.ForeColor = TextMuted;
            try
            {
                SetBusy(true);
                await _undoOptimization(proposal, appliedChangeIds);
                foreach (var change in proposal.Changes.Where(change => appliedChangeIds.Contains(change.Id)))
                    _rejectedTaskTypes.Add(change.Type);
                undo.Visible = false;
                state.Text = "已撤销本次应用，文档已恢复到应用前。";
                state.ForeColor = Accent;
                state.AccessibleName = state.Text;
                AddActivity("已完成 · 本次 Agent 改动已撤销");
                AddAgentMessage("已恢复应用前的文档。之后产生的其他改动没有被覆盖。", "工具结果");
                Remember("工具", "用户撤销了本次已应用的改动；文档已恢复到应用前快照。");
            }
            catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
            {
                undo.Enabled = true;
                state.Text = $"无法撤销：{ex.Message}";
                state.ForeColor = Error;
                state.AccessibleName = state.Text;
            }
            finally { SetBusy(false); }
        };

        reject.Click += (_, _) =>
        {
            foreach (var change in proposal.Changes) _rejectedTaskTypes.Add(change.Type);
            FinishReview("本次改动已全部拒绝，并会作为后续规划约束。文档没有修改。");
            AddActivity("已完成 · 本次改动全部拒绝，文档保持不变");
            Remember("工具", "用户拒绝了本次全部改动；相关工具类型已加入后续规划约束。");
        };

        ShowInlineChange(proposal.Changes[0], before, after);
        UpdateSelection();
        AddConversationControl(review, "侧栏差异审阅", new Padding(0, 0, 0, 10));
    }

    private static TextBox DiffPreviewBox() => new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        WordWrap = true,
        ScrollBars = ScrollBars.None,
        BorderStyle = BorderStyle.None,
        BackColor = Canvas,
        ForeColor = TextPrimary,
        Font = new Font("Consolas", 8.5F),
        TabStop = true,
    };

    private static Control WrapDiffPreview(TextBox preview)
    {
        var host = new RoundedPanel
        {
            Dock = DockStyle.Top,
            Height = 58,
            Radius = 8,
            BorderColor = Border,
            BorderWidth = 1,
            BackColor = Canvas,
            Padding = new Padding(9, 7, 7, 7),
            Margin = new Padding(0, 3, 0, 7),
        };
        host.Controls.Add(preview);
        var state = new DiffPreviewState(preview, host);
        preview.Tag = state;
        preview.SizeChanged += (_, _) => state.RefreshHeight();
        return host;
    }

    private static void ShowInlineChange(DocumentChange change, TextBox before, TextBox after)
    {
        before.Text = VisibleWhitespace(change.Before, change.BeforeLineCount);
        after.Text = VisibleWhitespace(change.After, change.AfterLineCount);
        before.AccessibleName = $"{change.Summary}修改前";
        after.AccessibleName = $"{change.Summary}修改后";
        (before.Tag as DiffPreviewState)?.Reset();
        (after.Tag as DiffPreviewState)?.Reset();
    }

    private sealed class DiffPreviewState(
        TextBox preview,
        RoundedPanel host)
    {
        private bool _resizing;

        public void Reset()
        {
            RefreshHeight();
        }

        public void RefreshHeight()
        {
            if (_resizing) return;
            _resizing = true;
            try
            {
                var width = Math.Max(240, preview.ClientSize.Width > 40
                    ? preview.ClientSize.Width - 4
                    : 360);
                var measured = TextRenderer.MeasureText(
                    preview.Text + " ", preview.Font, new Size(width, int.MaxValue),
                    TextFormatFlags.TextBoxControl | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
                var fullHeight = Math.Clamp(measured.Height + host.Padding.Vertical + 12, 58, 12000);
                host.Height = fullHeight;
            }
            finally
            {
                _resizing = false;
            }
        }
    }

    private static string VisibleWhitespace(string value, int lineCount = 0)
    {
        if (lineCount == 0 && value.Length == 0) return "⟦无内容⟧";
        var lines = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        if (lineCount > lines.Length)
            lines = lines.Concat(Enumerable.Repeat(string.Empty, lineCount - lines.Length)).ToArray();
        if (lineCount > 0 && lines.Length > lineCount) lines = lines.Take(lineCount).ToArray();
        return string.Join(Environment.NewLine,
            lines.Select(line => line.Length == 0 ? "⟦空行⟧" : line));
    }

    private static (Control Host, Label Label, WorkflowMarker Marker) CreateWorkflowRow(string text, bool completed)
    {
        var marker = new WorkflowMarker { Completed = completed, Margin = new Padding(0, 0, 8, 0) };
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = completed ? TextPrimary : TextMuted,
            Font = new Font("Segoe UI", 9F),
            Margin = new Padding(0, 5, 0, 7),
            UseMnemonic = false,
        };
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Margin = Padding.Empty,
            BackColor = Surface,
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.Controls.Add(marker, 0, 0);
        row.Controls.Add(label, 1, 0);
        return (row, label, marker);
    }

    private void AddConversationControl(Control control, string accessibleName, Padding margin)
    {
        var host = new Panel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Surface,
            Margin = margin,
            Padding = Padding.Empty,
            AccessibleName = accessibleName,
        };
        control.Dock = DockStyle.Top;
        host.Controls.Add(control);
        host.SizeChanged += (_, _) => QueueConversationExtentRefresh();
        control.SizeChanged += (_, _) => QueueConversationExtentRefresh();
        _messageCards.Add(host);
        _conversation.Controls.Add(host);
        ResizeMessageCards();
        RefreshConversationScrollExtent();
        if (IsHandleCreated) BeginInvoke(() =>
        {
            RefreshConversationScrollExtent();
            _conversation.ScrollControlIntoView(host);
        });
    }

    private void ResizeMessageCards()
    {
        var width = Math.Max(220, _conversation.ClientSize.Width - _conversation.Padding.Horizontal
            - SystemInformation.VerticalScrollBarWidth - 20);
        foreach (var card in _messageCards)
        {
            var targetWidth = card.AccessibleName == "你消息" ? Math.Max(188, width - 32) : width;
            if (Math.Abs(card.MinimumSize.Width - targetWidth) < 4) continue;
            card.MinimumSize = new Size(targetWidth, 0);
            card.MaximumSize = new Size(targetWidth, 0);
            card.Width = targetWidth;
            foreach (var label in Descendants(card).OfType<Label>())
                label.MaximumSize = new Size(Math.Max(160, targetWidth - 30), 0);
        }
        RefreshConversationScrollExtent();
    }

    private void QueueConversationExtentRefresh()
    {
        if (!IsHandleCreated || _conversationExtentRefreshPending) return;
        _conversationExtentRefreshPending = true;
        BeginInvoke(() =>
        {
            _conversationExtentRefreshPending = false;
            RefreshConversationScrollExtent();
        });
    }

    private void RefreshConversationScrollExtent()
    {
        if (_refreshingConversationExtent || IsDisposed) return;
        _refreshingConversationExtent = true;
        try
        {
            var contentHeight = _conversation.Padding.Vertical + 16;
            foreach (Control card in _conversation.Controls)
            {
                if (!card.Visible) continue;
                contentHeight += card.Height + card.Margin.Vertical;
            }
            _conversation.AutoScrollMinSize = new Size(0, contentHeight);
        }
        finally
        {
            _refreshingConversationExtent = false;
        }
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private void LoadSettings()
    {
        _loading = true;
        var provider = AiProviderCatalog.Find(_settings.AiProviderId);
        _provider.SelectedIndex = AiProviderCatalog.All
            .Select((item, index) => (item, index))
            .First(pair => pair.item.Id == provider.Id).index;
        _baseUrl.Text = string.IsNullOrWhiteSpace(_settings.AiBaseUrl) ? provider.BaseUrl : _settings.AiBaseUrl;
        PopulateModelOptions(provider,
            string.IsNullOrWhiteSpace(_settings.AiModel) ? provider.DefaultModel : _settings.AiModel);
        LoadProviderKey(provider.Id);
        _loading = false;
        SetConnectionExpanded(false);
        RefreshConnectionSummary();
    }

    private void ProviderChanged()
    {
        if (_loading || _provider.SelectedItem is not AiProviderPreset provider) return;
        _baseUrl.Text = provider.BaseUrl;
        PopulateModelOptions(provider, provider.DefaultModel);
        LoadProviderKey(provider.Id);
        RefreshConnectionSummary();
    }

    private void PopulateModelOptions(AiProviderPreset provider, string modelId)
    {
        _model.BeginUpdate();
        try
        {
            _model.Items.Clear();
            _model.Items.AddRange(provider.Models.Cast<object>().ToArray());
            var match = provider.Models.FirstOrDefault(item =>
                string.Equals(item.Id, modelId, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                _model.SelectedItem = match;
            }
            else
            {
                _model.SelectedIndex = -1;
                _model.Text = modelId;
            }
        }
        finally
        {
            _model.EndUpdate();
        }

        RefreshModelHint();
    }

    private void RefreshModelHint()
    {
        if (_model.SelectedItem is AiModelPreset preset
            && string.Equals(_model.Text, preset.DisplayText, StringComparison.Ordinal))
        {
            _modelHint.Text = $"将使用 {preset.Id}。{preset.Description}。";
            _modelHint.ForeColor = TextMuted;
            return;
        }

        _modelHint.Text = "高级选项：可直接输入服务商提供的模型 ID；实际可用范围取决于你的账号权限。";
        _modelHint.ForeColor = TextMuted;
    }

    private void LoadProviderKey(string providerId)
    {
        try { _apiKey.Text = _keyStore.Load(providerId) ?? string.Empty; }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _apiKey.Clear();
            AddAgentMessage(ex.Message, "连接设置", error: true);
        }
    }

    private void RefreshConnectionSummary()
    {
        var provider = _provider.SelectedItem as AiProviderPreset;
        var generationConnected = _settings.AiEnabled && !string.IsNullOrWhiteSpace(_apiKey.Text);
        _workspaceStatus.Text = ComposeWorkspaceStatus(
            provider?.Name ?? "自定义服务", generationConnected);
        _workspaceStatus.ForeColor = generationConnected ? Accent : TextMuted;
        _workspaceStatus.AccessibleName = $"工作区状态：{_workspaceStatus.Text}";
        _connectionSummary.Text = generationConnected
            ? $"生成模型：{provider?.Name ?? "自定义"} · 本地白名单路由"
            : "AI 服务尚未配置；离线工具可直接使用";
        _connectionSummary.ForeColor = generationConnected ? Accent : TextMuted;
    }

    internal static string ComposeWorkspaceStatus(string providerName, bool generationConnected) =>
        generationConnected
            ? $"AI 已连接（{providerName}）· 本地工具就绪"
            : "本地工具就绪";

    private AiConnectionOptions SaveConfiguration()
    {
        var options = ReadConfiguration();
        var provider = (AiProviderPreset)_provider.SelectedItem!;

        _keyStore.Save(options.ApiKey, provider.Id);
        _settings.AiEnabled = true;
        _settings.AiProviderId = provider.Id;
        _settings.AiBaseUrl = options.BaseUrl;
        _settings.AiModel = options.Model;
        _saveSettings();
        RefreshConnectionSummary();
        return options;
    }

    private AiConnectionOptions ReadConfiguration()
    {
        if (_provider.SelectedItem is not AiProviderPreset provider)
            throw new InvalidDataException("请选择 AI 服务商。");
        if (!Uri.TryCreate(_baseUrl.Text.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
            throw new InvalidDataException("接口地址必须是有效的 HTTP 或 HTTPS 地址。");
        var model = _model.SelectedItem is AiModelPreset preset
            && string.Equals(_model.Text, preset.DisplayText, StringComparison.Ordinal)
                ? preset.Id
                : ValidateModelId(_model.Text);
        if (string.IsNullOrWhiteSpace(_apiKey.Text))
            throw new InvalidDataException("请先配置服务商提供的 API Key。");

        return new AiConnectionOptions(_baseUrl.Text.Trim(), model, _apiKey.Text);
    }

    internal static string ValidateModelId(string value, string fieldName = "模型名称")
    {
        var model = value.Trim();
        if (model.Length == 0) throw new InvalidDataException($"请填写{fieldName}。");
        if (model.Any(char.IsWhiteSpace))
        {
            throw new InvalidDataException(
                $"{fieldName}应填写 API 模型 ID，不能包含空格。DeepSeek V4 Pro 请填写 deepseek-v4-pro。");
        }

        return model;
    }

    private async Task TestConnectionAsync()
    {
        const string idleText = "测试并保存生成模型";
        try
        {
            SetBusy(true);
            _test.Text = "正在测试…";
            SetConnectionStatus(_generationTestStatus, "状态：正在连接并验证服务地址、模型和凭据…", Accent);
            var options = ReadConfiguration();
            await TypographyAiModule.Load().TestConnectionAsync(options);
            _ = SaveConfiguration();
            SetConnectionStatus(_generationTestStatus, "连接成功：生成模型配置已保存，可以返回工作流使用。", Accent);
            AddAgentMessage("AI 服务已经可以使用。现在直接在输入框里说出你的文档任务即可。", "连接设置");
            RefreshConnectionSummary();
        }
        catch (Exception ex) when (IsExpectedAiError(ex))
        {
            SetConnectionExpanded(true);
            SetConnectionStatus(
                _generationTestStatus,
                $"连接失败：{DescribeConnectionError(ex)}",
                Error);
            AddAgentMessage($"连接失败：{ex.Message}\n请检查服务商、模型、地址和 API Key。", "连接设置", error: true);
        }
        finally
        {
            _test.Text = idleText;
            SetBusy(false);
        }
    }

    private static string DescribeConnectionError(Exception ex) => ex switch
    {
        TaskCanceledException => "请求超时。请检查网络、接口地址或稍后重试。",
        HttpRequestException => $"{ex.Message} 请检查网络、接口地址和服务商状态。",
        _ => ex.Message,
    };

    private static void SetConnectionStatus(Label label, string text, Color color)
    {
        label.Text = text;
        label.ForeColor = color;
        label.AccessibleName = text;
    }

    private void SetConnectionExpanded(bool expanded)
    {
        SuspendLayout();
        _connectionExpanded = expanded;
        _connectionHost.Visible = expanded;
        _conversation.Visible = !expanded;
        _suggestionsHost.Visible = !expanded;
        _composerHost.Visible = !expanded;
        _connectionToggle.Text = expanded ? "返回工作流" : "连接设置";
        _connectionToggle.AccessibleName = _connectionToggle.Text;
        ResumeLayout(performLayout: true);
    }

    private void SetBusy(bool busy)
    {
        _send.Enabled = !busy;
        _test.Enabled = !busy;
        _localCheck.Enabled = !busy;
        _connectionToggle.Enabled = !busy;
        _prompt.Enabled = !busy;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
    }

    private static bool IsExpectedAiError(Exception ex) => ex is InvalidDataException
        or InvalidOperationException or HttpRequestException or TaskCanceledException
        or FileNotFoundException or TypeLoadException or System.Text.Json.JsonException
        or System.ComponentModel.Win32Exception;

    private static TableLayoutPanel OneColumnLayout(int rows)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = rows,
            Margin = Padding.Empty,
            BackColor = Surface,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < rows; row++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        return layout;
    }

    private static FlowLayoutPanel BuildActionRow(params Button[] buttons)
    {
        var row = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0, 10, 0, 0),
            BackColor = Surface,
        };
        row.Controls.AddRange(buttons);
        return row;
    }

    private static ComboBox DropDown() => new()
    {
        Dock = DockStyle.Top,
        DropDownStyle = ComboBoxStyle.DropDownList,
        FlatStyle = FlatStyle.Flat,
        Margin = new Padding(0, 4, 0, 6),
        AccessibleRole = AccessibleRole.ComboBox,
    };

    private static ComboBox ModelDropDown() => new()
    {
        Dock = DockStyle.Top,
        DropDownStyle = ComboBoxStyle.DropDown,
        FlatStyle = FlatStyle.Flat,
        AutoCompleteMode = AutoCompleteMode.SuggestAppend,
        AutoCompleteSource = AutoCompleteSource.ListItems,
        IntegralHeight = false,
        MaxDropDownItems = 8,
        Margin = new Padding(0, 4, 0, 6),
        AccessibleName = "生成模型",
        AccessibleDescription = "选择推荐模型，或直接输入服务商提供的模型 ID",
        AccessibleRole = AccessibleRole.ComboBox,
    };

    private static TextBox TextField() => new()
    {
        Dock = DockStyle.Top,
        BorderStyle = BorderStyle.None,
        BackColor = Surface,
        Margin = new Padding(0, 4, 0, 6),
    };

    private static Control WrapField(Control control)
    {
        control.Dock = DockStyle.Fill;
        control.Margin = Padding.Empty;
        var host = new RoundedPanel
        {
            Dock = DockStyle.Top,
            Radius = 8,
            BorderColor = Border,
            BorderWidth = 1,
            BackColor = Surface,
            Margin = new Padding(0, 3, 0, 5),
        };
        host.Controls.Add(control);
        void ApplyMetrics()
        {
            var scale = Math.Max(1F, host.DeviceDpi / 96F);
            int Px(int logical) => (int)Math.Ceiling(logical * scale);
            host.Padding = control is ComboBox
                ? new Padding(Px(6), Px(5), Px(6), Px(5))
                : new Padding(Px(9), Px(7), Px(9), Px(7));
            // Native ComboBox/TextBox painting may consume a few pixels beyond PreferredSize.
            // Keep explicit descender room so PerMonitorV2 scaling never lets the border clip glyphs.
            var requiredHeight = Math.Max(
                Px(44), control.PreferredSize.Height + host.Padding.Vertical + Px(4));
            host.MinimumSize = new Size(0, requiredHeight);
            host.Height = requiredHeight;
        }

        host.HandleCreated += (_, _) => ApplyMetrics();
        host.DpiChangedAfterParent += (_, _) => ApplyMetrics();
        control.FontChanged += (_, _) => ApplyMetrics();
        ApplyMetrics();
        return host;
    }

    private static Label FieldLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font("Segoe UI", 9F, FontStyle.Bold),
        ForeColor = TextPrimary,
        Margin = new Padding(0, 4, 0, 1),
    };

    private static Label BodyLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = TextMuted,
        Margin = Padding.Empty,
    };

    private static Label ConnectionStatusLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = TextMuted,
        Margin = new Padding(2, 5, 2, 4),
        AccessibleName = "连接测试状态",
        AccessibleRole = AccessibleRole.StaticText,
    };

    private static Button PrimaryButton(string text)
    {
        var button = SecondaryButton(text);
        button.BackColor = Accent;
        button.ForeColor = Color.White;
        button.FlatAppearance.BorderSize = 0;
        button.MinimumSize = new Size(108, 36);
        if (button is RoundedButton rounded)
        {
            rounded.BorderColor = Color.Transparent;
            rounded.HoverBackColor = Color.FromArgb(20, 96, 66);
            rounded.PressedBackColor = Color.FromArgb(17, 82, 56);
        }
        return button;
    }

    private static Button SendButton()
    {
        var button = (RoundedButton)PrimaryButton(string.Empty);
        button.AutoSize = false;
        button.MinimumSize = Size.Empty;
        button.MaximumSize = Size.Empty;
        button.Size = new Size(42, 42);
        button.MinimumSize = button.Size;
        button.MaximumSize = button.Size;
        button.Padding = Padding.Empty;
        button.Margin = new Padding(8, 0, 0, 0);
        button.Radius = 21;
        button.DrawSendArrow = true;
        return button;
    }

    private static Button SecondaryButton(string text)
    {
        var button = new RoundedButton
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(88, 34),
            Padding = new Padding(10, 4, 10, 4),
            Margin = new Padding(0, 0, 7, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = Surface,
            ForeColor = TextPrimary,
            UseVisualStyleBackColor = false,
            Cursor = Cursors.Hand,
            Radius = 9,
            BorderColor = Border,
            HoverBackColor = Color.FromArgb(244, 247, 244),
            PressedBackColor = Color.FromArgb(234, 239, 235),
        };
        return button;
    }

    private static Button QuietButton(string text)
    {
        var button = SecondaryButton(text);
        button.ForeColor = Accent;
        button.MinimumSize = new Size(0, 34);
        if (button is RoundedButton rounded)
        {
            rounded.BorderColor = Color.Transparent;
            rounded.BackColor = Surface;
            rounded.HoverBackColor = AccentSoft;
            rounded.PressedBackColor = Color.FromArgb(215, 235, 223);
        }
        return button;
    }

    private sealed class BufferedFlowLayoutPanel : FlowLayoutPanel
    {
        public BufferedFlowLayoutPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            // This workspace intentionally has one vertical reading flow.  A tiny
            // preferred-width overrun must never introduce a second, horizontal axis.
            HorizontalScroll.Value = 0;
            HorizontalScroll.Enabled = false;
            HorizontalScroll.Visible = false;
        }

        protected override void OnScroll(ScrollEventArgs se)
        {
            if (se.ScrollOrientation == ScrollOrientation.HorizontalScroll) return;
            base.OnScroll(se);
        }
    }

    private sealed class StatusDot : Control
    {
        public StatusDot()
        {
            Size = new Size(12, 18);
            MinimumSize = Size;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(Accent);
            eventArgs.Graphics.FillEllipse(brush, 2, 5, 8, 8);
        }
    }

    private sealed class WorkflowMarker : Control
    {
        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool Completed { get; set; }

        public WorkflowMarker()
        {
            Size = new Size(24, 34);
            MinimumSize = Size;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var line = new Pen(Border, 1.5F);
            eventArgs.Graphics.DrawLine(line, 11, 18, 11, Height);
            var markerBounds = new Rectangle(4, 5, 15, 15);
            using var fill = new SolidBrush(Completed ? Accent : Surface);
            using var outline = new Pen(Completed ? Accent : Color.FromArgb(162, 173, 166), 1.8F);
            eventArgs.Graphics.FillEllipse(fill, markerBounds);
            eventArgs.Graphics.DrawEllipse(outline, markerBounds);
            if (!Completed) return;
            using var check = new Pen(Color.White, 1.6F)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
            eventArgs.Graphics.DrawLines(check, [new Point(8, 12), new Point(11, 15), new Point(16, 9)]);
        }
    }

    private sealed class RoundedPanel : Panel
    {
        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int Radius { get; set; } = 12;
        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int BorderWidth { get; set; }
        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Color BorderColor { get; set; } = Color.Transparent;

        public RoundedPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        }

        protected override void OnPaintBackground(PaintEventArgs eventArgs)
        {
            var parentColor = Parent?.BackColor ?? Canvas;
            eventArgs.Graphics.Clear(parentColor.A == 0 ? Surface : parentColor);
            eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            eventArgs.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            var scaledRadius = Math.Max(0, (int)Math.Round(Radius * DeviceDpi / 96F));
            using var path = RoundedGeometry.Create(ClientRectangle, scaledRadius);
            using var brush = new SolidBrush(BackColor);
            eventArgs.Graphics.FillPath(brush, path);
            if (BorderWidth <= 0 || BorderColor == Color.Transparent) return;
            var penWidth = Math.Max(1F, BorderWidth * DeviceDpi / 96F);
            var inset = Math.Max(1, (int)Math.Ceiling(penWidth / 2F));
            var borderBounds = new Rectangle(
                inset,
                inset,
                Math.Max(0, ClientSize.Width - inset * 2),
                Math.Max(0, ClientSize.Height - inset * 2));
            using var borderPath = RoundedGeometry.Create(borderBounds, Math.Max(1, scaledRadius - 1));
            using var pen = new Pen(BorderColor, penWidth);
            eventArgs.Graphics.DrawPath(pen, borderPath);
        }
    }

    private sealed class RoundedButton : Button
    {
        private bool _hovered;
        private bool _pressed;

        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int Radius { get; set; } = 9;
        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Color BorderColor { get; set; } = Color.Transparent;
        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Color HoverBackColor { get; set; } = Color.Empty;
        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Color PressedBackColor { get; set; } = Color.Empty;
        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool DrawSendArrow { get; set; }

        public RoundedButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        }

        protected override void OnMouseEnter(EventArgs eventArgs)
        {
            _hovered = true;
            Invalidate();
            base.OnMouseEnter(eventArgs);
        }

        protected override void OnMouseLeave(EventArgs eventArgs)
        {
            _hovered = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(eventArgs);
        }

        protected override void OnMouseDown(MouseEventArgs eventArgs)
        {
            _pressed = true;
            Invalidate();
            base.OnMouseDown(eventArgs);
        }

        protected override void OnMouseUp(MouseEventArgs eventArgs)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(eventArgs);
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            var parentColor = Parent?.BackColor ?? Surface;
            eventArgs.Graphics.Clear(parentColor.A == 0 ? Surface : parentColor);
            eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            eventArgs.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            var fill = !Enabled
                ? Color.FromArgb(238, 240, 238)
                : _pressed && PressedBackColor != Color.Empty
                    ? PressedBackColor
                    : _hovered && HoverBackColor != Color.Empty ? HoverBackColor : BackColor;
            var foreground = Enabled ? ForeColor : Color.FromArgb(145, 151, 147);
            var radius = DrawSendArrow
                ? Math.Max(1, ClientSize.Height / 2)
                : Math.Max(1, (int)Math.Round(Radius * DeviceDpi / 96F));
            using var path = RoundedGeometry.Create(Rectangle.Inflate(ClientRectangle, -1, -1), radius);
            using var brush = new SolidBrush(fill);
            eventArgs.Graphics.FillPath(brush, path);
            if (BorderColor != Color.Transparent)
            {
                using var pen = new Pen(BorderColor);
                eventArgs.Graphics.DrawPath(pen, path);
            }
            if (DrawSendArrow)
            {
                using var pen = new Pen(foreground, 2F)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                    LineJoin = LineJoin.Round,
                };
                var centerX = ClientRectangle.Left + ClientRectangle.Width / 2;
                var centerY = ClientRectangle.Top + ClientRectangle.Height / 2;
                eventArgs.Graphics.DrawLine(pen, centerX - 8, centerY, centerX + 7, centerY);
                eventArgs.Graphics.DrawLine(pen, centerX + 7, centerY, centerX + 1, centerY - 6);
                eventArgs.Graphics.DrawLine(pen, centerX + 7, centerY, centerX + 1, centerY + 6);
            }
            else
            {
                TextRenderer.DrawText(eventArgs.Graphics, Text, Font, ClientRectangle, foreground,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
            if (Focused && ShowFocusCues)
            {
                var focusBounds = Rectangle.Inflate(ClientRectangle, -3, -3);
                var focusRadius = DrawSendArrow
                    ? Math.Max(1, focusBounds.Height / 2)
                    : Math.Max(1, radius - 2);
                using var focusPath = RoundedGeometry.Create(focusBounds, focusRadius);
                var focusColor = fill.GetBrightness() < 0.55F ? Color.White : Accent;
                using var focusPen = new Pen(focusColor, Math.Max(1.5F, DeviceDpi / 96F));
                eventArgs.Graphics.DrawPath(focusPen, focusPath);
            }
        }
    }

    private static class RoundedGeometry
    {
        public static GraphicsPath Create(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            if (bounds.Width <= 0 || bounds.Height <= 0) return path;
            var diameter = Math.Min(Math.Max(0, radius * 2), Math.Min(bounds.Width, bounds.Height));
            if (diameter <= 1)
            {
                path.AddRectangle(bounds);
                return path;
            }
            var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    private static class NativeCueBanner
    {
        private const int EmSetCueBanner = 0x1501;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr showWhenFocused, string text);

        public static void Set(TextBox textBox, string text) =>
            _ = SendMessage(textBox.Handle, EmSetCueBanner, (IntPtr)1, text);
    }
}
