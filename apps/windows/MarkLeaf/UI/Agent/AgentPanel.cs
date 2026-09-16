using MarkLeaf.Services.AI;
using MarkLeaf.Services.Proof;
using MarkLeaf.Services.Settings;
using System.Diagnostics;

namespace MarkLeaf.UI.Agent;

internal sealed class AgentPanel : UserControl
{
    private static readonly Color Canvas = Color.FromArgb(247, 249, 248);
    private static readonly Color Surface = Color.FromArgb(255, 255, 255);
    private static readonly Color SurfaceMuted = Color.FromArgb(238, 243, 241);
    private static readonly Color Border = Color.FromArgb(214, 223, 219);
    private static readonly Color Ink = Color.FromArgb(27, 41, 36);
    private static readonly Color Muted = Color.FromArgb(91, 108, 101);
    private static readonly Color Accent = Color.FromArgb(24, 105, 86);
    private static readonly Color AccentSoft = Color.FromArgb(216, 237, 230);
    private static readonly Color Danger = Color.FromArgb(174, 62, 57);
    private static readonly Color Warning = Color.FromArgb(166, 102, 31);

    private readonly Func<string?> _getWorkspaceRoot;
    private readonly Func<string?> _getDocumentPath;
    private readonly Func<Task<string>> _getCurrentMarkdown;
    private readonly Action<string> _insertMarkdown;
    private readonly AiSettings _settings;
    private readonly Action<string> _rememberApiKey;
    private readonly Action _saveSettings;
    private readonly ProofProjectStore _store = new();
    private readonly OpenAiCompatibleClient _client = new();

    private readonly Label _contextLabel = new();
    private readonly Label _modelLabel = new();
    private readonly Panel _contentHost = new();
    private readonly Button _agentTab = new();
    private readonly Button _projectTab = new();
    private readonly Button _checkTab = new();
    private readonly RichTextBox _conversation = new();
    private readonly TextBox _prompt = new();
    private readonly Button _sendButton = new();
    private readonly Button _cancelButton = new();
    private readonly Button _applyButton = new();
    private readonly Label _agentStatus = new();
    private readonly Label _projectSummary = new();
    private readonly ListView _requirementList = new();
    private readonly ListView _sourceList = new();
    private readonly Label _checkSummary = new();
    private readonly ListView _checkList = new();

    private readonly Panel _agentPage;
    private readonly Panel _projectPage;
    private readonly Panel _checkPage;
    private ProofProject _project = new();
    private ProofCiResult? _lastCiResult;
    private string? _loadedRoot;
    private string _sessionApiKey;
    private string _lastAnswer = string.Empty;
    private IReadOnlyList<AiSource> _lastSources = [];
    private CancellationTokenSource? _cancellation;

    public AgentPanel(
        Func<string?> getWorkspaceRoot,
        Func<string?> getDocumentPath,
        Func<Task<string>> getCurrentMarkdown,
        Action<string> insertMarkdown,
        AiSettings settings,
        string sessionApiKey,
        Action<string> rememberApiKey,
        Action saveSettings)
    {
        _getWorkspaceRoot = getWorkspaceRoot;
        _getDocumentPath = getDocumentPath;
        _getCurrentMarkdown = getCurrentMarkdown;
        _insertMarkdown = insertMarkdown;
        _settings = settings;
        _sessionApiKey = sessionApiKey;
        _rememberApiKey = rememberApiKey;
        _saveSettings = saveSettings;

        Dock = DockStyle.Fill;
        BackColor = Canvas;
        MinimumSize = new Size(350, 0);
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;

        _agentPage = BuildAgentPage();
        _projectPage = BuildProjectPage();
        _checkPage = BuildCheckPage();
        Controls.Add(BuildRoot());
        ShowPage(_agentPage, _agentTab);
        AppendMessage("MarkLeaf Agent", "告诉我你要完成什么文档任务。我会先查看当前文档和项目资料，再提出可核对的修改建议。", Accent);
    }

