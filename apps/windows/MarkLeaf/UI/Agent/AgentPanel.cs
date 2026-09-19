using MarkLeaf.Services.AI;
using MarkLeaf.Services.AgentRuntime;
using MarkLeaf.Services.Proof;
using MarkLeaf.Services.Settings;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

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
    private readonly Action<string, string> _replaceSection;
    private readonly Func<string, Task> _openDocument;
    private readonly Func<string, int, Task> _revealSource;
    private readonly Func<string, Task<bool>> _exportDocument;
    private readonly AiSettings _settings;
    private readonly Action<string> _rememberApiKey;
    private readonly Action _saveSettings;
    private readonly string _webView2UserDataDirectory;
    private readonly ProofProjectStore _store = new();
    private readonly SemaphoreSlim _projectGate = new(1, 1);
    private readonly OpenAiCompatibleClient _client = new();
    private readonly WebView2 _webView = new();
    private readonly Label _loadingLabel = new();

    private ProofProject _project = new();
    private AgentRuntime? _agentRuntime;
    private AgentRunState? _activeRun;
    private ProofCiResult? _lastCiResult;
    private string? _loadedRoot;
    private string _sessionApiKey;
    private string _lastAnswer = string.Empty;
    private IReadOnlyList<AiSource> _lastSources = [];
    private IReadOnlyList<string> _availableModels = [];
    private AgentSectionEdit? _pendingSectionEdit;
    private AgentFileDraft? _pendingFileDraft;
    private string? _targetDocumentPath;
    private string _pendingCloudRequest = string.Empty;
    private string _pendingCloudMode = "auto";
    private string? _pendingCloudTargetPath;
    private bool _pendingSemanticCheck;
    private bool _cloudConsentGranted;
    private CancellationTokenSource? _cancellation;
    private bool _webReady;
    private bool _webViewInitializing;

    public event EventHandler<string>? ModelChanged;

    public AgentPanel(
        Func<string?> getWorkspaceRoot,
        Func<string?> getDocumentPath,
        Func<Task<string>> getCurrentMarkdown,
        Action<string, string> replaceSection,
        Func<string, Task> openDocument,
        Func<string, int, Task> revealSource,
        Func<string, Task<bool>> exportDocument,
        AiSettings settings,
        string sessionApiKey,
        Action<string> rememberApiKey,
        Action saveSettings)
    {
        _getWorkspaceRoot = getWorkspaceRoot;
        _getDocumentPath = getDocumentPath;
        _getCurrentMarkdown = getCurrentMarkdown;
        _replaceSection = replaceSection;
        _openDocument = openDocument;
        _revealSource = revealSource;
        _exportDocument = exportDocument;
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
            var activePath = _getDocumentPath();
            if (!string.IsNullOrWhiteSpace(activePath)
                && !PathEquals(activePath, _targetDocumentPath ?? string.Empty))
            {
                _targetDocumentPath = activePath;
                _lastCiResult = null;
            }
            SendState();
        }
        catch (Exception exception)
        {
            SendError($"项目读取失败：{exception.Message}");
        }
    }

    public void FocusComposer() => SendToWeb(new { type = "focus" });

    public void OpenModelPicker() => SendToWeb(new { type = "open_model_picker" });

    public void OpenSettings() => SendToWeb(new { type = "open_settings" });

    public void SwitchPage(string page) => SendToWeb(new { type = "switch_page", page });

    public void InvalidateDocumentAnalysis()
    {
        if (_lastCiResult is null) return;
        _lastCiResult = null;
        SendState();
    }

    public void ResetWorkspaceContext(string? workspaceRoot)
    {
        _loadedRoot = null;
        _agentRuntime = null;
        _activeRun = null;
        _targetDocumentPath = null;
        _lastCiResult = null;
        _pendingSectionEdit = null;
        _pendingFileDraft = null;
        _project = new ProofProject
        {
            Title = string.IsNullOrWhiteSpace(workspaceRoot)
                ? "尚未打开项目"
                : Path.GetFileName(workspaceRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
        };
        SendState();
    }

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
                    await RefreshContextAsync();
                    break;
                case "send":
                    var prompt = root.TryGetProperty("prompt", out var promptValue)
                        ? promptValue.GetString() ?? string.Empty
                        : string.Empty;
                    var mode = root.TryGetProperty("mode", out var modeValue)
                        ? modeValue.GetString() ?? "auto"
                        : "auto";
                    await RunAgentAsync(prompt, mode, ReadString(root, "targetPath"));
                    break;
                case "cancel":
                    _cancellation?.Cancel();
                    break;
                case "apply":
                    await ApplyLastAnswerAsync();
                    break;
                case "select_target_document":
                    var targetPath = ReadString(root, "path");
                    if (!string.IsNullOrWhiteSpace(targetPath)) await SelectTargetDocumentAsync(targetPath);
                    break;
                case "open_source":
                    var sourcePath = ReadString(root, "path");
                    var sourceLine = root.TryGetProperty("line", out var lineValue) && lineValue.TryGetInt32(out var parsedLine)
                        ? Math.Max(1, parsedLine)
                        : 1;
                    if (!string.IsNullOrWhiteSpace(sourcePath) && File.Exists(sourcePath))
                        await _revealSource(sourcePath, sourceLine);
                    break;
                case "grant_cloud_consent":
                    _cloudConsentGranted = true;
                    var pendingRequest = _pendingCloudRequest;
                    var pendingMode = _pendingCloudMode;
                    var pendingTarget = _pendingCloudTargetPath;
                    var pendingSemanticCheck = _pendingSemanticCheck;
                    _pendingCloudRequest = string.Empty;
                    _pendingCloudTargetPath = null;
                    _pendingSemanticCheck = false;
                    SendToWeb(new { type = "cloud_consent_resolved" });
                    if (!string.IsNullOrWhiteSpace(pendingRequest)) await RunAgentAsync(pendingRequest, pendingMode, pendingTarget);
                    else if (pendingSemanticCheck) await RunDocumentCheckAsync(announce: true, semantic: true);
                    break;
                case "deny_cloud_consent":
                    _pendingCloudRequest = string.Empty;
                    _pendingCloudTargetPath = null;
                    var deniedSemanticCheck = _pendingSemanticCheck;
                    _pendingSemanticCheck = false;
                    SendToWeb(new { type = "cloud_consent_resolved" });
                    if (deniedSemanticCheck)
                        SendToWeb(new { type = "toast", message = "已取消云端语义核验，保留本地快速检查结果" });
                    break;
                case "import_requirements":
                    await ImportRequirementsAsync();
                    break;
                case "import_sources":
                    await ImportSourcesAsync();
                    break;
                case "run_check":
                    await RunDocumentCheckAsync(announce: true, semantic: true);
                    break;
                case "export":
                    await ExportCompanionFilesAsync();
                    break;
                case "settings":
                    ShowAgentSettings();
                    break;
                case "select_model":
                    SelectModel(root);
                    break;
                case "save_settings":
                    SaveAgentSettings(root);
                    break;
                case "test_connection":
                    await TestConnectionAsync(root);
                    break;
            }
        }
        catch (Exception exception)
        {
            SendAgentFailure(exception);
            SetBusy(false, string.Empty);
        }
    }

    private async Task RunAgentAsync(string request, string mode, string? requestedTargetPath = null)
    {
        if (string.IsNullOrWhiteSpace(request))
        {
            SendToWeb(new { type = "toast", message = "先描述要完成的文档任务" });
            return;
        }

        try
        {
            if ((_pendingSectionEdit is not null || _pendingFileDraft is not null)
                && AgentDocumentEditService.IsApplyConfirmation(request))
            {
                await ApplyLastAnswerAsync();
                return;
            }
            if ((_pendingSectionEdit is not null || _pendingFileDraft is not null)
                && AgentDocumentEditService.IsApplyCancellation(request))
            {
                _pendingSectionEdit = null;
                _pendingFileDraft = null;
                SendToWeb(new { type = "toast", message = "已取消上一条修改预览，正文没有变化" });
                return;
            }

            await EnsureProjectAsync();
            var targetPath = ResolveTargetDocumentPath(requestedTargetPath);
            if (!string.IsNullOrWhiteSpace(targetPath)
                && !PathEquals(targetPath, _getDocumentPath() ?? string.Empty))
            {
                await _openDocument(targetPath);
            }
            _targetDocumentPath = _getDocumentPath() ?? targetPath;
            if (AgentDocumentEditService.TryGetExportFormat(request, out var exportFormat))
            {
                var opened = await _exportDocument(exportFormat);
                if (opened)
                {
                    SendToWeb(new
                    {
                        type = "assistant",
                        content = $"已打开 {exportFormat.ToUpperInvariant()} 导出窗口。请选择文件名和保存位置，然后点击导出。",
                        sources = Array.Empty<object>(),
                        canApply = false,
                    });
                }
                else
                {
                    SendToWeb(new
                    {
                        type = "assistant",
                        content = "当前没有可导出的文档。请先在编辑区打开目标文档，再重新发送导出指令。",
                        sources = Array.Empty<object>(),
                        canApply = false,
                    });
                }
                return;
            }
            _cancellation = new CancellationTokenSource();
            var targetName = GetTargetDisplayName(_targetDocumentPath);
            SetBusy(true, $"正在读取 {targetName}", cancellable: true);
            var markdown = await _getCurrentMarkdown();
            if (string.IsNullOrWhiteSpace(markdown) && _project.Sources.Count == 0)
                throw new InvalidOperationException("请先打开一份文档，或在“上下文”中添加资料。");

            if (ShouldConfirmCloud() && !_cloudConsentGranted)
            {
                _pendingCloudRequest = request;
                _pendingCloudMode = mode;
                _pendingCloudTargetPath = _targetDocumentPath;
                SendToWeb(new
                {
                    type = "cloud_consent_required",
                    provider = _settings.ProviderName,
                    message = "云端模型需要接收本次任务命中的资料片段。允许后，本次软件运行期间不再重复询问。",
                });
                return;
            }

            if (_agentRuntime is not null)
            {
                _activeRun = await _agentRuntime.StartRunAsync(
                    request.Trim(),
                    ToWorkspaceRelativePath(_targetDocumentPath),
                    _cancellation.Token);
                await _agentRuntime.AddPartAsync(_activeRun, new AgentMessagePart
                {
                    Type = AgentMessagePartType.Progress,
                    Title = "准备上下文",
                    Content = $"正在读取操作主文件“{targetName}”并检索相关资料。",
                }, _cancellation.Token);
            }

            var currentOnly = ProofSourceQueryService.ShouldUseOnlyCurrentDocument(request);
            _lastSources = ProofSourceQueryService.BuildSources(
                _project,
                markdown,
                targetName,
                _targetDocumentPath,
                request.Trim(),
                _settings.MaxSources,
                currentOnly);
            if (_lastSources.Count == 0)
                throw new InvalidOperationException("没有找到可供 Agent 使用的文本资料。");

            SendToWeb(new
            {
                type = "activity",
                label = $"已找到 {_lastSources.Count} 个相关片段",
                detail = $"正在调用 {_settings.Model}",
            });

            var behavior = AgentDocumentEditService.Classify(request, mode);
            _pendingSectionEdit = null;
            _pendingFileDraft = null;
            var scopeInstruction = currentOnly
                ? $"操作主文件是“{targetName}”。本任务只允许依据该文件回答；“当前文档/这个文件”只指它"
                : $"操作主文件是“{targetName}”。其他资料只用于核验或补充，不能取代主文件的任务对象";
            var rewriteContext = string.Empty;
            if (behavior == AgentTaskBehavior.EditPreview
                && AgentDocumentEditService.TryGetTargetSection(
                    markdown,
                    request.Trim(),
                    out var rewriteHeading,
                    out var originalSection))
            {
                var boundedOriginal = originalSection.Length > 6000
                    ? originalSection[..6000] + "\n（原章节过长，此处已截断）"
                    : originalSection;
                rewriteContext = $"。待改写章节是“{rewriteHeading}”，原文如下：\n<original-section>\n{boundedOriginal}\n</original-section>";
            }
            var instruction = behavior switch
            {
                AgentTaskBehavior.PlanOnly => $"{scopeInstruction}。用户明确要求规划。给出精炼、可执行的步骤和必要风险，不要反复询问可从主文件直接判断的问题",
                AgentTaskBehavior.EditPreview => $"{scopeInstruction}{rewriteContext}。直接给出修改预览，不要先输出计划。改写必须重新组织论述顺序、句式和信息层级，改善章节目的、逻辑衔接和表达质量；不得仅复制原文、只插入一条资料、只改标题或只做同义替换。保留可核验事实，不得为了显得变化大而编造内容。把适合直接写入正文的完整章节放进唯一一个 ```markdown 代码块；代码块内不得混入核查过程、风险说明、待补资料或临时来源编号。代码块外再简短说明本次实质改进了什么、依据与风险，不要声称已经写入",
                AgentTaskBehavior.CreateFilePreview => $"{scopeInstruction}。{AgentDocumentEditService.BuildCreateFileGuidance(request.Trim())}把完整文件正文放进唯一一个 ```markdown 代码块；代码块外只简短说明将创建的文件，不要声称已经创建",
                _ => $"{scopeInstruction}。直接完成只读任务，不要输出分步计划，不要改写正文，不要追问可从主文件判断的问题。严格遵守字数、表格或清单要求；找不到证据时明确写缺少证据，绝不补造",
            };
            _lastAnswer = await _client.CompleteAsync(
                _settings.Endpoint,
                _settings.Model,
                _sessionApiKey,
                instruction,
                request.Trim(),
                _lastSources,
                _cancellation.Token);

            var canApply = false;
            if (behavior == AgentTaskBehavior.EditPreview)
            {
                _lastAnswer = AgentDocumentEditService.NormalizePreviewFence(_lastAnswer);
                var parsed = AgentDocumentEditService.TryCreateSectionEdit(
                    markdown,
                    request.Trim(),
                    _lastAnswer,
                    out _pendingSectionEdit);
                var quality = parsed && _pendingSectionEdit is not null
                    ? AgentDocumentEditService.EvaluateSectionEditQuality(markdown, _pendingSectionEdit)
                    : new AgentEditQualityResult(false, "模型没有返回可识别的完整章节预览。", 1);
                canApply = parsed
                    && (!AgentDocumentEditService.RequiresMaterialRewrite(request) || quality.Passed);
                if (!canApply)
                {
                    _pendingSectionEdit = null;
                    _lastAnswer += $"\n\n> 预览未通过改写质量检查：{quality.Message}正文没有变化，也不会显示替换按钮。请补充希望加强的重点后重新生成。";
                }
            }
            if (behavior == AgentTaskBehavior.CreateFilePreview)
            {
                _lastAnswer = AgentDocumentEditService.NormalizePreviewFence(_lastAnswer);
                var root = ResolveProjectRoot();
                canApply = root is not null && AgentDocumentEditService.TryCreateFileDraft(
                    root,
                    request.Trim(),
                    _lastAnswer,
                    out _pendingFileDraft);
                if (!canApply)
                {
                    _pendingFileDraft = null;
                    _lastAnswer += "\n\n> 文件预览格式无法识别，因此没有显示创建按钮。请重新生成；项目文件没有发生变化。";
                }
            }
            if (canApply && _pendingSectionEdit is not null)
            {
                _pendingSectionEdit = _pendingSectionEdit with
                {
                    TargetDocumentPath = _targetDocumentPath,
                    OriginalDocumentHash = ComputeDocumentHash(markdown),
                };
            }

            var displayedSources = SelectReferencedSources(_lastAnswer, _lastSources, currentOnly);

            if (_agentRuntime is not null && _activeRun is not null)
            {
                await _agentRuntime.AddPartAsync(_activeRun, new AgentMessagePart
                {
                    Type = AgentMessagePartType.Text,
                    Content = _lastAnswer,
                }, _cancellation.Token);
                await _agentRuntime.CompleteRunAsync(_activeRun, _lastAnswer, _cancellation.Token);
            }

            SendToWeb(new
            {
                type = "assistant",
                content = _lastAnswer,
                sources = displayedSources.Select(FormatSource).ToArray(),
                canApply,
                applyLabel = _pendingFileDraft is not null
                    ? $"创建“{_pendingFileDraft.DisplayPath}”"
                    : canApply ? $"替换“{_pendingSectionEdit!.TargetHeading}”章节" : string.Empty,
            });
            AddAudit(behavior switch
            {
                AgentTaskBehavior.PlanOnly => "Agent 制定计划",
                AgentTaskBehavior.EditPreview => "Agent 生成修改预览",
                AgentTaskBehavior.CreateFilePreview => "Agent 生成新文件预览",
                _ => "Agent 回答文档问题",
            }, request.Trim(), false);
            await SaveProjectAsync();
        }
        catch (OperationCanceledException) when (_cancellation?.IsCancellationRequested == true)
        {
            if (_agentRuntime is not null && _activeRun is not null)
                await _agentRuntime.CancelRunAsync(_activeRun, CancellationToken.None);
            SendToWeb(new { type = "cancelled", message = "任务已停止，没有修改文档。你可以调整要求后重新发送。" });
        }
        catch (OperationCanceledException exception)
        {
            if (_agentRuntime is not null && _activeRun is not null)
                await _agentRuntime.FailRunAsync(_activeRun, exception, CancellationToken.None);
            SendAgentFailure(exception);
        }
        catch (Exception exception)
        {
            if (_agentRuntime is not null && _activeRun is not null)
                await _agentRuntime.FailRunAsync(_activeRun, exception, CancellationToken.None);
            SendAgentFailure(exception);
        }
        finally
        {
            _activeRun = null;
            _cancellation?.Dispose();
            _cancellation = null;
            SetBusy(false, string.Empty);
        }
    }

    private async Task ApplyLastAnswerAsync()
    {
        if (_pendingFileDraft is not null)
        {
            var draft = _pendingFileDraft;
            if (File.Exists(draft.TargetPath))
            {
                SendToWeb(new { type = "toast", message = $"“{draft.DisplayPath}”已存在，未覆盖；请换一个文件名" });
                _pendingFileDraft = null;
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(draft.TargetPath)!);
            var portableFileMarkdown = PortableCitationService.ConvertAgentSourcesToFootnotes(draft.Markdown, _lastSources);
            await File.WriteAllTextAsync(draft.TargetPath, portableFileMarkdown, Encoding.UTF8);
            AddAudit("人工确认创建文件", draft.DisplayPath, true);
            await SaveProjectAsync();
            await _openDocument(draft.TargetPath);
            SendToWeb(new { type = "applied" });
            SendToWeb(new { type = "toast", message = $"已创建“{draft.DisplayPath}”" });
            _pendingFileDraft = null;
            return;
        }
        if (_pendingSectionEdit is null) return;
        if (!string.IsNullOrWhiteSpace(_pendingSectionEdit.TargetDocumentPath)
            && !PathEquals(_pendingSectionEdit.TargetDocumentPath, _getDocumentPath() ?? string.Empty))
        {
            await _openDocument(_pendingSectionEdit.TargetDocumentPath);
        }
        var currentMarkdown = await _getCurrentMarkdown();
        if (!string.IsNullOrWhiteSpace(_pendingSectionEdit.OriginalDocumentHash)
            && !string.Equals(_pendingSectionEdit.OriginalDocumentHash, ComputeDocumentHash(currentMarkdown), StringComparison.Ordinal))
        {
            SendToWeb(new { type = "toast", message = "预览后正文已经变化，请重新生成修改预览，避免覆盖新内容" });
            _pendingSectionEdit = null;
            return;
        }
        var portableMarkdown = PortableCitationService.ConvertAgentSourcesToFootnotes(
            _pendingSectionEdit.ReplacementMarkdown,
            _lastSources);
        _replaceSection(_pendingSectionEdit.TargetHeading, portableMarkdown);
        AddAudit("人工采纳 Agent 建议", $"替换“{_pendingSectionEdit.TargetHeading}”章节", true);
        await SaveProjectAsync();
        SendToWeb(new { type = "applied" });
        SendToWeb(new { type = "toast", message = $"已替换“{_pendingSectionEdit.TargetHeading}”章节，可在编辑器中撤销" });
        _pendingSectionEdit = null;
        _lastCiResult = null;
    }

    private async Task SelectTargetDocumentAsync(string path)
    {
        if (!File.Exists(path)) return;
        _targetDocumentPath = path;
        _pendingSectionEdit = null;
        _pendingFileDraft = null;
        _lastCiResult = null;
        await _openDocument(path);
        SendState();
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

    private async Task RunDocumentCheckAsync(bool announce, bool semantic = false)
    {
        try
        {
            if (announce) SetBusy(true, semantic ? "正在准备语义检查" : "正在快速检查当前文档");
            await EnsureProjectAsync();
            var markdown = await _getCurrentMarkdown();
            var root = ResolveProjectRoot() ?? Environment.CurrentDirectory;
            _lastCiResult = DocumentCiService.Analyze(markdown, _project, root);
            if (semantic && _project.Requirements.Count > 0)
            {
                if (ShouldConfirmCloud() && !_cloudConsentGranted)
                {
                    _pendingSemanticCheck = true;
                    await SaveProjectAsync();
                    SendState();
                    SendToWeb(new
                    {
                        type = "cloud_consent_required",
                        provider = _settings.ProviderName,
                        message = "语义核验将把当前主文档和已导入的任务要求发送到所配置的云端模型；不会发送其他项目资料。允许后，本次软件运行期间不再重复询问。",
                    });
                    return;
                }

                SetBusy(true, "正在按语义核验要求", cancellable: true);
                _cancellation = new CancellationTokenSource();
                try
                {
                    var assessments = await RequirementSemanticReviewService.ReviewAsync(
                        _client,
                        _settings.Endpoint,
                        _settings.Model,
                        _sessionApiKey,
                        markdown,
                        _project.Requirements,
                        _cloudConsentGranted || !ShouldConfirmCloud(),
                        _cancellation.Token);
                    _lastCiResult = DocumentCiService.ApplySemanticAssessments(
                        _project,
                        _lastCiResult,
                        assessments);
                    AddAudit("语义核验任务要求", $"核验 {_project.Requirements.Count} 项要求", false);
                }
                catch (OperationCanceledException) when (_cancellation?.IsCancellationRequested == true)
                {
                    SendToWeb(new { type = "toast", message = "语义核验已停止，保留本地快速检查结果" });
                }
                catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidDataException or UnauthorizedAccessException or TaskCanceledException)
                {
                    SendToWeb(new
                    {
                        type = "semantic_check_failed",
                        message = $"语义核验失败：{exception.Message} 已保留本地快速检查结果。",
                    });
                }
                finally
                {
                    _cancellation?.Dispose();
                    _cancellation = null;
                }
            }
            await SaveProjectAsync();
            SendState();
            if (announce)
            {
                var label = _lastCiResult.SemanticVerified ? "语义核验完成" : "快速检查完成";
                SendToWeb(new { type = "toast", message = $"{label}，发现 {_lastCiResult.ErrorCount} 项需处理问题" });
            }
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
        await _projectGate.WaitAsync();
        try
        {
            var root = ResolveProjectRoot();
            if (root is null)
            {
                _loadedRoot = null;
                _project = new ProofProject { Title = "尚未打开项目" };
                return;
            }
            if (string.Equals(_loadedRoot, root, StringComparison.OrdinalIgnoreCase)) return;

            var loadedProject = await _store.LoadAsync(root);
            var synchronized = await SynchronizeWorkspaceAsync(root, loadedProject);
            var currentRoot = ResolveProjectRoot();
            if (!string.Equals(currentRoot, root, StringComparison.OrdinalIgnoreCase)) return;
            if (synchronized) await _store.SaveAsync(root, loadedProject);
            var agentRuntime = await AgentRuntime.OpenAsync(root);
            currentRoot = ResolveProjectRoot();
            if (!string.Equals(currentRoot, root, StringComparison.OrdinalIgnoreCase)) return;
            _project = loadedProject;
            _agentRuntime = agentRuntime;
            _loadedRoot = root;
            _lastCiResult = null;
        }
        finally
        {
            _projectGate.Release();
        }
    }

    private async Task SaveProjectAsync()
    {
        await _projectGate.WaitAsync();
        try
        {
            var root = ResolveProjectRoot();
            if (root is not null
                && string.Equals(_loadedRoot, root, StringComparison.OrdinalIgnoreCase))
                await _store.SaveAsync(root, _project);
        }
        finally
        {
            _projectGate.Release();
        }
    }

    private string? ResolveProjectRoot()
    {
        var workspace = _getWorkspaceRoot();
        if (!string.IsNullOrWhiteSpace(workspace) && Directory.Exists(workspace)) return workspace;
        var path = _getDocumentPath();
        var directory = string.IsNullOrWhiteSpace(path) ? null : Path.GetDirectoryName(path);
        return !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory) ? directory : null;
    }

    private string? ToWorkspaceRelativePath(string? path)
    {
        var root = ResolveProjectRoot();
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(path)) return null;
        var relativePath = Path.GetRelativePath(root, path);
        return relativePath.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relativePath)
            ? Path.GetFileName(path)
            : relativePath.Replace('\\', '/');
    }

    private void SendState()
    {
        var root = ResolveProjectRoot();
        var requirements = _project.Requirements.Select(requirement => new
        {
            title = requirement.Title,
            description = requirement.Description,
            covered = _lastCiResult is not null && requirement.IsCovered,
            checkedNow = _lastCiResult is not null,
            coverageState = _lastCiResult is null ? "unchecked" : requirement.CoverageState,
            matchedHeading = requirement.MatchedHeading,
            evidence = requirement.EvidenceText,
            reason = requirement.CoverageReason,
            startLine = requirement.EvidenceStartLine,
            endLine = requirement.EvidenceEndLine,
            confidence = requirement.CoverageConfidence,
            path = _targetDocumentPath ?? _getDocumentPath(),
        }).ToArray();
        var sources = _project.Sources.Select(source => new
        {
            name = source.DisplayName,
            kind = source.Kind,
            status = source.ExtractionStatus,
            trust = source.TrustLevel,
            ready = !string.IsNullOrWhiteSpace(source.ExtractedText),
        }).ToArray();
        var audits = _project.AuditTrail
            .OrderByDescending(item => item.AtUtc)
            .Take(30)
            .Select(item => new
            {
                action = item.Action,
                detail = item.Detail,
                time = item.AtUtc.ToLocalTime().ToString("MM-dd HH:mm"),
                confirmed = item.HumanConfirmed,
            })
            .ToArray();
        var check = _lastCiResult is null
            ? null
            : new
            {
                coverage = _lastCiResult.CoveragePercent,
                evidence = _lastCiResult.EvidencePercent,
                errors = _lastCiResult.ErrorCount,
                verification = _lastCiResult.SemanticVerified ? "semantic" : "quick",
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
            documentName = GetTargetDisplayName(_targetDocumentPath ?? _getDocumentPath()),
            projectName = string.IsNullOrWhiteSpace(root)
                ? string.Empty
                : Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            model = _settings.Model,
            providerName = _settings.ProviderName,
            providerType = _settings.ProviderType,
            endpoint = _settings.Endpoint,
            apiKeyConfigured = !string.IsNullOrWhiteSpace(_sessionApiKey),
            confirmBeforeCloud = _settings.ConfirmBeforeCloud,
            isLocal = IsLocalEndpoint(_settings.Endpoint),
            availableModels = _availableModels
                .Append(_settings.Model)
                .Where(model => !string.IsNullOrWhiteSpace(model))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            documents = EnumerateMarkdownDocuments(root),
            requirements,
            sources,
            audits,
            check,
        });
    }

    private async Task<bool> SynchronizeWorkspaceAsync(string root, ProofProject project)
    {
        var changed = false;
        var currentPath = _getDocumentPath();
        foreach (var path in EnumerateWorkspaceKnowledgeFiles(root).Take(120))
        {
            if (PathEquals(path, currentPath ?? string.Empty)) continue;
            var info = new FileInfo(path);
            if (info.Length > 20 * 1024 * 1024) continue;
            var source = project.Sources.FirstOrDefault(item => PathEquals(item.FilePath, path));
            if (source is not null
                && source.FileModifiedAtUtc == info.LastWriteTimeUtc
                && !string.IsNullOrWhiteSpace(source.ExtractedText))
                continue;

            source ??= new ProofSource { FilePath = path, AddedAtUtc = DateTime.UtcNow };
            if (!project.Sources.Contains(source)) project.Sources.Add(source);
            source.DisplayName = Path.GetRelativePath(root, path).Replace('\\', '/');
            source.Kind = SourceTextExtractor.DetectKind(path);
            source.TrustLevel = GuessTrustLevel(path);
            source.FileModifiedAtUtc = info.LastWriteTimeUtc;
            try
            {
                var (text, status) = await SourceTextExtractor.ExtractAsync(path);
                source.ExtractedText = text;
                source.ExtractionStatus = status;

            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
            {
                source.ExtractedText = string.Empty;
                source.ExtractionStatus = $"索引失败：{exception.Message}";
            }
            changed = true;
        }
        return changed;
    }

    private static IEnumerable<string> EnumerateWorkspaceKnowledgeFiles(string root)
    {
        var supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".md", ".markdown", ".txt", ".pdf", ".docx", ".csv", ".tsv", ".json" };
        var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".git", ".markleaf", "bin", "obj", "node_modules", ".vs" };
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            string[] directories;
            string[] files;
            try
            {
                directories = Directory.GetDirectories(directory);
                files = Directory.GetFiles(directory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (var child in directories)
                if (!ignored.Contains(Path.GetFileName(child))) pending.Push(child);
            foreach (var file in files)
                if (supported.Contains(Path.GetExtension(file))) yield return file;
        }
    }

    private object[] EnumerateMarkdownDocuments(string? root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return [];
        var current = _targetDocumentPath ?? _getDocumentPath();
        return EnumerateWorkspaceKnowledgeFiles(root)
            .Where(path => Path.GetExtension(path).Equals(".md", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(path).Equals(".markdown", StringComparison.OrdinalIgnoreCase))
            .Select(path => new
            {
                name = Path.GetRelativePath(root, path).Replace('\\', '/'),
                path,
                active = PathEquals(path, current ?? string.Empty),
            })
            .Cast<object>()
            .ToArray();
    }

    private object FormatSource(AiSource source)
    {
        var location = !string.IsNullOrWhiteSpace(source.Locator)
            ? source.Locator
            : Path.GetExtension(source.DisplayPath).Equals(".pdf", StringComparison.OrdinalIgnoreCase)
                ? "PDF"
                : source.StartLine == source.EndLine
                    ? $"第 {source.StartLine} 行"
                    : $"第 {source.StartLine}-{source.EndLine} 行";
        return new
        {
            id = source.Id,
            label = $"{source.Id}  {source.DisplayPath}  {location}",
            path = ResolveSourcePath(source.DisplayPath),
            line = source.StartLine,
        };
    }

    private string? ResolveTargetDocumentPath(string? requestedPath)
    {
        if (!string.IsNullOrWhiteSpace(requestedPath) && File.Exists(requestedPath)) return requestedPath;
        if (!string.IsNullOrWhiteSpace(_targetDocumentPath) && File.Exists(_targetDocumentPath)) return _targetDocumentPath;
        return _getDocumentPath();
    }

    private string GetTargetDisplayName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "当前文档";
        var root = ResolveProjectRoot();
        if (!string.IsNullOrWhiteSpace(root))
        {
            try { return Path.GetRelativePath(root, path).Replace('\\', '/'); }
            catch { }
        }
        return Path.GetFileName(path);
    }

    private string ResolveSourcePath(string displayPath)
    {
        var matching = _project.Sources.FirstOrDefault(source =>
            string.Equals(source.DisplayName, displayPath, StringComparison.OrdinalIgnoreCase));
        if (matching is not null && File.Exists(matching.FilePath)) return matching.FilePath;
        var root = ResolveProjectRoot();
        if (string.IsNullOrWhiteSpace(root)) return string.Empty;
        try
        {
            var candidate = Path.GetFullPath(Path.Combine(root, displayPath.Replace('/', Path.DirectorySeparatorChar)));
            return File.Exists(candidate) ? candidate : string.Empty;
        }
        catch { return string.Empty; }
    }

    private static IReadOnlyList<AiSource> SelectReferencedSources(
        string answer,
        IReadOnlyList<AiSource> sources,
        bool currentOnly)
    {
        var ids = Regex.Matches(answer, @"\[(S\d+)\]")
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = sources.Where(source => ids.Contains(source.Id)).ToArray();
        if (selected.Length > 0) return selected;
        return currentOnly && sources.Count > 0 ? [sources[0]] : [];
    }

    private static string ComputeDocumentHash(string markdown) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(markdown ?? string.Empty)));

    private void SetBusy(bool busy, string label, bool cancellable = false)
    {
        UseWaitCursor = busy;
        SendToWeb(new { type = "busy", busy, label, cancellable });
    }

    private void SendError(string message) => SendToWeb(new { type = "error", message });

    private void SendAgentFailure(Exception exception)
    {
        if (exception is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            var local = IsLocalEndpoint(_settings.Endpoint);
            var unauthorized = exception.Message.Contains("401", StringComparison.Ordinal)
                || exception.Message.Contains("403", StringComparison.Ordinal);
            SendToWeb(new
            {
                type = "connection_error",
                title = exception is TaskCanceledException or OperationCanceledException
                    ? "模型响应超时"
                    : unauthorized
                    ? "API 身份验证失败"
                    : local ? "本地模型未连接" : "无法连接模型服务",
                detail = exception is TaskCanceledException or OperationCanceledException
                    ? "模型在 3 分钟内没有返回结果。请先测试连接，确认模型 ID 可用后重试；也可以减少一次发送的资料数量。"
                    : unauthorized
                    ? "请检查 API Key、Base URL 和模型权限。"
                    : local
                        ? "没有连接到 Ollama。请先启动 Ollama，或改用你自己的 API。"
                        : "请检查网络、Base URL 与服务状态，然后重试。",
            });
            return;
        }
        SendError(exception.Message);
    }

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

    private void SelectModel(JsonElement root)
    {
        var model = ReadString(root, "model");
        if (string.IsNullOrWhiteSpace(model)) return;
        _settings.Model = model.Trim();
        _saveSettings();
        ModelChanged?.Invoke(this, _settings.Model);
        SendState();
        SendToWeb(new { type = "toast", message = $"已切换到 {_settings.Model}" });
    }

    private void SaveAgentSettings(JsonElement root)
    {
        var endpoint = ReadString(root, "endpoint").Trim();
        var model = ReadString(root, "model").Trim();
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new ArgumentException("请填写 API Base URL。");
        _ = OpenAiCompatibleClient.BuildModelsUrl(endpoint);
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("请填写模型 ID，或先测试连接并选择模型。");

        _settings.ProviderType = ReadString(root, "providerType") is "ollama"
            ? "ollama"
            : "openai-compatible";
        var providerName = ReadString(root, "providerName").Trim();
        _settings.ProviderName = string.IsNullOrWhiteSpace(providerName)
            ? (_settings.ProviderType == "ollama" ? "Ollama" : "自定义 API")
            : providerName;
        _settings.Endpoint = endpoint;
        _settings.Model = model;
        _settings.PrivacyMode = IsLocalEndpoint(endpoint) ? "local" : "cloud";
        if (root.TryGetProperty("confirmBeforeCloud", out var confirmValue)
            && confirmValue.ValueKind is JsonValueKind.True or JsonValueKind.False)
            _settings.ConfirmBeforeCloud = confirmValue.GetBoolean();

        var apiKey = ReadString(root, "apiKey");
        if (!string.IsNullOrEmpty(apiKey))
        {
            _sessionApiKey = apiKey.Trim();
            _rememberApiKey(_sessionApiKey);
        }
        _saveSettings();
        ModelChanged?.Invoke(this, _settings.Model);
        SendState();
        SendToWeb(new { type = "settings_saved" });
        SendToWeb(new { type = "toast", message = "模型配置已保存" });
    }

    private async Task TestConnectionAsync(JsonElement root)
    {
        var endpoint = ReadString(root, "endpoint").Trim();
        var apiKey = ReadString(root, "apiKey");
        if (string.IsNullOrWhiteSpace(apiKey)) apiKey = _sessionApiKey;
        SendToWeb(new { type = "connection_testing" });
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var models = await _client.ListModelsAsync(endpoint, apiKey, timeout.Token);
            _availableModels = models;
            SendToWeb(new
            {
                type = "connection_result",
                success = true,
                message = models.Count == 0
                    ? "连接成功，但服务没有返回模型列表。你仍可手动填写模型 ID。"
                    : $"连接成功，发现 {models.Count} 个模型。",
                models,
            });
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            var local = IsLocalEndpoint(endpoint);
            SendToWeb(new
            {
                type = "connection_result",
                success = false,
                message = local
                    ? "没有连接到 Ollama。请确认 Ollama 已启动并监听 11434 端口。"
                    : "连接失败。请检查 Base URL、API Key、网络和服务权限。",
            });
        }
    }

    private static string ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static bool IsLocalEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)) return false;
        return uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
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
        return !IsLocalEndpoint(_settings.Endpoint);
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
