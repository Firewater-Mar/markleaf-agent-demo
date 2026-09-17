using MarkLeaf.Services.AI;
using MarkLeaf.Services.Proof;
using MarkLeaf.Services.Settings;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Diagnostics;
using System.Text.Json;

namespace MarkLeaf.UI.Agent;

internal sealed class AgentPanel : UserControl
{
    private static readonly Color Surface = Color.FromArgb(255, 255, 255);
    private static readonly Color Canvas = Color.FromArgb(247, 248, 247);
    private static readonly Color Border = Color.FromArgb(220, 227, 223);
    private static readonly Color Ink = Color.FromArgb(22, 33, 29);
    private static readonly Color Muted = Color.FromArgb(93, 107, 101);
    private static readonly Color Accent = Color.FromArgb(23, 107, 85);

    private readonly Func<string?> _getWorkspaceRoot;
    private readonly Func<string?> _getDocumentPath;
    private readonly Func<Task<string>> _getCurrentMarkdown;
    private readonly Action<string> _insertMarkdown;
    private readonly AiSettings _settings;
    private readonly Action<string> _rememberApiKey;
    private readonly Action _saveSettings;
    private readonly string _webView2UserDataDirectory;
    private readonly ProofProjectStore _store = new();
    private readonly OpenAiCompatibleClient _client = new();
    private readonly WebView2 _webView = new();
    private readonly Label _loadingLabel = new();

