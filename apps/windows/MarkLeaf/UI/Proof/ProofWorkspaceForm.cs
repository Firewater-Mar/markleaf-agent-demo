using MarkLeaf.Services.AI;
using MarkLeaf.Services.Proof;
using MarkLeaf.Services.Settings;
using MarkLeaf.UI.Controls;
using MarkLeaf.UI.Dialogs;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MarkLeaf.UI.Proof;

internal sealed class ProofWorkspaceForm : Form
{
    private readonly string _workspaceRoot;
    private readonly string? _currentDocumentPath;
    private readonly Func<Task<string>> _getCurrentMarkdown;
    private readonly Action<string> _insertMarkdown;
    private readonly AiSettings _settings;
    private readonly Action<string> _rememberApiKey;
    private readonly Action _saveSettings;
    private readonly ProofProjectStore _store = new();
    private readonly OpenAiCompatibleClient _client = new();
    private readonly Dictionary<string, Control> _pages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ProofNavButton> _navigation = new(StringComparer.Ordinal);
    private readonly Panel _pageHost = new() { Dock = DockStyle.Fill, BackColor = ProofPalette.Canvas };
    private readonly Label _pageTitle = new();
    private readonly Label _pageSubtitle = new();
    private readonly Label _saveState = new();

    private readonly ProofStatCard _requirementStat = new("要求覆盖");
    private readonly ProofStatCard _evidenceStat = new("证据覆盖");
    private readonly ProofStatCard _sourceStat = new("资料索引");
    private readonly ProofStatCard _issueStat = new("待处理问题");
    private readonly ProofProgressRing _healthRing = new();
    private readonly Label _healthSummary = new();
    private readonly FlowLayoutPanel _dashboardIssues = new();

    private readonly DataGridView _requirementsGrid = new();
    private readonly Label _requirementsSummary = new();
    private readonly DataGridView _sourcesGrid = new();
    private readonly Label _sourcesSummary = new();
    private readonly ListView _ciList = new();
    private readonly ListView _claimList = new();
    private readonly Label _ciSummary = new();

    private readonly ComboBox _agentTask = new();
    private readonly TextBox _agentRequest = new();
    private readonly TextBox _agentOutput = new();
    private readonly ListBox _agentSources = new();
    private readonly Label _agentStatus = new();
    private readonly Button _runAgentButton = new();
    private readonly Button _stopAgentButton = new();
    private readonly Button _insertAgentButton = new();
    private CancellationTokenSource? _agentCancellation;
    private IReadOnlyList<AiSource> _lastAgentSources = [];

    private readonly ListBox _deliveryFiles = new();
    private readonly Label _deliveryStatus = new();
    private readonly TextBox _endpoint = new();
    private readonly TextBox _model = new();
    private readonly TextBox _apiKey = new();
    private readonly ComboBox _privacyMode = new();
    private readonly CheckBox _confirmCloud = new();

    private ProofProject _project = new();
    private ProofCiResult? _lastResult;

    public ProofWorkspaceForm(
        string workspaceRoot,
        string? currentDocumentPath,
        Func<Task<string>> getCurrentMarkdown,
        Action<string> insertMarkdown,
        AiSettings settings,
        string sessionApiKey,
        Action<string> rememberApiKey,
        Action saveSettings)
    {
        _workspaceRoot = workspaceRoot;
        _currentDocumentPath = currentDocumentPath;
        _getCurrentMarkdown = getCurrentMarkdown;
        _insertMarkdown = insertMarkdown;
        _settings = settings;
        _rememberApiKey = rememberApiKey;
        _saveSettings = saveSettings;
        _apiKey.Text = sessionApiKey;

        Text = "MarkLeaf Proof";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1040, 700);
        Size = new Size(1280, 820);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = ProofPalette.Canvas;
        Font = new Font("Segoe UI Variable Text", 9F);
        MaximizeBox = true;
        MinimizeBox = true;