    public async Task RefreshContextAsync()
    {
        try
        {
            await EnsureProjectAsync();
            RefreshProjectViews();
            UpdateHeader();
        }
        catch (Exception exception)
        {
            _contextLabel.Text = "项目读取失败";
            _agentStatus.Text = exception.Message;
        }
    }

    public void FocusComposer()
    {
        ShowPage(_agentPage, _agentTab);
        _prompt.Focus();
    }

    private Control BuildRoot()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Canvas,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildTabs(), 0, 1);
        root.Controls.Add(_contentHost, 0, 2);
        return root;
    }

    private Control BuildHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(14, 11, 12, 8),
            ColumnCount = 3,
            RowCount = 2,
            BackColor = Surface,
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        var mark = new Label
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 8, 0),
            Text = "M",
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Accent,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
        };
        var title = new Label
        {
            Dock = DockStyle.Fill,
            Text = "MarkLeaf Agent",
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Ink,
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
        };
        _contextLabel.Dock = DockStyle.Fill;
        _contextLabel.Text = "正在读取上下文";
        _contextLabel.TextAlign = ContentAlignment.MiddleLeft;
        _contextLabel.ForeColor = Muted;
        _contextLabel.AutoEllipsis = true;
        _modelLabel.Dock = DockStyle.Fill;
        _modelLabel.TextAlign = ContentAlignment.MiddleRight;
        _modelLabel.ForeColor = Muted;
        _modelLabel.Font = new Font("Segoe UI", 8F);
        var settings = CreateTextButton("设置");
        settings.Margin = new Padding(8, 0, 0, 0);
        settings.Click += (_, _) => ShowAgentSettings();
        header.Controls.Add(mark, 0, 0);
        header.SetRowSpan(mark, 2);
        header.Controls.Add(title, 1, 0);
        header.Controls.Add(_contextLabel, 1, 1);
        header.Controls.Add(settings, 2, 0);
        header.Controls.Add(_modelLabel, 2, 1);
        return header;
    }

    private Control BuildTabs()
    {
        var tabs = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(12, 4, 12, 4),
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Surface,
        };
        for (var index = 0; index < 3; index++) tabs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
        ConfigureTab(_agentTab, "Agent", (_, _) => ShowPage(_agentPage, _agentTab));
        ConfigureTab(_projectTab, "项目上下文", async (_, _) =>
        {
            await RefreshContextAsync();
            ShowPage(_projectPage, _projectTab);
        });
        ConfigureTab(_checkTab, "体检", async (_, _) =>
        {
            await RunDocumentCheckAsync();
            ShowPage(_checkPage, _checkTab);
        });
        tabs.Controls.Add(_agentTab, 0, 0);
        tabs.Controls.Add(_projectTab, 1, 0);
        tabs.Controls.Add(_checkTab, 2, 0);
        return tabs;
    }

    private Panel BuildAgentPage()
    {
        var page = new Panel { Dock = DockStyle.Fill, BackColor = Canvas, Padding = new Padding(12, 10, 12, 12) };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = Canvas,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 67));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.Controls.Add(BuildQuickActions(), 0, 0);

        _conversation.Dock = DockStyle.Fill;
        _conversation.Margin = new Padding(0, 0, 0, 8);
        _conversation.ReadOnly = true;
        _conversation.BorderStyle = BorderStyle.FixedSingle;
        _conversation.BackColor = Surface;
        _conversation.ForeColor = Ink;
        _conversation.Font = new Font("Segoe UI", 9.5F);
        _conversation.DetectUrls = false;
        layout.Controls.Add(_conversation, 0, 1);

        _agentStatus.Dock = DockStyle.Fill;
        _agentStatus.Text = "不会自动修改文档";
        _agentStatus.ForeColor = Muted;
        _agentStatus.AutoEllipsis = true;
        layout.Controls.Add(_agentStatus, 0, 2);

        _prompt.Dock = DockStyle.Fill;
        _prompt.Margin = Padding.Empty;
        _prompt.Multiline = true;
        _prompt.ScrollBars = ScrollBars.Vertical;
        _prompt.AcceptsReturn = true;
        _prompt.PlaceholderText = "例如：根据现有资料补写应用价值，并标注每个关键结论的来源";
        _prompt.Font = new Font("Segoe UI", 10F);
        _prompt.BackColor = Surface;
        _prompt.ForeColor = Ink;
        _prompt.KeyDown += async (_, eventArgs) =>
        {
            if (eventArgs.KeyCode == Keys.Enter && eventArgs.Control)
            {
                eventArgs.SuppressKeyPress = true;
                await RunAgentAsync();
            }
        };
        layout.Controls.Add(_prompt, 0, 3);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 7, 0, 0),
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Canvas,
        };
        ConfigureButton(_sendButton, "发送", primary: true);
        _sendButton.Click += async (_, _) => await RunAgentAsync();
        ConfigureButton(_cancelButton, "停止", primary: false);
        _cancelButton.Enabled = false;
        _cancelButton.Click += (_, _) => _cancellation?.Cancel();
        ConfigureButton(_applyButton, "应用到文档", primary: false);
        _applyButton.Enabled = false;
        _applyButton.Click += async (_, _) => await ApplyLastAnswerAsync();
        actions.Controls.Add(_sendButton);
        actions.Controls.Add(_cancelButton);
        actions.Controls.Add(_applyButton);
        layout.Controls.Add(actions, 0, 4);
        page.Controls.Add(layout);
        return page;
    }

    private Control BuildQuickActions()
    {
        var quick = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Canvas,
        };
        quick.Controls.Add(CreatePromptButton("补写", "根据任务要求和现有资料，补写当前文档最欠缺的部分。"));
        quick.Controls.Add(CreatePromptButton("找证据", "检查当前文档的重要结论，为缺少来源的内容寻找证据。"));
        quick.Controls.Add(CreatePromptButton("评委审查", "从评委视角审查当前文档，指出最影响评分的问题。"));
        quick.Controls.Add(CreatePromptButton("一致性", "检查产品名称、术语、日期、版本和关键数字是否前后一致。"));
        return quick;
    }

    private Panel BuildProjectPage()
    {
        var page = new Panel { Dock = DockStyle.Fill, BackColor = Canvas, Padding = new Padding(12, 10, 12, 12) };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = Canvas,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Canvas,
        };
        var importRequirements = CreateButton("导入要求", primary: true);
        importRequirements.Click += async (_, _) => await ImportRequirementsAsync();
        var importSources = CreateButton("添加资料", primary: false);
        importSources.Click += async (_, _) => await ImportSourcesAsync();
        actions.Controls.Add(importRequirements);
        actions.Controls.Add(importSources);
        layout.Controls.Add(actions, 0, 0);
        _projectSummary.Dock = DockStyle.Fill;
        _projectSummary.ForeColor = Muted;
        _projectSummary.AutoEllipsis = true;
        layout.Controls.Add(_projectSummary, 0, 1);
        ConfigureList(_requirementList);
        _requirementList.Columns.Add("要求", 220);
        _requirementList.Columns.Add("状态", 80);
        layout.Controls.Add(_requirementList, 0, 2);
        layout.Controls.Add(SectionLabel("资料"), 0, 3);
        ConfigureList(_sourceList);
        _sourceList.Columns.Add("资料", 170);
        _sourceList.Columns.Add("索引状态", 180);
        layout.Controls.Add(_sourceList, 0, 4);
        page.Controls.Add(layout);
        return page;
    }

    private Panel BuildCheckPage()
    {
        var page = new Panel { Dock = DockStyle.Fill, BackColor = Canvas, Padding = new Padding(12, 10, 12, 12) };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Canvas,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var run = CreateButton("重新体检", primary: true);
        run.Click += async (_, _) => await RunDocumentCheckAsync();
        var export = CreateButton("导出配套材料", primary: false);
        export.Click += async (_, _) => await ExportCompanionFilesAsync();
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Canvas };
        toolbar.Controls.Add(run);
        toolbar.Controls.Add(export);
        layout.Controls.Add(toolbar, 0, 0);
        _checkSummary.Dock = DockStyle.Fill;
        _checkSummary.Padding = new Padding(10, 8, 10, 8);
        _checkSummary.BackColor = AccentSoft;
        _checkSummary.ForeColor = Ink;
        _checkSummary.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        layout.Controls.Add(_checkSummary, 0, 1);
        ConfigureList(_checkList);
        _checkList.Columns.Add("状态", 82);
        _checkList.Columns.Add("问题", 190);
        _checkList.Columns.Add("说明", 260);
        layout.Controls.Add(_checkList, 0, 2);
        page.Controls.Add(layout);
        return page;
    }

    private async Task RunAgentAsync()
    {
        if (string.IsNullOrWhiteSpace(_prompt.Text))
        {
            _agentStatus.Text = "先输入你要完成的任务";
            _prompt.Focus();
            return;
        }
        try
        {
            await EnsureProjectAsync();
            var markdown = await _getCurrentMarkdown();
            if (string.IsNullOrWhiteSpace(markdown) && _project.Sources.Count == 0)
                throw new InvalidOperationException("请先打开一份文档，或在“项目上下文”中添加资料。");
            if (ShouldConfirmCloud() && MessageBox.Show(
                    FindForm(),
                    "这次任务会把命中的资料片段和你的要求发送到所配置的远程模型。是否继续？",
                    "确认发送",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            var request = _prompt.Text.Trim();
            AppendMessage("你", request, Ink);
            _cancellation = new CancellationTokenSource();
            SetBusy(true, "正在查找相关资料");
            _lastSources = ProofSourceQueryService.BuildSources(
                _project,
                markdown,
                Path.GetFileName(_getDocumentPath()) ?? "当前文档",
                request,
                _settings.MaxSources);
            if (_lastSources.Count == 0) throw new InvalidOperationException("没有找到可供 Agent 使用的文本资料。");
            _agentStatus.Text = $"已找到 {_lastSources.Count} 个相关片段，正在调用 {_settings.Model}";
            _lastAnswer = await _client.CompleteAsync(
                _settings.Endpoint,
                _settings.Model,
                _sessionApiKey,
                "分析任务并提出可直接审阅的 Markdown 修改建议",
                request,
                _lastSources,
                _cancellation.Token);
            AppendMessage("Agent", _lastAnswer, Accent);
            _applyButton.Enabled = true;
            _prompt.Clear();
            _agentStatus.Text = $"完成，使用 {_lastSources.Count} 个来源片段。应用前请核对。";
            AddAudit("Agent 生成建议", request, false);
            await SaveProjectAsync();
        }
        catch (OperationCanceledException)
        {
            _agentStatus.Text = "任务已停止，没有修改文档";
        }
        catch (Exception exception)
        {
            AppendMessage("系统", exception.Message, Danger);
            _agentStatus.Text = "任务没有完成";
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
            SetBusy(false, _agentStatus.Text);
        }
    }

    private async Task ApplyLastAnswerAsync()
    {
        if (string.IsNullOrWhiteSpace(_lastAnswer)) return;
        if (MessageBox.Show(
                FindForm(),
                "确认已经核对建议和来源，并插入到当前光标位置？",
                "应用 Agent 建议",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        var portable = PortableCitationService.ConvertAgentSourcesToFootnotes(_lastAnswer, _lastSources);
        _insertMarkdown(portable);
        AddAudit("人工采纳 Agent 建议", $"写入 {portable.Length} 个字符", true);
        await SaveProjectAsync();
        _agentStatus.Text = "已应用到文档，可以使用撤销恢复";
        _applyButton.Enabled = false;
    }

    private async Task ImportRequirementsAsync()
    {
        var root = ResolveProjectRoot();
        if (root is null)
        {
            ShowOpenProjectMessage();
            return;
        }
        using var dialog = new OpenFileDialog
        {
            Title = "选择任务要求或评分标准",
            Filter = "支持的文档|*.md;*.markdown;*.txt;*.docx;*.pdf|所有文件|*.*",
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            await EnsureProjectAsync();
            var (text, status) = await SourceTextExtractor.ExtractAsync(dialog.FileName);
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException(status);
            var existing = _project.Requirements.Select(item => item.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var additions = RequirementAnalyzer.Analyze(text).Where(item => existing.Add(item.Title)).ToArray();
            _project.Requirements.AddRange(additions);
            AddAudit("导入任务要求", $"从 {Path.GetFileName(dialog.FileName)} 识别 {additions.Length} 项", true);
            await SaveProjectAsync();
            await RunDocumentCheckAsync();
            RefreshProjectViews();
        }
        catch (Exception exception)
        {
            MessageBox.Show(FindForm(), exception.Message, "导入失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task ImportSourcesAsync()
    {
        if (ResolveProjectRoot() is null)
        {
            ShowOpenProjectMessage();
            return;
        }
        using var dialog = new OpenFileDialog
        {
            Title = "添加 Agent 可使用的资料",
            Filter = "资料文件|*.md;*.markdown;*.txt;*.docx;*.pdf;*.csv;*.tsv;*.json;*.png;*.jpg;*.jpeg;*.webp|所有文件|*.*",
            Multiselect = true,
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        await EnsureProjectAsync();
        foreach (var path in dialog.FileNames)
        {
            if (_project.Sources.Any(source => PathEquals(source.FilePath, path))) continue;
            try
            {
                var (text, status) = await SourceTextExtractor.ExtractAsync(path);
                var info = new FileInfo(path);
                _project.Sources.Add(new ProofSource
                {
                    FilePath = path,
                    DisplayName = info.Name,
                    Kind = SourceTextExtractor.DetectKind(path),
                    TrustLevel = GuessTrustLevel(path),
                    FileModifiedAtUtc = info.LastWriteTimeUtc,
                    ExtractedText = text,
                    ExtractionStatus = status,
                });
            }
            catch (Exception exception)
            {
                _project.Sources.Add(new ProofSource
                {
                    FilePath = path,
                    DisplayName = Path.GetFileName(path),
                    Kind = SourceTextExtractor.DetectKind(path),
                    ExtractionStatus = $"索引失败：{exception.Message}",
                });
            }
        }
        AddAudit("添加项目资料", $"资料中心现有 {_project.Sources.Count} 项", true);
        await SaveProjectAsync();
        RefreshProjectViews();
    }

    private async Task RunDocumentCheckAsync()
    {
        try
        {
            await EnsureProjectAsync();
            var markdown = await _getCurrentMarkdown();
            var root = ResolveProjectRoot() ?? Environment.CurrentDirectory;
            _lastCiResult = DocumentCiService.Analyze(markdown, _project, root);
            await SaveProjectAsync();
            RefreshCheckView();
            RefreshProjectViews();
        }
        catch (Exception exception)
        {
            _checkSummary.Text = exception.Message;
        }
    }

    private async Task ExportCompanionFilesAsync()
    {
        try
        {
            await EnsureProjectAsync();
            var root = ResolveProjectRoot();
            if (root is null)
            {
                ShowOpenProjectMessage();
                return;
            }
            var markdown = await _getCurrentMarkdown();
            _lastCiResult = DocumentCiService.Analyze(markdown, _project, root);
            var exportDirectory = Path.Combine(root, ".markleaf", "exports");
            var documentName = Path.GetFileName(_getDocumentPath()) ?? "当前文档";
            var files = await ProofReportBuilder.ExportPackageAsync(
                exportDirectory,
                _project,
                _lastCiResult,
                documentName);
            AddAudit("导出配套材料", $"生成 {files.Count} 个 Markdown 文件", true);
            await SaveProjectAsync();
            RefreshCheckView();
            if (MessageBox.Show(
                    FindForm(),
                    $"已生成 {files.Count} 个文件：\n{exportDirectory}\n\n是否打开文件夹？",
                    "导出完成",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information) == DialogResult.Yes)
            {
                Process.Start(new ProcessStartInfo("explorer.exe", exportDirectory) { UseShellExecute = true });
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(FindForm(), exception.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task EnsureProjectAsync()
    {
        var root = ResolveProjectRoot();
        if (root is null)
        {
            _loadedRoot = null;
            _project = new ProofProject { Title = "尚未打开项目" };
            return;
        }
        if (string.Equals(_loadedRoot, root, StringComparison.OrdinalIgnoreCase)) return;
        _project = await _store.LoadAsync(root);
        _loadedRoot = root;
    }

    private async Task SaveProjectAsync()
    {
        var root = ResolveProjectRoot();
        if (root is null) return;
        await _store.SaveAsync(root, _project);
    }

    private string? ResolveProjectRoot()
    {
        var workspace = _getWorkspaceRoot();
        if (!string.IsNullOrWhiteSpace(workspace) && Directory.Exists(workspace)) return workspace;
        var path = _getDocumentPath();
        var directory = string.IsNullOrWhiteSpace(path) ? null : Path.GetDirectoryName(path);
        return !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory) ? directory : null;
    }

    private void RefreshProjectViews()
    {
        UpdateHeader();
        _requirementList.Items.Clear();
        foreach (var requirement in _project.Requirements)
        {
            var item = new ListViewItem(requirement.Title);
            item.SubItems.Add(requirement.IsCovered ? "已覆盖" : "待补充");
            item.ForeColor = requirement.IsCovered ? Accent : Warning;
            _requirementList.Items.Add(item);
        }
        if (_project.Requirements.Count == 0)
            _requirementList.Items.Add(new ListViewItem(["尚未导入任务要求", ""]));

        _sourceList.Items.Clear();
        foreach (var source in _project.Sources)
        {
            var item = new ListViewItem(source.DisplayName);
            item.SubItems.Add(source.ExtractionStatus);
            item.ForeColor = string.IsNullOrWhiteSpace(source.ExtractedText) ? Warning : Ink;
            _sourceList.Items.Add(item);
        }
        if (_project.Sources.Count == 0)
            _sourceList.Items.Add(new ListViewItem(["尚未添加项目资料", ""]));
        _projectSummary.Text = $"{_project.Requirements.Count} 项要求，{_project.Sources.Count} 项资料，数据保存在项目 .markleaf 文件夹";
    }

    private void RefreshCheckView()
    {
        _checkList.Items.Clear();
        if (_lastCiResult is null)
        {
            _checkSummary.Text = "打开文档后运行体检";
            return;
        }
        _checkSummary.Text = $"要求覆盖 {_lastCiResult.CoveragePercent}%　证据覆盖 {_lastCiResult.EvidencePercent}%　需处理 {_lastCiResult.ErrorCount} 项";
        foreach (var issue in _lastCiResult.Issues.OrderBy(issue => issue.Severity))
        {
            var state = issue.Severity switch
            {
                ProofIssueSeverity.Error => "需处理",
                ProofIssueSeverity.Warning => "检查",
                ProofIssueSeverity.Passed => "通过",
                _ => "提示",
            };
            var item = new ListViewItem(state);
            item.SubItems.Add(issue.Title);
            item.SubItems.Add(issue.Detail);
            item.ForeColor = issue.Severity switch
            {
                ProofIssueSeverity.Error => Danger,
                ProofIssueSeverity.Warning => Warning,
                ProofIssueSeverity.Passed => Accent,
                _ => Muted,
            };
            _checkList.Items.Add(item);
        }
    }

    private void UpdateHeader()
    {
        var document = Path.GetFileName(_getDocumentPath());
        _contextLabel.Text = string.IsNullOrWhiteSpace(document)
            ? "打开文档后开始工作"
            : document;
        _modelLabel.Text = _settings.Model;
    }

    private void ShowPage(Control page, Button selectedButton)
    {
        _contentHost.SuspendLayout();
        _contentHost.Controls.Clear();
        _contentHost.Controls.Add(page);
        page.Dock = DockStyle.Fill;
        _contentHost.ResumeLayout();
        foreach (var button in new[] { _agentTab, _projectTab, _checkTab })
        {
            var selected = ReferenceEquals(button, selectedButton);
            button.BackColor = selected ? AccentSoft : Surface;
            button.ForeColor = selected ? Accent : Muted;
            button.Font = new Font("Segoe UI", 9F, selected ? FontStyle.Bold : FontStyle.Regular);
        }
    }

    private void AppendMessage(string role, string content, Color roleColor)
    {
        _conversation.SelectionStart = _conversation.TextLength;
        _conversation.SelectionLength = 0;
        _conversation.SelectionFont = new Font("Segoe UI", 9F, FontStyle.Bold);
        _conversation.SelectionColor = roleColor;
        _conversation.AppendText(role + "\n");
        _conversation.SelectionFont = new Font("Segoe UI", 9.5F, FontStyle.Regular);
        _conversation.SelectionColor = Ink;
        _conversation.AppendText(content.Trim() + "\n\n");
        _conversation.SelectionStart = _conversation.TextLength;
        _conversation.ScrollToCaret();
    }

    private void SetBusy(bool busy, string status)
    {
        _sendButton.Enabled = !busy;
        _cancelButton.Enabled = busy;
        _prompt.Enabled = !busy;
        _agentStatus.Text = status;
        UseWaitCursor = busy;
    }

    private Button CreatePromptButton(string text, string prompt)
    {
        var button = CreateButton(text, primary: false);
        button.Height = 28;
        button.MinimumSize = new Size(72, 28);
        button.Margin = new Padding(0, 0, 6, 6);
        button.Font = new Font("Segoe UI", 8.5F);
        button.Click += (_, _) =>
        {
            _prompt.Text = prompt;
            _prompt.Focus();
            _prompt.SelectionStart = _prompt.TextLength;
        };
        return button;
    }

    private static void ConfigureTab(Button button, string text, EventHandler handler)
    {
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(0, 0, 4, 0);
        button.Text = text;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.Cursor = Cursors.Hand;
        button.Click += handler;
    }

    private static void ConfigureButton(Button button, string text, bool primary)
    {
        button.Text = text;
        button.AutoSize = true;
        button.MinimumSize = new Size(82, 31);
        button.Height = 31;
        button.Padding = new Padding(10, 0, 10, 0);
        button.Margin = new Padding(6, 0, 0, 0);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.BorderColor = Border;
        button.BackColor = primary ? Accent : Surface;
        button.ForeColor = primary ? Color.White : Ink;
        button.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
    }

    private static Button CreateButton(string text, bool primary)
    {
        var button = new Button();
        ConfigureButton(button, text, primary);
        return button;
    }

    private static Button CreateTextButton(string text)
        => new()
        {
            AutoSize = true,
            Text = text,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Muted,
            BackColor = Surface,
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI", 8.5F),
            Margin = Padding.Empty,
            Padding = new Padding(5, 0, 5, 0),
        };

    private static Label SectionLabel(string text)
        => new()
        {
            Dock = DockStyle.Fill,
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Ink,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
        };

    private static void ConfigureList(ListView list)
    {
        list.Dock = DockStyle.Fill;
        list.View = View.Details;
        list.FullRowSelect = true;
        list.HideSelection = false;
        list.GridLines = false;
        list.BorderStyle = BorderStyle.FixedSingle;
        list.BackColor = Surface;
        list.ForeColor = Ink;
        list.Font = new Font("Segoe UI", 8.8F);
        list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
    }

    private void ShowAgentSettings()
    {
        using var dialog = new Form
        {
            Text = "Agent 设置",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            AutoScaleMode = AutoScaleMode.Dpi,
            ClientSize = new Size(520, 276),
            BackColor = Surface,
            Font = new Font("Segoe UI", 9F),
        };
        var endpoint = new TextBox { Text = _settings.Endpoint, Dock = DockStyle.Fill };
        var model = new TextBox { Text = _settings.Model, Dock = DockStyle.Fill };
        var apiKey = new TextBox { Text = _sessionApiKey, UseSystemPasswordChar = true, Dock = DockStyle.Fill };
        var confirm = new CheckBox { Text = "发送到非本地地址前再次确认", Checked = _settings.ConfirmBeforeCloud, AutoSize = true };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 2,
            RowCount = 5,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var index = 0; index < 4; index++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        AddSettingsRow(layout, 0, "API 地址", endpoint);
        AddSettingsRow(layout, 1, "模型", model);
        AddSettingsRow(layout, 2, "API Key", apiKey);
        layout.Controls.Add(confirm, 1, 3);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var save = CreateButton("保存", true);
        var cancel = CreateButton("取消", false);
        save.DialogResult = DialogResult.OK;
        cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 1, 4);
        dialog.Controls.Add(layout);
        dialog.AcceptButton = save;
        dialog.CancelButton = cancel;
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        _settings.Endpoint = endpoint.Text.Trim();
        _settings.Model = model.Text.Trim();
        _settings.ConfirmBeforeCloud = confirm.Checked;
        _sessionApiKey = apiKey.Text;
        _rememberApiKey(_sessionApiKey);
        _saveSettings();
        UpdateHeader();
    }

    private static void AddSettingsRow(TableLayoutPanel layout, int row, string label, Control control)
    {
        layout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = label,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Ink,
        }, 0, row);
        control.Margin = new Padding(0, 8, 0, 6);
        layout.Controls.Add(control, 1, row);
    }

    private bool ShouldConfirmCloud()
    {
        if (!_settings.ConfirmBeforeCloud) return false;
        if (!Uri.TryCreate(_settings.Endpoint, UriKind.Absolute, out var uri)) return true;
        return !uri.IsLoopback && !uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
    }

    private void ShowOpenProjectMessage()
        => MessageBox.Show(
            FindForm(),
            "请先打开一个工作区或 Markdown 文档。",
            "MarkLeaf Agent",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

    private void AddAudit(string action, string detail, bool humanConfirmed)
    {
        _project.AuditTrail.Add(new ProofAuditEvent
        {
            Action = action,
            Detail = detail,
            HumanConfirmed = humanConfirmed,
        });
        if (_project.AuditTrail.Count > 500)
            _project.AuditTrail.RemoveRange(0, _project.AuditTrail.Count - 500);
    }

    private static string GuessTrustLevel(string path)
    {
        var name = Path.GetFileName(path);
        return name.Contains("官方", StringComparison.OrdinalIgnoreCase)
            || name.Contains("标准", StringComparison.OrdinalIgnoreCase)
            || name.Contains("论文", StringComparison.OrdinalIgnoreCase)
            ? "高"
            : "一般";
    }

    private static bool PathEquals(string left, string right)
    {
        try { return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase); }
        catch { return string.Equals(left, right, StringComparison.OrdinalIgnoreCase); }
    }
}
