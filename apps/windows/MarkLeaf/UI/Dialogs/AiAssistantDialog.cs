using MarkLeaf.Services.AI;
using MarkLeaf.Services.Settings;
using MarkLeaf.UI.Controls;
using System.Runtime.InteropServices;

namespace MarkLeaf.UI.Dialogs;

internal sealed class AiAssistantDialog : Form
{
    private readonly string? _workspaceRoot;
    private readonly string? _currentDocumentPath;
    private readonly string _currentMarkdown;
    private readonly AiSettings _settings;
    private readonly Action<string> _insertMarkdown;
    private readonly Action<string> _rememberApiKey;
    private readonly Action _saveSettings;
    private readonly OpenAiCompatibleClient _client = new();

    private readonly TextBox _endpoint = new() { Dock = DockStyle.Fill };
    private readonly TextBox _model = new() { Dock = DockStyle.Fill };
    private readonly TextBox _apiKey = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly ComboBox _task = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _includeCurrent = new() { AutoSize = true, Text = "当前文档" };
    private readonly CheckBox _includeWorkspace = new() { AutoSize = true, Text = "工作区资料" };
    private readonly TextBox _request = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        AcceptsReturn = true,
        ScrollBars = ScrollBars.Vertical,
    };
    private readonly TextBox _output = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Both,
        Font = new Font("Cascadia Mono", 9.5f),
    };
    private readonly ListBox _sources = new() { Dock = DockStyle.Fill };
    private readonly Label _status = new() { AutoSize = true, Text = "内容不会自动写入文档。请先核对来源和生成结果。" };
    private readonly Button _generate = new() { AutoSize = true, Text = "生成预览" };
    private readonly Button _cancel = new() { AutoSize = true, Text = "停止", Enabled = false };
    private readonly Button _insert = new() { AutoSize = true, Text = "插入到光标位置", Enabled = false };
    private readonly Button _copy = new() { AutoSize = true, Text = "复制结果", Enabled = false };
    private CancellationTokenSource? _generationCancellation;

    public AiAssistantDialog(
        string? workspaceRoot,
        string? currentDocumentPath,
        string currentMarkdown,
        AiSettings settings,
        string sessionApiKey,
        Action<string> insertMarkdown,
        Action<string> rememberApiKey,
        Action saveSettings)
    {
        _workspaceRoot = workspaceRoot;
        _currentDocumentPath = currentDocumentPath;
        _currentMarkdown = currentMarkdown;
        _settings = settings;
        _insertMarkdown = insertMarkdown;
        _rememberApiKey = rememberApiKey;
        _saveSettings = saveSettings;

        Text = "MarkLeaf AI · 可信文档助手";
        BackColor = DialogColors.Secondary;
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(this.ScaleForDpi(760), this.ScaleForDpi(620));
        Size = new Size(this.ScaleForDpi(920), this.ScaleForDpi(720));
        MaximizeBox = true;
        MinimizeBox = false;
        ShowInTaskbar = false;

        _endpoint.Text = settings.Endpoint;
        _model.Text = settings.Model;
        _apiKey.Text = sessionApiKey;
        _apiKey.PlaceholderText = "本地 Ollama 可留空；密钥仅保留到本次应用退出";
        _task.Items.AddRange([
            "依据资料回答问题",
            "生成有引用的文档草稿",
            "审查事实、引用与前后文一致性",
            "整理大纲和关键观点",
        ]);
        _task.SelectedIndex = 0;
        _includeCurrent.Checked = settings.IncludeCurrentDocument;
        _includeWorkspace.Checked = settings.IncludeWorkspace;
        _includeCurrent.Enabled = !string.IsNullOrWhiteSpace(currentMarkdown);
        _includeWorkspace.Enabled = !string.IsNullOrWhiteSpace(workspaceRoot) && Directory.Exists(workspaceRoot);
        _request.PlaceholderText = "例如：根据现有资料生成一份项目背景与应用价值说明，每个关键结论标注来源。";

        _generate.Click += async (_, _) => await GenerateAsync();
        _cancel.Click += (_, _) => _generationCancellation?.Cancel();
        _insert.Click += (_, _) => InsertResult();
        _copy.Click += (_, _) => CopyResult();
        FormClosing += (_, _) => _generationCancellation?.Cancel();

        Controls.Add(BuildLayout());
    }

    private Control BuildLayout()
    {
        var connection = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 2,
            AutoSize = true,
        };
        connection.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        connection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        connection.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        connection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        connection.Controls.Add(LabelFor("API 地址"), 0, 0);
        connection.Controls.Add(_endpoint, 1, 0);
        connection.Controls.Add(LabelFor("模型"), 2, 0);
        connection.Controls.Add(_model, 3, 0);
        connection.Controls.Add(LabelFor("API Key"), 0, 1);
        connection.Controls.Add(_apiKey, 1, 1);
        connection.SetColumnSpan(_apiKey, 3);

        var sourceOptions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
        };
        sourceOptions.Controls.Add(LabelFor("使用资料："));
        sourceOptions.Controls.Add(_includeCurrent);
        sourceOptions.Controls.Add(_includeWorkspace);

        var taskLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        taskLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        taskLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        taskLayout.Controls.Add(LabelFor("任务类型"), 0, 0);
        taskLayout.Controls.Add(_task, 1, 0);

        var resultSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
        };
        resultSplit.Panel1.Controls.Add(WrapWithTitle("生成结果（Markdown 预览）", _output));
        resultSplit.Panel2.Controls.Add(WrapWithTitle("本次使用的来源", _sources));

        var actionButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
        };
        var close = new Button { AutoSize = true, Text = "关闭", DialogResult = DialogResult.Cancel };
        actionButtons.Controls.Add(close);
        actionButtons.Controls.Add(_insert);
        actionButtons.Controls.Add(_copy);
        actionButtons.Controls.Add(_cancel);
        actionButtons.Controls.Add(_generate);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Padding(this.ScaleForDpi(12)),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, this.ScaleForDpi(108)));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(connection, 0, 0);
        root.Controls.Add(sourceOptions, 0, 1);
        root.Controls.Add(taskLayout, 0, 2);
        root.Controls.Add(WrapWithTitle("你的要求", _request), 0, 3);
        root.Controls.Add(resultSplit, 0, 4);
        root.Controls.Add(_status, 0, 5);
        root.Controls.Add(actionButtons, 0, 6);
        return root;
    }

    private static Label LabelFor(string text) => new()
    {
        AutoSize = true,
        Text = text,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(3, 6, 8, 3),
    };

    private static Control WrapWithTitle(string title, Control content)
    {
        var group = new GroupBox { Dock = DockStyle.Fill, Text = title, Padding = new Padding(8) };
        group.Controls.Add(content);
        return group;
    }

    private async Task GenerateAsync()
    {
        if (string.IsNullOrWhiteSpace(_request.Text))
        {
            MessageBox.Show(this, "请先填写任务要求。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            _request.Focus();
            return;
        }
        if (!_includeCurrent.Checked && !_includeWorkspace.Checked)
        {
            MessageBox.Show(this, "请至少选择一种资料来源。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SaveCurrentSettings();
        SetBusy(true, "正在检索本地资料…");
        _output.Clear();
        _sources.Items.Clear();
        _generationCancellation = new CancellationTokenSource();
        try
        {
            var sources = await AiKnowledgeRetriever.RetrieveAsync(
                _workspaceRoot,
                _currentDocumentPath,
                _currentMarkdown,
                _request.Text,
                _includeCurrent.Checked,
                _includeWorkspace.Checked,
                _settings.MaxSources,
                _generationCancellation.Token);
            if (sources.Count == 0)
                throw new InvalidOperationException("没有找到可用资料。请先打开文档或包含 Markdown/TXT 的工作区。");

            foreach (var source in sources)
                _sources.Items.Add($"[{source.Id}] {source.DisplayPath}:{source.StartLine}-{source.EndLine}");

            SetBusy(true, $"正在调用 {_model.Text.Trim()}，已提供 {sources.Count} 条资料…");
            var result = await _client.CompleteAsync(
                _endpoint.Text,
                _model.Text,
                _apiKey.Text,
                _task.SelectedItem?.ToString() ?? "依据资料回答问题",
                _request.Text,
                sources,
                _generationCancellation.Token);
            _output.Text = result;
            _insert.Enabled = true;
            _copy.Enabled = true;
            _status.Text = $"生成完成，共使用 {sources.Count} 条来源。插入前请核对内容与引用。";
        }
        catch (OperationCanceledException)
        {
            _status.Text = "已停止生成。";
        }
        catch (Exception exception)
        {
            _status.Text = "生成失败。";
            MessageBox.Show(this, exception.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _generationCancellation.Dispose();
            _generationCancellation = null;
            SetBusy(false, _status.Text);
        }
    }

    private void SaveCurrentSettings()
    {
        _settings.Endpoint = _endpoint.Text.Trim();
        _settings.Model = _model.Text.Trim();
        _settings.IncludeCurrentDocument = _includeCurrent.Checked;
        _settings.IncludeWorkspace = _includeWorkspace.Checked;
        _rememberApiKey(_apiKey.Text);
        _saveSettings();
    }

    private void InsertResult()
    {
        if (string.IsNullOrWhiteSpace(_output.Text)) return;
        var result = MessageBox.Show(
            this,
            "确认已核对生成内容及来源，并插入到当前光标位置？",
            Text,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (result != DialogResult.Yes) return;
        _insertMarkdown(_output.Text);
        _status.Text = "内容已插入文档；仍可使用撤销恢复。";
    }

    private void CopyResult()
    {
        if (string.IsNullOrWhiteSpace(_output.Text)) return;
        try
        {
            Clipboard.SetText(_output.Text);
            _status.Text = "结果已复制。";
        }
        catch (Exception exception) when (exception is ExternalException or ThreadStateException)
        {
            MessageBox.Show(this, "无法访问剪贴板，请稍后重试。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void SetBusy(bool busy, string status)
    {
        _generate.Enabled = !busy;
        _cancel.Enabled = busy;
        _endpoint.Enabled = !busy;
        _model.Enabled = !busy;
        _apiKey.Enabled = !busy;
        _task.Enabled = !busy;
        _includeCurrent.Enabled = !busy && !string.IsNullOrWhiteSpace(_currentMarkdown);
        _includeWorkspace.Enabled = !busy && !string.IsNullOrWhiteSpace(_workspaceRoot) && Directory.Exists(_workspaceRoot);
        _request.Enabled = !busy;
        _status.Text = status;
        UseWaitCursor = busy;
    }
}