        ConfigureDataGrids();
        BuildPages();
        Controls.Add(BuildShell());
        Shown += async (_, _) => await InitializeProjectAsync();
        FormClosing += (_, _) => _agentCancellation?.Cancel();
    }

    private Control BuildShell()
    {
        var content = new Panel { Dock = DockStyle.Fill, BackColor = ProofPalette.Canvas };
        content.Controls.Add(_pageHost);
        content.Controls.Add(BuildHeader());
        var shell = new Panel { Dock = DockStyle.Fill, BackColor = ProofPalette.Canvas };
        shell.Controls.Add(content);
        shell.Controls.Add(BuildSidebar());
        return shell;
    }

    private Control BuildSidebar()
    {
        var sidebar = new Panel
        {
            Dock = DockStyle.Left,
            Width = this.ScaleForDpi(210),
            BackColor = ProofPalette.Sidebar,
            Padding = new Padding(12, 16, 12, 12),
        };
        var brand = new Panel { Dock = DockStyle.Top, Height = 74, BackColor = ProofPalette.Sidebar };
        var mark = new Label
        {
            Text = "M",
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = ProofPalette.Accent,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Variable Display", 13F, FontStyle.Bold),
            Size = new Size(36, 36),
            Location = new Point(5, 5),
        };
        var title = new Label
        {
            Text = "MarkLeaf Proof",
            ForeColor = ProofPalette.Ink,
            Font = new Font("Segoe UI Variable Display", 11.5F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(50, 4),
        };
        var caption = new Label
        {
            Text = "可信文档工作台",
            ForeColor = ProofPalette.Muted,
            Font = new Font("Segoe UI Variable Text", 8.5F),
            AutoSize = true,
            Location = new Point(50, 29),
        };
        brand.Controls.Add(mark);
        brand.Controls.Add(title);
        brand.Controls.Add(caption);

        var nav = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = ProofPalette.Sidebar,
            Padding = new Padding(0, 8, 0, 0),
        };
        AddNavigation(nav, "overview", "概览", "项目健康度与下一步");
        AddNavigation(nav, "requirements", "任务要求", "检查必交项和评分项");
        AddNavigation(nav, "sources", "资料中心", "管理可追溯来源");
        AddNavigation(nav, "ci", "文档体检", "结构、引用与一致性");
        AddNavigation(nav, "agent", "文档 Agent", "基于资料提出修改");
        AddNavigation(nav, "delivery", "交付中心", "报告、答辩与调研材料");
        AddNavigation(nav, "settings", "AI 与隐私", "本地或云端模型设置");

        var privacy = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 54,
            Text = "项目数据保存在工作区\n.markleaf 文件夹中",
            ForeColor = ProofPalette.Muted,
            Font = new Font("Segoe UI Variable Text", 8F),
            Padding = new Padding(6, 10, 0, 0),
        };
        sidebar.Controls.Add(nav);
        sidebar.Controls.Add(privacy);
        sidebar.Controls.Add(brand);
        return sidebar;
    }

    private void AddNavigation(Control parent, string id, string text, string subtitle)
    {
        var button = new ProofNavButton(text)
        {
            Width = this.ScaleForDpi(184),
            AccessibleDescription = subtitle,
            Margin = new Padding(0, 0, 0, 4),
        };
        button.Click += (_, _) => ShowPage(id);
        _navigation[id] = button;
        parent.Controls.Add(button);
    }

    private Control BuildHeader()
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = this.ScaleForDpi(92),
            BackColor = ProofPalette.Canvas,
            Padding = new Padding(30, 17, 30, 8),
        };
        _pageTitle.Dock = DockStyle.Top;
        _pageTitle.Height = 35;
        _pageTitle.Font = new Font("Segoe UI Variable Display", 18F, FontStyle.Bold);
        _pageTitle.ForeColor = ProofPalette.Ink;
        _pageSubtitle.Dock = DockStyle.Top;
        _pageSubtitle.Height = 26;
        _pageSubtitle.Font = new Font("Segoe UI Variable Text", 9.5F);
        _pageSubtitle.ForeColor = ProofPalette.Muted;
        _saveState.Dock = DockStyle.Right;
        _saveState.Width = 160;
        _saveState.Text = "本地项目";
        _saveState.TextAlign = ContentAlignment.TopRight;
        _saveState.ForeColor = ProofPalette.Muted;
        header.Controls.Add(_saveState);
        header.Controls.Add(_pageSubtitle);
        header.Controls.Add(_pageTitle);
        return header;
    }

    private void BuildPages()
    {
        _pages["overview"] = BuildOverviewPage();
        _pages["requirements"] = BuildRequirementsPage();
        _pages["sources"] = BuildSourcesPage();
        _pages["ci"] = BuildCiPage();
        _pages["agent"] = BuildAgentPage();
        _pages["delivery"] = BuildDeliveryPage();
        _pages["settings"] = BuildSettingsPage();
    }

    private Control BuildOverviewPage()
    {
        var page = CreateScrollablePage();
        var stats = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 132,
            ColumnCount = 4,
            BackColor = ProofPalette.Canvas,
        };
        for (var index = 0; index < 4; index++) stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        stats.Controls.Add(_requirementStat, 0, 0);
        stats.Controls.Add(_evidenceStat, 1, 0);
        stats.Controls.Add(_sourceStat, 2, 0);
        stats.Controls.Add(_issueStat, 3, 0);
        foreach (Control control in stats.Controls) control.Dock = DockStyle.Fill;

        var health = new ProofCard { Dock = DockStyle.Top, Height = 146 };
        _healthRing.Location = new Point(22, 34);
        _healthSummary.Location = new Point(116, 32);
        _healthSummary.Size = new Size(680, 82);
        _healthSummary.Font = new Font("Segoe UI Variable Text", 10.5F);
        _healthSummary.ForeColor = ProofPalette.Ink;
        health.Controls.Add(_healthRing);
        health.Controls.Add(_healthSummary);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 58,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = ProofPalette.Canvas,
            Padding = new Padding(0, 6, 0, 6),
        };
        actions.Controls.Add(CreatePrimaryButton("运行文档体检", async (_, _) => await RunCiAsync(showPage: true)));
        actions.Controls.Add(CreateSecondaryButton("导入任务要求", (_, _) => ShowPage("requirements")));
        actions.Controls.Add(CreateSecondaryButton("打开文档 Agent", (_, _) => ShowPage("agent")));

        var issuesTitle = SectionTitle("优先处理");
        _dashboardIssues.Dock = DockStyle.Top;
        _dashboardIssues.AutoSize = true;
        _dashboardIssues.FlowDirection = FlowDirection.TopDown;
        _dashboardIssues.WrapContents = false;
        _dashboardIssues.BackColor = ProofPalette.Canvas;

        page.Controls.Add(_dashboardIssues);
        page.Controls.Add(issuesTitle);
        page.Controls.Add(actions);
        page.Controls.Add(health);
        page.Controls.Add(stats);
        return page;
    }

    private Control BuildRequirementsPage()
    {
        var page = new Panel { Dock = DockStyle.Fill, BackColor = ProofPalette.Canvas, Padding = new Padding(30, 0, 30, 24) };
        var toolbar = BuildToolbar(
            CreatePrimaryButton("导入要求文件", async (_, _) => await ImportRequirementsAsync()),
            CreateSecondaryButton("从剪贴板识别", async (_, _) => await ImportRequirementsFromClipboardAsync()),
            CreateSecondaryButton("手动添加", async (_, _) => await AddRequirementAsync()),
            CreateDangerButton("移除所选", async (_, _) => await RemoveSelectedRequirementAsync()));
        _requirementsSummary.Dock = DockStyle.Bottom;
        _requirementsSummary.Height = 34;
        _requirementsSummary.ForeColor = ProofPalette.Muted;
        _requirementsSummary.TextAlign = ContentAlignment.MiddleLeft;
        page.Controls.Add(_requirementsGrid);
        page.Controls.Add(_requirementsSummary);
        page.Controls.Add(toolbar);
        return page;
    }

    private Control BuildSourcesPage()
    {
        var page = new Panel { Dock = DockStyle.Fill, BackColor = ProofPalette.Canvas, Padding = new Padding(30, 0, 30, 24) };
        var toolbar = BuildToolbar(
            CreatePrimaryButton("添加资料", async (_, _) => await ImportSourcesAsync()),
            CreateSecondaryButton("重新建立索引", async (_, _) => await ReindexSourcesAsync()),
            CreateDangerButton("移除所选", async (_, _) => await RemoveSelectedSourceAsync()));
        _sourcesSummary.Dock = DockStyle.Bottom;
        _sourcesSummary.Height = 34;
        _sourcesSummary.ForeColor = ProofPalette.Muted;
        _sourcesSummary.TextAlign = ContentAlignment.MiddleLeft;
        page.Controls.Add(_sourcesGrid);
        page.Controls.Add(_sourcesSummary);
        page.Controls.Add(toolbar);
        return page;
    }

    private Control BuildCiPage()
    {
        var page = new Panel { Dock = DockStyle.Fill, BackColor = ProofPalette.Canvas, Padding = new Padding(30, 0, 30, 24) };
        var toolbar = BuildToolbar(CreatePrimaryButton("重新检查", async (_, _) => await RunCiAsync(showPage: false)));
        _ciSummary.Dock = DockStyle.Top;
        _ciSummary.Height = 52;
        _ciSummary.Padding = new Padding(12, 8, 0, 0);
        _ciSummary.BackColor = ProofPalette.AccentSoft;
        _ciSummary.ForeColor = ProofPalette.Ink;
        _ciSummary.Font = new Font("Segoe UI Variable Text", 10F, FontStyle.Bold);
        _ciList.Dock = DockStyle.Fill;
        _ciList.View = View.Details;
        _ciList.FullRowSelect = true;
        _ciList.GridLines = false;
        _ciList.BorderStyle = BorderStyle.FixedSingle;
        _ciList.BackColor = ProofPalette.Surface;
        _ciList.ForeColor = ProofPalette.Ink;
        _ciList.Columns.Add("状态", 90);
        _ciList.Columns.Add("类别", 100);
        _ciList.Columns.Add("问题", 260);
        _ciList.Columns.Add("说明", 520);
        _claimList.Dock = DockStyle.Fill;
        _claimList.View = View.Details;
        _claimList.FullRowSelect = true;
        _claimList.GridLines = false;
        _claimList.BorderStyle = BorderStyle.FixedSingle;
        _claimList.BackColor = ProofPalette.Surface;
        _claimList.ForeColor = ProofPalette.Ink;
        _claimList.Columns.Add("证据状态", 100);
        _claimList.Columns.Add("行号", 64);
        _claimList.Columns.Add("结论", 600);
        _claimList.Columns.Add("来源标记", 170);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var issueTab = new TabPage("检查结果") { BackColor = ProofPalette.Surface };
        var claimTab = new TabPage("结论与证据") { BackColor = ProofPalette.Surface };
        issueTab.Controls.Add(_ciList);
        claimTab.Controls.Add(_claimList);
        tabs.TabPages.Add(issueTab);
        tabs.TabPages.Add(claimTab);
        page.Controls.Add(tabs);
        page.Controls.Add(_ciSummary);
        page.Controls.Add(toolbar);
        return page;
    }

    private Control BuildAgentPage()
    {
        var page = new Panel { Dock = DockStyle.Fill, BackColor = ProofPalette.Canvas, Padding = new Padding(30, 0, 30, 24) };
        var body = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 520,
            BackColor = ProofPalette.Border,
        };
        body.Panel1.BackColor = ProofPalette.Canvas;
        body.Panel2.BackColor = ProofPalette.Canvas;
        body.Panel1.Padding = new Padding(0, 0, 10, 0);
        body.Panel2.Padding = new Padding(10, 0, 0, 0);
        body.Panel1.Controls.Add(BuildAgentComposer());
        body.Panel2.Controls.Add(BuildAgentResult());
        page.Controls.Add(body);
        return page;
    }

    private Control BuildAgentComposer()
    {
        _agentTask.DropDownStyle = ComboBoxStyle.DropDownList;
        _agentTask.Items.AddRange([
            "按要求补写内容",
            "为结论寻找证据",
            "评委视角审查",
            "识别缺失材料",
            "术语与数字一致性检查",
            "整理大纲和答辩要点",
        ]);
        _agentTask.SelectedIndex = 0;
        _agentTask.Dock = DockStyle.Top;
        _agentTask.Height = 34;
        _agentRequest.Dock = DockStyle.Fill;
        _agentRequest.Multiline = true;
        _agentRequest.ScrollBars = ScrollBars.Vertical;
        _agentRequest.PlaceholderText = "描述你要完成的任务。Agent 只能使用当前文档和资料中心中已索引的内容。";
        _agentRequest.Font = new Font("Segoe UI Variable Text", 10F);
        _agentRequest.BackColor = ProofPalette.Surface;
        _agentRequest.ForeColor = ProofPalette.Ink;

        var quick = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 74,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = ProofPalette.Canvas,
            Padding = new Padding(0, 6, 0, 6),
        };
        quick.Controls.Add(CreateCompactButton("补齐应用价值", "根据任务要求和现有资料，补写应用价值部分。明确目标用户、真实问题、使用场景和可验证收益。"));
        quick.Controls.Add(CreateCompactButton("检查关键数字", "检查文档中的关键数字，指出来源充分、来源不足或相互冲突的内容。"));
        quick.Controls.Add(CreateCompactButton("模拟评委追问", "从评委视角提出最可能影响评分的问题，并按优先级给出改进建议。"));
        quick.Controls.Add(CreateCompactButton("生成 Mermaid", "根据现有文档和资料生成一个能表达核心流程的 Mermaid 图，并说明图中每个节点的依据。"));

        _runAgentButton.Text = "生成修改建议";
        StyleButton(_runAgentButton, primary: true);
        _runAgentButton.Click += async (_, _) => await RunAgentAsync();
        _stopAgentButton.Text = "停止";
        StyleButton(_stopAgentButton, primary: false);
        _stopAgentButton.Enabled = false;
        _stopAgentButton.Click += (_, _) => _agentCancellation?.Cancel();
        var actions = BuildToolbar(_runAgentButton, _stopAgentButton);
        _agentStatus.Dock = DockStyle.Bottom;
        _agentStatus.Height = 46;
        _agentStatus.ForeColor = ProofPalette.Muted;
        _agentStatus.Text = "每次生成都会显示使用的来源，写入正文前需要人工确认。";
        _agentStatus.Padding = new Padding(0, 8, 0, 0);

        var panel = new Panel { Dock = DockStyle.Fill, BackColor = ProofPalette.Canvas };
        var requestHost = new ProofCard { Dock = DockStyle.Fill, Padding = new Padding(14) };
        requestHost.Controls.Add(_agentRequest);
        panel.Controls.Add(requestHost);
        panel.Controls.Add(_agentStatus);
        panel.Controls.Add(actions);
        panel.Controls.Add(quick);
        panel.Controls.Add(FieldLabel("任务类型"));
        panel.Controls.Add(_agentTask);
        return panel;
    }

    private Control BuildAgentResult()
    {
        _agentOutput.Dock = DockStyle.Fill;
        _agentOutput.Multiline = true;
        _agentOutput.ReadOnly = true;
        _agentOutput.ScrollBars = ScrollBars.Both;
        _agentOutput.Font = new Font("Cascadia Mono", 9.5F);
        _agentOutput.BackColor = ProofPalette.Surface;
        _agentOutput.ForeColor = ProofPalette.Ink;
        _agentOutput.PlaceholderText = "生成结果会显示在这里。";
        _agentSources.Dock = DockStyle.Bottom;
        _agentSources.Height = 122;
        _agentSources.BackColor = ProofPalette.Surface;
        _agentSources.ForeColor = ProofPalette.Muted;
        _insertAgentButton.Text = "确认并插入正文";
        StyleButton(_insertAgentButton, primary: true);
        _insertAgentButton.Enabled = false;
        _insertAgentButton.Click += async (_, _) => await InsertAgentResultAsync();
        var copy = CreateSecondaryButton("复制建议", (_, _) => CopyAgentResult());
        var actions = BuildToolbar(_insertAgentButton, copy);
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = ProofPalette.Canvas };
        panel.Controls.Add(_agentOutput);
        panel.Controls.Add(FieldLabel("本次使用的来源", DockStyle.Bottom, 30));
        panel.Controls.Add(_agentSources);
        panel.Controls.Add(actions);
        panel.Controls.Add(FieldLabel("建议预览"));
        return panel;
    }

    private Control BuildDeliveryPage()
    {
        var page = CreateScrollablePage();
        var intro = new ProofCard { Dock = DockStyle.Top, Height = 126 };
        intro.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "从同一份 Markdown 和 Proof 项目数据生成配套材料。所有导出文件均可继续编辑，不会替你提交或发送。",
            ForeColor = ProofPalette.Ink,
            Font = new Font("Segoe UI Variable Text", 10.5F),
            Padding = new Padding(0, 12, 0, 0),
        });
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 58,
            BackColor = ProofPalette.Canvas,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 6, 0, 6),
        };
        actions.Controls.Add(CreatePrimaryButton("生成完整交付包", async (_, _) => await ExportDeliveryPackageAsync()));
        actions.Controls.Add(CreateSecondaryButton("打开导出文件夹", (_, _) => OpenExportDirectory()));
        _deliveryStatus.Dock = DockStyle.Top;
        _deliveryStatus.Height = 42;
        _deliveryStatus.ForeColor = ProofPalette.Muted;
        _deliveryStatus.Text = "交付包包含：文档体检报告、AI 使用说明、答辩提纲和调研工具包。";
        _deliveryFiles.Dock = DockStyle.Top;
        _deliveryFiles.Height = 230;
        _deliveryFiles.BackColor = ProofPalette.Surface;
        _deliveryFiles.ForeColor = ProofPalette.Ink;
        _deliveryFiles.DoubleClick += (_, _) => OpenSelectedDeliveryFile();
        page.Controls.Add(_deliveryFiles);
        page.Controls.Add(_deliveryStatus);
        page.Controls.Add(actions);
        page.Controls.Add(intro);
        return page;
    }

    private Control BuildSettingsPage()
    {
        var page = CreateScrollablePage();
        var card = new ProofCard { Dock = DockStyle.Top, Height = 390, Padding = new Padding(22) };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _endpoint.Text = _settings.Endpoint;
        _model.Text = _settings.Model;
        _apiKey.UseSystemPasswordChar = true;
        _apiKey.PlaceholderText = "本地 Ollama 可留空，密钥只保留到应用退出";
        _privacyMode.DropDownStyle = ComboBoxStyle.DropDownList;
        _privacyMode.Items.AddRange(["本地模式", "混合模式", "云端模式"]);
        _privacyMode.SelectedItem = _settings.PrivacyMode switch
        {
            "cloud" => "云端模式",
            "hybrid" => "混合模式",
            _ => "本地模式",
        };
        _confirmCloud.Text = "发送到非本地地址前再次确认";
        _confirmCloud.Checked = _settings.ConfirmBeforeCloud;
        _confirmCloud.AutoSize = true;
        AddSettingRow(layout, 0, "API 地址", _endpoint);
        AddSettingRow(layout, 1, "模型", _model);
        AddSettingRow(layout, 2, "API Key", _apiKey);
        AddSettingRow(layout, 3, "隐私模式", _privacyMode);
        AddSettingRow(layout, 4, "发送确认", _confirmCloud);
        var note = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(680, 0),
            Text = "本地模式默认使用 localhost。混合或云端模式会把你的任务、命中的资料片段和当前文档相关内容发送给所配置的模型服务。MarkLeaf 不会自动上传整个工作区。",
            ForeColor = ProofPalette.Muted,
            Padding = new Padding(0, 8, 0, 0),
        };
        layout.Controls.Add(note, 1, 5);
        card.Controls.Add(layout);
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 62,
            BackColor = ProofPalette.Canvas,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 8, 0, 8),
        };
        buttons.Controls.Add(CreatePrimaryButton("保存设置", (_, _) => SaveAiSettings()));
        page.Controls.Add(buttons);
        page.Controls.Add(card);
        return page;
    }

    private async Task InitializeProjectAsync()
    {
        try
        {
            _saveState.Text = "正在读取项目…";
            _project = await _store.LoadAsync(_workspaceRoot);
            RefreshRequirementsGrid();
            RefreshSourcesGrid();
            await RunCiAsync(showPage: false, changePage: false);
            ShowPage("overview");
            _saveState.Text = "已保存到本地";
        }
        catch (Exception exception)
        {
            _saveState.Text = "项目读取失败";
            MessageBox.Show(this, exception.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            ShowPage("overview");
        }
    }

    private void ShowPage(string id)
    {
        if (!_pages.TryGetValue(id, out var page)) return;
        _pageHost.SuspendLayout();
        _pageHost.Controls.Clear();
        _pageHost.Controls.Add(page);
        page.Dock = DockStyle.Fill;
        _pageHost.ResumeLayout();
        foreach (var item in _navigation) item.Value.Selected = item.Key == id;
        (_pageTitle.Text, _pageSubtitle.Text) = id switch
        {
            "overview" => (_project.Title, "查看文档健康度，并从最重要的问题开始。"),
            "requirements" => ("任务要求", "导入评分标准、作业要求、期刊规范或公司模板。"),
            "sources" => ("资料中心", "每条重要结论都应该能回到原始资料。"),
            "ci" => ("文档体检", "检查要求覆盖、证据、结构、本地资源和引用。"),
            "agent" => ("文档 Agent", "Agent 提出有来源的修改建议，最终写入由你确认。"),
            "delivery" => ("交付中心", "生成可编辑的报告、答辩材料和透明度说明。"),
            "settings" => ("AI 与隐私", "明确模型在哪里运行，以及哪些内容会被发送。"),
            _ => ("MarkLeaf Proof", "可信文档工作台"),
        };
    }

    private async Task ImportRequirementsAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择任务要求、评分标准或模板",
            Filter = "支持的文档|*.md;*.markdown;*.txt;*.docx;*.pdf|所有文件|*.*",
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _saveState.Text = "正在识别要求…";
            var (text, status) = await SourceTextExtractor.ExtractAsync(dialog.FileName);
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException(status);
            await MergeRequirementsAsync(RequirementAnalyzer.Analyze(text), Path.GetFileName(dialog.FileName));
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "导入要求失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task ImportRequirementsFromClipboardAsync()
    {
        string text;
        try { text = Clipboard.GetText(); }
        catch (Exception exception) when (exception is ExternalException or ThreadStateException)
        {
            MessageBox.Show(this, "暂时无法读取剪贴板，请稍后重试。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(text))
        {
            MessageBox.Show(this, "剪贴板中没有可识别的文字。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        await MergeRequirementsAsync(RequirementAnalyzer.Analyze(text), "剪贴板");
    }

    private async Task MergeRequirementsAsync(IReadOnlyList<ProofRequirement> requirements, string sourceName)
    {
        var existing = _project.Requirements.Select(item => item.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var additions = requirements.Where(item => existing.Add(item.Title)).ToArray();
        if (additions.Length == 0)
        {
            MessageBox.Show(this, "没有发现新的任务要求。可以手动添加或换一份文件。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        _project.Requirements.AddRange(additions);
        AddAudit("导入任务要求", $"从“{sourceName}”识别并确认 {additions.Length} 项要求", true);
        await SaveProjectAsync();
        RefreshRequirementsGrid();
        await RunCiAsync(showPage: false, changePage: false);
        _saveState.Text = $"已加入 {additions.Length} 项要求";
    }

    private async Task AddRequirementAsync()
    {
        using var dialog = new TextInputDialog("添加任务要求", "要求名称：");
        if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.InputText)) return;
        _project.Requirements.Add(new ProofRequirement { Title = dialog.InputText, Description = dialog.InputText });
        AddAudit("添加任务要求", dialog.InputText, true);
        await SaveProjectAsync();
        RefreshRequirementsGrid();
    }

    private async Task RemoveSelectedRequirementAsync()
    {
        if (_requirementsGrid.CurrentRow?.Tag is not ProofRequirement requirement) return;
        _project.Requirements.Remove(requirement);
        AddAudit("移除任务要求", requirement.Title, true);
        await SaveProjectAsync();
        RefreshRequirementsGrid();
        await RunCiAsync(showPage: false, changePage: false);
    }

    private async Task ImportSourcesAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择项目资料",
            Filter = "资料文件|*.md;*.markdown;*.txt;*.docx;*.pdf;*.csv;*.tsv;*.json;*.png;*.jpg;*.jpeg;*.webp|所有文件|*.*",
            Multiselect = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var imported = 0;
        foreach (var path in dialog.FileNames)
        {
            if (_project.Sources.Any(source => PathEquals(source.FilePath, path))) continue;
            try
            {
                _saveState.Text = $"正在索引 {Path.GetFileName(path)}…";
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
                imported++;
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
        if (imported > 0) AddAudit("导入资料", $"新增 {imported} 个资料文件", true);
        await SaveProjectAsync();
        RefreshSourcesGrid();
        RefreshDashboard();
        _saveState.Text = $"资料中心已更新，共新增 {imported} 项";
    }

    private async Task ReindexSourcesAsync()
    {
        var targets = _sourcesGrid.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(row => row.Tag)
            .OfType<ProofSource>()
            .ToArray();
        if (targets.Length == 0) targets = _project.Sources.ToArray();
        foreach (var source in targets)
        {
            try
            {
                var (text, status) = await SourceTextExtractor.ExtractAsync(source.FilePath);
                source.ExtractedText = text;
                source.ExtractionStatus = status;
                source.FileModifiedAtUtc = File.GetLastWriteTimeUtc(source.FilePath);
            }
            catch (Exception exception)
            {
                source.ExtractionStatus = $"索引失败：{exception.Message}";
            }
        }
        AddAudit("重建资料索引", $"处理 {targets.Length} 项资料", true);
        await SaveProjectAsync();
        RefreshSourcesGrid();
    }

    private async Task RemoveSelectedSourceAsync()
    {
        var selected = _sourcesGrid.SelectedRows.Cast<DataGridViewRow>()
            .Select(row => row.Tag).OfType<ProofSource>().ToArray();
        if (selected.Length == 0 && _sourcesGrid.CurrentRow?.Tag is ProofSource current) selected = [current];
        if (selected.Length == 0) return;
        foreach (var source in selected) _project.Sources.Remove(source);
        AddAudit("移除资料", $"移除 {selected.Length} 项资料登记，原始文件未删除", true);
        await SaveProjectAsync();
        RefreshSourcesGrid();
        RefreshDashboard();
    }

    private async Task RunCiAsync(bool showPage, bool changePage = true)
    {
        try
        {
            _saveState.Text = "正在检查文档…";
            var markdown = await _getCurrentMarkdown();
            _lastResult = DocumentCiService.Analyze(markdown, _project, _workspaceRoot);
            AddAudit("运行文档体检", $"发现 {_lastResult.ErrorCount} 个错误和 {_lastResult.WarningCount} 个警告", false);
            await SaveProjectAsync();
            RefreshCiView();
            RefreshRequirementsGrid();
            RefreshDashboard();
            _saveState.Text = "文档体检已完成";
            if (showPage && changePage) ShowPage("ci");
        }
        catch (Exception exception)
        {
            _saveState.Text = "检查失败";
            MessageBox.Show(this, exception.Message, "文档体检失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task RunAgentAsync()
    {
        if (string.IsNullOrWhiteSpace(_agentRequest.Text))
        {
            MessageBox.Show(this, "请先描述需要 Agent 完成的任务。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            _agentRequest.Focus();
            return;
        }
        SaveAiSettings(silent: true);
        if (ShouldConfirmCloud() && MessageBox.Show(
                this,
                "这次任务会把命中的资料片段和你的要求发送到所配置的远程模型。是否继续？",
                "确认发送内容",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        _agentCancellation = new CancellationTokenSource();
        SetAgentBusy(true, "正在检索与任务最相关的资料…");
        _agentOutput.Clear();
        _agentSources.Items.Clear();
        try
        {
            var markdown = await _getCurrentMarkdown();
            var sources = ProofSourceQueryService.BuildSources(
                _project,
                markdown,
                Path.GetFileName(_currentDocumentPath) ?? "当前文档",
                _agentRequest.Text,
                _settings.MaxSources);
            if (sources.Count == 0)
                throw new InvalidOperationException("没有可用资料。请先打开文档或在资料中心添加可索引的资料。");
            _lastAgentSources = sources;
            foreach (var source in sources)
                _agentSources.Items.Add($"[{source.Id}] {source.DisplayPath}:{source.StartLine}-{source.EndLine}");
            SetAgentBusy(true, $"正在调用 {_settings.Model}，仅发送 {sources.Count} 个命中片段…");
            var result = await _client.CompleteAsync(
                _settings.Endpoint,
                _settings.Model,
                _apiKey.Text,
                _agentTask.SelectedItem?.ToString() ?? "可信文档修改建议",
                _agentRequest.Text,
                sources,
                _agentCancellation.Token);
            _agentOutput.Text = result;
            _insertAgentButton.Enabled = true;
            _agentStatus.Text = $"建议已生成，使用 {sources.Count} 个资料片段。请核对后再写入正文。";
            AddAudit("Agent 生成建议", _agentTask.SelectedItem?.ToString() ?? "文档任务", false);
            await SaveProjectAsync();
        }
        catch (OperationCanceledException)
        {
            _agentStatus.Text = "已停止本次任务，没有写入文档。";
        }
        catch (Exception exception)
        {
            _agentStatus.Text = "生成失败，没有写入文档。";
            MessageBox.Show(this, exception.Message, "Agent 任务失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _agentCancellation?.Dispose();
            _agentCancellation = null;
            SetAgentBusy(false, _agentStatus.Text);
        }
    }

    private async Task InsertAgentResultAsync()
    {
        if (string.IsNullOrWhiteSpace(_agentOutput.Text)) return;
        if (MessageBox.Show(
                this,
                "确认已经核对内容和来源，并把建议插入当前光标位置？插入后仍可撤销。",
                "人工确认",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        var portableMarkdown = BuildPortableAgentMarkdown(_agentOutput.Text, _lastAgentSources);
        _insertMarkdown(portableMarkdown);
        AddAudit("采纳 Agent 建议", _agentTask.SelectedItem?.ToString() ?? "文档任务", true);
        await SaveProjectAsync();
        _agentStatus.Text = "建议已插入正文，操作已记录。";
    }

    private void CopyAgentResult()
    {
        if (string.IsNullOrWhiteSpace(_agentOutput.Text)) return;
        try
        {
            Clipboard.SetText(_agentOutput.Text);
            _agentStatus.Text = "建议已复制。";
        }
        catch (Exception exception) when (exception is ExternalException or ThreadStateException)
        {
            MessageBox.Show(this, "暂时无法访问剪贴板，请稍后重试。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task ExportDeliveryPackageAsync()
    {
        await RunCiAsync(showPage: false, changePage: false);
        if (_lastResult is null) return;
        try
        {
            var paths = await ProofReportBuilder.ExportPackageAsync(
                _store.GetExportDirectory(_workspaceRoot),
                _project,
                _lastResult,
                Path.GetFileName(_currentDocumentPath) ?? "当前文档");
            _deliveryFiles.Items.Clear();
            foreach (var path in paths) _deliveryFiles.Items.Add(path);
            _deliveryStatus.Text = $"已生成 {paths.Count} 个可编辑文件。双击列表中的文件可以打开。";
            AddAudit("生成交付包", $"生成 {paths.Count} 个配套文件", true);
            await SaveProjectAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "生成交付包失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OpenExportDirectory()
    {
        var path = _store.GetExportDirectory(_workspaceRoot);
        Directory.CreateDirectory(path);
        OpenPath(path);
    }

    private void OpenSelectedDeliveryFile()
    {
        if (_deliveryFiles.SelectedItem is string path && File.Exists(path)) OpenPath(path);
    }

    private static void OpenPath(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch { }
    }

    private void SaveAiSettings(bool silent = false)
    {
        _settings.Endpoint = _endpoint.Text.Trim();
        _settings.Model = _model.Text.Trim();
        _settings.PrivacyMode = _privacyMode.SelectedItem?.ToString() switch
        {
            "云端模式" => "cloud",
            "混合模式" => "hybrid",
            _ => "local",
        };
        _settings.ConfirmBeforeCloud = _confirmCloud.Checked;
        _rememberApiKey(_apiKey.Text);
        _saveSettings();
        if (!silent) _saveState.Text = "AI 与隐私设置已保存";
    }

    private bool ShouldConfirmCloud()
    {
        if (!_settings.ConfirmBeforeCloud) return false;
        if (!Uri.TryCreate(_settings.Endpoint, UriKind.Absolute, out var uri)) return true;
        return !uri.IsLoopback && !uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshDashboard()
    {
        var result = _lastResult;
        var requirementPercent = result?.CoveragePercent ?? 0;
        var evidencePercent = result?.EvidencePercent ?? 0;
        var indexedSources = _project.Sources.Count(source => !string.IsNullOrWhiteSpace(source.ExtractedText));
        var problems = (result?.ErrorCount ?? 0) + (result?.WarningCount ?? 0);
        _requirementStat.SetValue($"{requirementPercent}%", $"{result?.CoveredRequirementCount ?? 0}/{result?.RequirementCount ?? 0} 项有对应内容", requirementPercent >= 80 ? ProofPalette.Accent : ProofPalette.Warning);
        _evidenceStat.SetValue($"{evidencePercent}%", $"{result?.SupportedClaimCount ?? 0}/{result?.ClaimCount ?? 0} 个重要结论有标记", evidencePercent >= 80 ? ProofPalette.Accent : ProofPalette.Warning);
        _sourceStat.SetValue(indexedSources.ToString(), $"共登记 {_project.Sources.Count} 项资料");
        _issueStat.SetValue(problems.ToString(), $"{result?.ErrorCount ?? 0} 个错误，{result?.WarningCount ?? 0} 个警告", problems == 0 ? ProofPalette.Accent : ProofPalette.Danger);
        var health = result is null ? 0 : Math.Clamp(100 - result.ErrorCount * 18 - result.WarningCount * 6, 0, 100);
        _healthRing.Value = health;
        _healthSummary.Text = result is null
            ? "尚未运行文档体检。导入要求和资料后，可以获得更准确的项目健康度。"
            : health >= 85
                ? "文档基础状态良好。建议进行一次评委视角审查，再生成最终交付包。"
                : health >= 60
                    ? "文档已经具备基本结构，但仍有要求或证据需要补充。先处理下方的高优先级问题。"
                    : "文档存在会影响交付的问题。建议先补齐任务要求和资料，再让 Agent 按小范围修改。";

        _dashboardIssues.SuspendLayout();
        _dashboardIssues.Controls.Clear();
        var priority = result?.Issues.Where(issue => issue.Severity is ProofIssueSeverity.Error or ProofIssueSeverity.Warning).Take(5).ToArray() ?? [];
        if (priority.Length == 0)
            _dashboardIssues.Controls.Add(IssueRow("已完成基础检查", "当前没有高优先级问题。", ProofPalette.Accent));
        foreach (var issue in priority)
            _dashboardIssues.Controls.Add(IssueRow(issue.Title, issue.Detail, issue.Severity == ProofIssueSeverity.Error ? ProofPalette.Danger : ProofPalette.Warning));
        _dashboardIssues.ResumeLayout();
    }

    private void RefreshRequirementsGrid()
    {
        _requirementsGrid.Rows.Clear();
        foreach (var requirement in _project.Requirements)
        {
            var index = _requirementsGrid.Rows.Add(
                requirement.IsCovered,
                requirement.Title,
                requirement.Points?.ToString() ?? "",
                requirement.Required ? "必须" : "可选",
                string.IsNullOrWhiteSpace(requirement.MatchedHeading) ? "尚未匹配" : requirement.MatchedHeading);
            _requirementsGrid.Rows[index].Tag = requirement;
        }
        var totalPoints = _project.Requirements.Where(item => item.Points.HasValue).Sum(item => item.Points!.Value);
        _requirementsSummary.Text = $"共 {_project.Requirements.Count} 项要求，其中 {_project.Requirements.Count(item => item.IsCovered)} 项已有对应内容" + (totalPoints > 0 ? $"，可识别分值 {totalPoints} 分" : string.Empty);
    }

    private void RefreshSourcesGrid()
    {
        _sourcesGrid.Rows.Clear();
        foreach (var source in _project.Sources)
        {
            var changed = File.Exists(source.FilePath) && source.FileModifiedAtUtc is { } last
                && File.GetLastWriteTimeUtc(source.FilePath) > last.AddSeconds(1);
            var index = _sourcesGrid.Rows.Add(source.DisplayName, source.Kind, source.TrustLevel, changed ? "源文件有更新" : source.ExtractionStatus, source.FilePath);
            _sourcesGrid.Rows[index].Tag = source;
        }
        _sourcesSummary.Text = $"共 {_project.Sources.Count} 项资料，{_project.Sources.Count(source => !string.IsNullOrWhiteSpace(source.ExtractedText))} 项可供 Agent 检索";
    }

    private void RefreshCiView()
    {
        _ciList.Items.Clear();
        _claimList.Items.Clear();
        if (_lastResult is null)
        {
            _ciSummary.Text = "尚未运行检查";
            return;
        }
        foreach (var issue in _lastResult.Issues.OrderBy(issue => issue.Severity))
        {
            var item = new ListViewItem(SeverityText(issue.Severity));
            item.SubItems.Add(issue.Category);
            item.SubItems.Add(issue.Title);
            item.SubItems.Add(issue.Line is { } line ? $"第 {line} 行：{issue.Detail}" : issue.Detail);
            item.ForeColor = issue.Severity switch
            {
                ProofIssueSeverity.Error => ProofPalette.Danger,
                ProofIssueSeverity.Warning => ProofPalette.Warning,
                ProofIssueSeverity.Passed => ProofPalette.Accent,
                _ => ProofPalette.Info,
            };
            _ciList.Items.Add(item);
        }
        foreach (var claim in _lastResult.Claims)
        {
            var stateText = claim.State switch
            {
                ProofEvidenceState.Linked => "已关联",
                ProofEvidenceState.NeedsReview => "待核验",
                ProofEvidenceState.PossibleConflict => "可能冲突",
                _ => "无来源",
            };
            var item = new ListViewItem(stateText);
            item.SubItems.Add(claim.Line.ToString());
            item.SubItems.Add(claim.Text.Length > 160 ? claim.Text[..160] + "…" : claim.Text);
            item.SubItems.Add(string.Join("，", claim.CitationIds));
            item.ForeColor = claim.State switch
            {
                ProofEvidenceState.Linked => ProofPalette.Accent,
                ProofEvidenceState.NeedsReview => ProofPalette.Warning,
                ProofEvidenceState.PossibleConflict => Color.FromArgb(116, 79, 145),
                _ => ProofPalette.Danger,
            };
            _claimList.Items.Add(item);
        }
        _ciSummary.Text = $"要求覆盖 {_lastResult.CoveragePercent}%　证据覆盖 {_lastResult.EvidencePercent}%　需要处理 {_lastResult.ErrorCount} 项　建议检查 {_lastResult.WarningCount} 项";
    }

    private void ConfigureDataGrids()
    {
        ConfigureGrid(_requirementsGrid);
        _requirementsGrid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "已覆盖", Width = 72, ReadOnly = true });
        _requirementsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "要求", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true });
        _requirementsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "分值", Width = 66, ReadOnly = true });
        _requirementsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "类型", Width = 72, ReadOnly = true });
        _requirementsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "对应章节", Width = 220, ReadOnly = true });

        ConfigureGrid(_sourcesGrid);
        _sourcesGrid.MultiSelect = true;
        _sourcesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "资料", Width = 190, ReadOnly = true });
        _sourcesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "格式", Width = 78, ReadOnly = true });
        var trust = new DataGridViewComboBoxColumn { HeaderText = "可信度", Width = 90 };
        trust.Items.AddRange("高", "一般", "待核实");
        _sourcesGrid.Columns.Add(trust);
        _sourcesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "索引状态", Width = 230, ReadOnly = true });
        _sourcesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "原始位置", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true });
        _sourcesGrid.CellValueChanged += async (_, args) =>
        {
            if (args.RowIndex < 0 || args.ColumnIndex != 2 || _sourcesGrid.Rows[args.RowIndex].Tag is not ProofSource source) return;
            source.TrustLevel = _sourcesGrid.Rows[args.RowIndex].Cells[2].Value?.ToString() ?? "一般";
            AddAudit("调整资料可信度", $"{source.DisplayName}：{source.TrustLevel}", true);
            await SaveProjectAsync();
        };
        _sourcesGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_sourcesGrid.IsCurrentCellDirty) _sourcesGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
    }

    private static void ConfigureGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill;
        grid.BackgroundColor = ProofPalette.Surface;
        grid.BorderStyle = BorderStyle.FixedSingle;
        grid.RowHeadersVisible = false;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor = ProofPalette.SurfaceMuted;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = ProofPalette.Ink;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Variable Text", 9F, FontStyle.Bold);
        grid.ColumnHeadersHeight = 38;
        grid.DefaultCellStyle.BackColor = ProofPalette.Surface;
        grid.DefaultCellStyle.ForeColor = ProofPalette.Ink;
        grid.DefaultCellStyle.SelectionBackColor = ProofPalette.AccentSoft;
        grid.DefaultCellStyle.SelectionForeColor = ProofPalette.Ink;
        grid.DefaultCellStyle.Padding = new Padding(5);
        grid.GridColor = ProofPalette.Border;
    }

    private async Task SaveProjectAsync()
    {
        _saveState.Text = "正在保存…";
        await _store.SaveAsync(_workspaceRoot, _project);
        _saveState.Text = "已保存到本地";
    }

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

    private void SetAgentBusy(bool busy, string status)
    {
        _runAgentButton.Enabled = !busy;
        _stopAgentButton.Enabled = busy;
        _agentRequest.Enabled = !busy;
        _agentTask.Enabled = !busy;
        _agentStatus.Text = status;
        UseWaitCursor = busy;
    }

    private Button CreateCompactButton(string text, string prompt)
    {
        var button = CreateSecondaryButton(text, (_, _) => _agentRequest.Text = prompt);
        button.Height = 28;
        button.Font = new Font("Segoe UI Variable Text", 8.5F);
        return button;
    }

    private static Panel CreateScrollablePage()
        => new()
        {
            Dock = DockStyle.Fill,
            BackColor = ProofPalette.Canvas,
            Padding = new Padding(30, 0, 30, 24),
            AutoScroll = true,
        };

    private static Label SectionTitle(string text)
        => new()
        {
            Dock = DockStyle.Top,
            Height = 48,
            Text = text,
            ForeColor = ProofPalette.Ink,
            Font = new Font("Segoe UI Variable Display", 13F, FontStyle.Bold),
            Padding = new Padding(0, 15, 0, 0),
        };

    private static Label FieldLabel(string text, DockStyle dock = DockStyle.Top, int height = 30)
        => new()
        {
            Dock = dock,
            Height = height,
            Text = text,
            ForeColor = ProofPalette.Ink,
            Font = new Font("Segoe UI Variable Text", 9F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
        };

    private static FlowLayoutPanel BuildToolbar(params Control[] controls)
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 56,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = ProofPalette.Canvas,
            Padding = new Padding(0, 6, 0, 6),
        };
        panel.Controls.AddRange(controls);
        return panel;
    }

    private static Button CreatePrimaryButton(string text, EventHandler handler)
    {
        var button = new Button { Text = text };
        StyleButton(button, primary: true);
        button.Click += handler;
        return button;
    }

    private static Button CreateSecondaryButton(string text, EventHandler handler)
    {
        var button = new Button { Text = text };
        StyleButton(button, primary: false);
        button.Click += handler;
        return button;
    }

    private static Button CreateDangerButton(string text, EventHandler handler)
    {
        var button = CreateSecondaryButton(text, handler);
        button.ForeColor = ProofPalette.Danger;
        return button;
    }

    private static void StyleButton(Button button, bool primary)
    {
        button.AutoSize = true;
        button.MinimumSize = new Size(96, 34);
        button.Height = 34;
        button.Padding = new Padding(12, 0, 12, 0);
        button.Margin = new Padding(0, 0, 8, 0);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.BorderColor = ProofPalette.Border;
        button.BackColor = primary ? ProofPalette.Accent : ProofPalette.Surface;
        button.ForeColor = primary ? Color.White : ProofPalette.Ink;
        button.Font = new Font("Segoe UI Variable Text", 9F, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
        if (primary)
        {
            button.FlatAppearance.MouseOverBackColor = ProofPalette.AccentHover;
            button.FlatAppearance.MouseDownBackColor = ProofPalette.AccentHover;
        }
    }

    private static Control IssueRow(string title, string detail, Color accent)
    {
        var card = new ProofCard { Width = 880, Height = 66, Margin = new Padding(0, 0, 0, 8), Padding = new Padding(18, 11, 18, 8) };
        var marker = new Panel { Dock = DockStyle.Left, Width = 4, BackColor = accent, Margin = new Padding(0, 0, 12, 0) };
        var titleLabel = new Label { Dock = DockStyle.Top, Height = 23, Text = title, ForeColor = ProofPalette.Ink, Font = new Font("Segoe UI Variable Text", 9.5F, FontStyle.Bold) };
        var detailLabel = new Label { Dock = DockStyle.Fill, Text = detail, ForeColor = ProofPalette.Muted, AutoEllipsis = true };
        card.Controls.Add(detailLabel);
        card.Controls.Add(titleLabel);
        card.Controls.Add(marker);
        return card;
    }

    private static void AddSettingRow(TableLayoutPanel layout, int row, string label, Control control)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, row == 5 ? 80 : 52));
        var caption = new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = ProofPalette.Ink,
            Font = new Font("Segoe UI Variable Text", 9F, FontStyle.Bold),
        };
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0, 8, 0, 8);
        layout.Controls.Add(caption, 0, row);
        layout.Controls.Add(control, 1, row);
    }

    private static string GuessTrustLevel(string path)
    {
        var name = Path.GetFileName(path);
        if (name.Contains("官方", StringComparison.OrdinalIgnoreCase)
            || name.Contains("标准", StringComparison.OrdinalIgnoreCase)
            || name.Contains("论文", StringComparison.OrdinalIgnoreCase))
            return "高";
        return "一般";
    }

    internal static string BuildPortableAgentMarkdown(string markdown, IReadOnlyList<AiSource> sources)
    {
        if (string.IsNullOrWhiteSpace(markdown) || sources.Count == 0) return markdown;
        var runId = DateTime.Now.ToString("yyyyMMddHHmmss");
        var result = markdown.Trim();
        var footnotes = new List<string>();
        foreach (var source in sources)
        {
            var token = $"ml-{runId}-{source.Id.ToLowerInvariant()}";
            var marker = $"[{source.Id}]";
            if (!result.Contains(marker, StringComparison.Ordinal)) continue;
            result = result.Replace(marker, $"[^{token}]", StringComparison.Ordinal);
            footnotes.Add($"[^{token}]: {source.DisplayPath}，第 {source.StartLine}-{source.EndLine} 行。由 MarkLeaf Proof 在本次任务中检索。 ");
        }
        return footnotes.Count == 0
            ? result
            : result + "\n\n## AI 建议所用来源\n\n" + string.Join("\n", footnotes);
    }

    private static string SeverityText(ProofIssueSeverity severity) => severity switch
    {
        ProofIssueSeverity.Error => "需处理",
        ProofIssueSeverity.Warning => "建议检查",
        ProofIssueSeverity.Passed => "已通过",
        _ => "提示",
    };

    private static bool PathEquals(string left, string right)
    {
        try { return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase); }
        catch { return string.Equals(left, right, StringComparison.OrdinalIgnoreCase); }
    }
}