    private ProofProject _project = new();
    private ProofCiResult? _lastCiResult;
    private string? _loadedRoot;
    private string _sessionApiKey;
    private string _lastAnswer = string.Empty;
    private IReadOnlyList<AiSource> _lastSources = [];
    private CancellationTokenSource? _cancellation;
    private bool _webReady;
    private bool _webViewInitializing;

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
        _webView2UserDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MarkLeaf",
            "WebView2",
            "Agent");

        Dock = DockStyle.Fill;
        BackColor = Canvas;
        MinimumSize = new Size(320, 0);
        AutoScaleMode = AutoScaleMode.Dpi;

        _webView.Dock = DockStyle.Fill;
        _webView.Visible = false;
        _webView.DefaultBackgroundColor = Canvas;
        _webView.CreationProperties = new CoreWebView2CreationProperties
        {
            UserDataFolder = _webView2UserDataDirectory,
        };

        _loadingLabel.Dock = DockStyle.Fill;
        _loadingLabel.Text = "正在准备 MarkLeaf Agent…";
        _loadingLabel.TextAlign = ContentAlignment.MiddleCenter;
        _loadingLabel.ForeColor = Muted;
        _loadingLabel.BackColor = Canvas;
        _loadingLabel.Font = new Font("Segoe UI", 9.5F);

        Controls.Add(_webView);
        Controls.Add(_loadingLabel);
        HandleCreated += OnHandleCreated;
    }

    public async Task RefreshContextAsync()
    {
        try
        {
            await EnsureProjectAsync();
            SendState();
        }
        catch (Exception exception)
        {
            SendError($"项目读取失败：{exception.Message}");
        }
    }

    public void FocusComposer() => SendToWeb(new { type = "focus" });

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            HandleCreated -= OnHandleCreated;
            _cancellation?.Cancel();
            _cancellation?.Dispose();
            if (_webView.CoreWebView2 is not null)
                _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
            _webView.Dispose();
            _loadingLabel.Dispose();
        }
        base.Dispose(disposing);
    }

    private async void OnHandleCreated(object? sender, EventArgs eventArgs)
    {
        if (_webViewInitializing || _webReady || IsDisposed) return;
        _webViewInitializing = true;
        try
        {
            Directory.CreateDirectory(_webView2UserDataDirectory);
            await _webView.EnsureCoreWebView2Async();
            if (IsDisposed || _webView.CoreWebView2 is null) return;

            var browserSettings = _webView.CoreWebView2.Settings;
            browserSettings.AreDefaultContextMenusEnabled = false;
            browserSettings.AreDevToolsEnabled = false;
            browserSettings.IsStatusBarEnabled = false;
            browserSettings.IsZoomControlEnabled = false;
            browserSettings.IsPinchZoomEnabled = false;
            _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            _webView.NavigateToString(AgentPanelPage.Html);
        }
        catch (Exception exception)
        {
            _loadingLabel.Text = $"Agent 界面无法启动\n\n{exception.Message}";
            _loadingLabel.ForeColor = Color.FromArgb(179, 67, 62);
        }
        finally
        {
            _webViewInitializing = false;
        }
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs eventArgs)
    {
        try
        {
            using var document = JsonDocument.Parse(eventArgs.WebMessageAsJson);
            var root = document.RootElement;
            if (!root.TryGetProperty("action", out var actionValue)) return;
            switch (actionValue.GetString())
            {
                case "ready":
                    _webReady = true;
                    _loadingLabel.Visible = false;
                    _webView.Visible = true;
                    _webView.BringToFront();
                    BeginInvoke(NormalizeHostSplit);
                    await RefreshContextAsync();
                    break;
                case "send":
                    var prompt = root.TryGetProperty("prompt", out var promptValue)
                        ? promptValue.GetString() ?? string.Empty
                        : string.Empty;
                    var mode = root.TryGetProperty("mode", out var modeValue)
                        ? modeValue.GetString() ?? "plan"
                        : "plan";
                    await RunAgentAsync(prompt, mode);
                    break;
                case "cancel":
                    _cancellation?.Cancel();
                    break;
                case "apply":
                    await ApplyLastAnswerAsync();
                    break;
                case "import_requirements":
                    await ImportRequirementsAsync();
                    break;
                case "import_sources":
                    await ImportSourcesAsync();
                    break;
                case "run_check":
                    await RunDocumentCheckAsync(announce: true);
                    break;
                case "export":
                    await ExportCompanionFilesAsync();
                    break;
                case "settings":
                    ShowAgentSettings();
                    break;
            }
        }
        catch (Exception exception)
        {
            SendError(exception.Message);
            SetBusy(false, string.Empty);
        }
    }

    private async Task RunAgentAsync(string request, string mode)
    {
        if (string.IsNullOrWhiteSpace(request))
        {
            SendToWeb(new { type = "toast", message = "先描述要完成的文档任务" });
            return;
        }

        try
        {
            await EnsureProjectAsync();
            _cancellation = new CancellationTokenSource();
            SetBusy(true, "正在读取当前文档", cancellable: true);
            var markdown = await _getCurrentMarkdown();
            if (string.IsNullOrWhiteSpace(markdown) && _project.Sources.Count == 0)
                throw new InvalidOperationException("请先打开一份文档，或在“上下文”中添加资料。");

            if (ShouldConfirmCloud() && MessageBox.Show(
                    FindForm(),
                    "这次任务会把命中的资料片段和你的要求发送到所配置的远程模型。是否继续？",
                    "确认发送",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes)
            {
                SendToWeb(new { type = "toast", message = "已取消，没有发送资料" });
                return;
            }

            _lastSources = ProofSourceQueryService.BuildSources(
                _project,
                markdown,
                Path.GetFileName(_getDocumentPath()) ?? "当前文档",
                request.Trim(),
                _settings.MaxSources);
            if (_lastSources.Count == 0)
                throw new InvalidOperationException("没有找到可供 Agent 使用的文本资料。");

            SendToWeb(new
            {
                type = "activity",
                label = $"已找到 {_lastSources.Count} 个相关片段",
                detail = $"正在调用 {_settings.Model}",
            });

            var isPlan = string.Equals(mode, "plan", StringComparison.OrdinalIgnoreCase);
            var instruction = isPlan
                ? "只分析并给出分步计划。指出依据、风险和需要用户确认的事项，不生成可直接插入正文的完整段落"
                : "生成可直接审阅的 Markdown 修改建议。保持原文风格，为重要事实标注来源，并明确资料不足之处";
            _lastAnswer = await _client.CompleteAsync(
                _settings.Endpoint,
                _settings.Model,
                _sessionApiKey,
                instruction,
                request.Trim(),
                _lastSources,
                _cancellation.Token);

            SendToWeb(new
            {
                type = "assistant",
                content = _lastAnswer,
                sources = _lastSources.Select(source => $"{source.Id} · {source.DisplayPath}:{source.StartLine}").ToArray(),
                canApply = !isPlan,
            });
            AddAudit(isPlan ? "Agent 制定计划" : "Agent 生成建议", request.Trim(), false);
            await SaveProjectAsync();
        }
        catch (OperationCanceledException)
        {
            SendToWeb(new { type = "toast", message = "任务已停止，没有修改文档" });
        }
        catch (Exception exception)
        {
            SendError(exception.Message);
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
            SetBusy(false, string.Empty);
        }
    }

    private async Task ApplyLastAnswerAsync()
    {
        if (string.IsNullOrWhiteSpace(_lastAnswer)) return;
        if (MessageBox.Show(
                FindForm(),
                "确认已经核对建议和来源，并插入到当前光标位置？\n\n插入后仍可在编辑器中撤销。",
                "应用 Agent 建议",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        var portable = PortableCitationService.ConvertAgentSourcesToFootnotes(_lastAnswer, _lastSources);
        _insertMarkdown(portable);
        AddAudit("人工采纳 Agent 建议", $"写入 {portable.Length} 个字符", true);
        await SaveProjectAsync();
        SendToWeb(new { type = "applied" });
        SendToWeb(new { type = "toast", message = "已应用到文档，可使用撤销恢复" });
    }

    private async Task ImportRequirementsAsync()
    {
        if (ResolveProjectRoot() is null)
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
            SetBusy(true, "正在识别任务要求");
            await EnsureProjectAsync();
            var (text, status) = await SourceTextExtractor.ExtractAsync(dialog.FileName);
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException(status);
            var existing = _project.Requirements.Select(item => item.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var additions = RequirementAnalyzer.Analyze(text).Where(item => existing.Add(item.Title)).ToArray();
            _project.Requirements.AddRange(additions);
            AddAudit("导入任务要求", $"从 {Path.GetFileName(dialog.FileName)} 识别 {additions.Length} 项", true);
            await SaveProjectAsync();
            await RunDocumentCheckAsync(announce: false);
            SendState();
            SendToWeb(new { type = "toast", message = $"已识别 {additions.Length} 项新要求" });
        }
        catch (Exception exception)
        {
            SendError($"导入失败：{exception.Message}");
        }
        finally
        {
            SetBusy(false, string.Empty);
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

        try
        {
            SetBusy(true, "正在索引项目资料");
            await EnsureProjectAsync();
            var added = 0;
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
                added++;
            }
            AddAudit("添加项目资料", $"新增 {added} 项，资料中心现有 {_project.Sources.Count} 项", true);
            await SaveProjectAsync();
            SendState();
            SendToWeb(new { type = "toast", message = added == 0 ? "所选资料已经存在" : $"已添加 {added} 项资料" });
        }
        catch (Exception exception)
        {
            SendError($"添加资料失败：{exception.Message}");
        }
        finally
        {
            SetBusy(false, string.Empty);
        }
    }

    private async Task RunDocumentCheckAsync(bool announce)
    {
        try
        {
            if (announce) SetBusy(true, "正在检查当前文档");
            await EnsureProjectAsync();
            var markdown = await _getCurrentMarkdown();
            var root = ResolveProjectRoot() ?? Environment.CurrentDirectory;
            _lastCiResult = DocumentCiService.Analyze(markdown, _project, root);
            await SaveProjectAsync();
            SendState();
            if (announce)
                SendToWeb(new { type = "toast", message = $"检查完成，发现 {_lastCiResult.ErrorCount} 项需处理问题" });
        }
        catch (Exception exception)
        {
            SendError($"检查失败：{exception.Message}");
        }
        finally
        {
            if (announce) SetBusy(false, string.Empty);
        }
    }

    private async Task ExportCompanionFilesAsync()
    {
        try
        {
            SetBusy(true, "正在生成配套材料");
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
            var files = await ProofReportBuilder.ExportPackageAsync(exportDirectory, _project, _lastCiResult, documentName);
            AddAudit("导出配套材料", $"生成 {files.Count} 个 Markdown 文件", true);
            await SaveProjectAsync();
            SendState();
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
            SendError($"导出失败：{exception.Message}");
        }
        finally
        {
            SetBusy(false, string.Empty);
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
        _lastCiResult = null;
    }

    private async Task SaveProjectAsync()
    {
        var root = ResolveProjectRoot();
        if (root is not null) await _store.SaveAsync(root, _project);
    }

    private string? ResolveProjectRoot()
    {
        var workspace = _getWorkspaceRoot();
        if (!string.IsNullOrWhiteSpace(workspace) && Directory.Exists(workspace)) return workspace;
        var path = _getDocumentPath();
        var directory = string.IsNullOrWhiteSpace(path) ? null : Path.GetDirectoryName(path);
        return !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory) ? directory : null;
    }

    private void SendState()
    {
        var root = ResolveProjectRoot();
        var requirements = _project.Requirements.Select(requirement => new
        {
            title = requirement.Title,
            description = requirement.Description,
            covered = requirement.IsCovered,
        }).ToArray();
        var sources = _project.Sources.Select(source => new
        {
            name = source.DisplayName,
            kind = source.Kind,
            status = source.ExtractionStatus,
            trust = source.TrustLevel,
            ready = !string.IsNullOrWhiteSpace(source.ExtractedText),
        }).ToArray();
        var check = _lastCiResult is null
            ? null
            : new
            {
                coverage = _lastCiResult.CoveragePercent,
                evidence = _lastCiResult.EvidencePercent,
                errors = _lastCiResult.ErrorCount,
                issues = _lastCiResult.Issues.OrderBy(issue => issue.Severity).Select(issue => new
                {
                    title = issue.Title,
                    detail = issue.Detail,
                    state = issue.Severity switch
                    {
                        ProofIssueSeverity.Error => "需处理",
                        ProofIssueSeverity.Warning => "检查",
                        ProofIssueSeverity.Passed => "通过",
                        _ => "提示",
                    },
                    tone = issue.Severity switch
                    {
                        ProofIssueSeverity.Error => "bad",
                        ProofIssueSeverity.Warning => "warn",
                        ProofIssueSeverity.Passed => "good",
                        _ => string.Empty,
                    },
                }).ToArray(),
            };

        SendToWeb(new
        {
            type = "state",
            documentName = Path.GetFileName(_getDocumentPath()),
            projectName = string.IsNullOrWhiteSpace(root)
                ? string.Empty
                : Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            model = _settings.Model,
            requirements,
            sources,
            check,
        });
    }

    private void NormalizeHostSplit()
    {
        if (IsDisposed
            || Parent is not SplitterPanel panel
            || panel.Parent is not SplitContainer split
            || !ReferenceEquals(panel, split.Panel2))
            return;

        var scale = Math.Max(1F, DeviceDpi / 96F);
        var desiredAgentWidth = (int)Math.Round(430 * scale);
        var editorMinimum = Math.Max(split.Panel1MinSize, (int)Math.Round(420 * scale));
        var maximumDistance = Math.Max(
            split.Panel1MinSize,
            split.ClientSize.Width - split.SplitterWidth - split.Panel2MinSize);
        split.SplitterDistance = Math.Clamp(
            split.ClientSize.Width - split.SplitterWidth - desiredAgentWidth,
            editorMinimum,
            maximumDistance);
    }

    private void SetBusy(bool busy, string label, bool cancellable = false)
    {
        UseWaitCursor = busy;
        SendToWeb(new { type = "busy", busy, label, cancellable });
    }

    private void SendError(string message) => SendToWeb(new { type = "error", message });

    private void SendToWeb(object payload)
    {
        if (!_webReady || IsDisposed || _webView.CoreWebView2 is null) return;
        _webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload));
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
            ClientSize = new Size(540, 300),
            BackColor = Surface,
            Font = new Font("Segoe UI", 9F),
        };
        var endpoint = new TextBox { Text = _settings.Endpoint, Dock = DockStyle.Fill };
        var model = new TextBox { Text = _settings.Model, Dock = DockStyle.Fill };
        var apiKey = new TextBox { Text = _sessionApiKey, UseSystemPasswordChar = true, Dock = DockStyle.Fill };
        var confirm = new CheckBox { Text = "发送到非本地地址前再次确认", Checked = _settings.ConfirmBeforeCloud, AutoSize = true };
        var hint = new Label
        {
            Text = "支持 OpenAI-compatible API；本地 Ollama 默认地址为 http://localhost:11434/v1。",
            ForeColor = Muted,
            AutoSize = true,
            MaximumSize = new Size(390, 0),
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 2, RowCount = 6 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var index = 0; index < 4; index++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        AddSettingsRow(layout, 0, "API 地址", endpoint);
        AddSettingsRow(layout, 1, "模型", model);
        AddSettingsRow(layout, 2, "API Key", apiKey);
        layout.Controls.Add(confirm, 1, 3);
        layout.Controls.Add(hint, 1, 4);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var save = CreateDialogButton("保存", true); save.DialogResult = DialogResult.OK;
        var cancel = CreateDialogButton("取消", false); cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 1, 5);
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
        SendState();
        SendToWeb(new { type = "toast", message = "Agent 设置已保存" });
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

    private static Button CreateDialogButton(string text, bool primary)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            MinimumSize = new Size(84, 32),
            Height = 32,
            Padding = new Padding(10, 0, 10, 0),
            Margin = new Padding(7, 0, 0, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Accent : Surface,
            ForeColor = primary ? Color.White : Ink,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            Cursor = Cursors.Hand,
        };
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.BorderColor = Border;
        return button;
    }

    private bool ShouldConfirmCloud()
    {
        if (!_settings.ConfirmBeforeCloud) return false;
        if (!Uri.TryCreate(_settings.Endpoint, UriKind.Absolute, out var uri)) return true;
        return !uri.IsLoopback && !uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
    }

    private void ShowOpenProjectMessage()
        => MessageBox.Show(FindForm(), "请先打开一个工作区或 Markdown 文档。", "MarkLeaf Agent", MessageBoxButtons.OK, MessageBoxIcon.Information);

    private void AddAudit(string action, string detail, bool humanConfirmed)
    {
        _project.AuditTrail.Add(new ProofAuditEvent { Action = action, Detail = detail, HumanConfirmed = humanConfirmed });
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
