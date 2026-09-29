using MarkLeaf.Documents;
using MarkLeaf.Services;
using MarkLeaf.Services.ImportExport;
using MarkLeaf.Services.Typography;
using MarkLeaf.Services.AI;
using MarkLeaf.Services.Settings;
using MarkLeaf.UI.Controls;
using MarkLeaf.UI.Dialogs;

namespace MarkLeaf.UI;

internal sealed partial class MainForm
{
    private SplitContainer CreateAiWorkspaceSplit()
    {
        var templateStore = new TypographyTemplateStore(_paths.DataDirectory);
        _aiPanel = new TypographyAiPanel(
            _settings.Typography,
            new WindowsCredentialAiApiKeyStore(),
            templateStore,
            constraints => PrepareTypographyReviewAsync(constraints),
            (proposal, acceptedChangeIds) => ApplyTypographyReviewAsync(proposal, acceptedChangeIds),
            () => ShowTypographyAssistantAsync(),
            template =>
            {
                _settings.Typography.SelectedTemplateId = template.Id;
                SaveSettings();
                SetStatus($"已启用排版模板“{template.Name}”。");
            },
            SaveSettings,
            (proposal, appliedChangeIds) => UndoTypographyReviewAsync(proposal, appliedChangeIds),
            intent => RunAgentLocalDocumentToolAsync(intent));

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Size = new Size(Math.Max(1280, this.ScaleForDpi(960)), this.ScaleForDpi(740)),
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel2,
            SplitterWidth = 1,
            Panel1MinSize = 480,
            Panel2MinSize = this.ScaleForDpi(340),
            BackColor = Color.FromArgb(220, 225, 221),
        };
        split.Panel1.Controls.Add(_sidebarSplit);
        split.Panel2.Controls.Add(_aiPanel);
        split.HandleCreated += (_, _) => SetSplitterDistanceSafely(
            split, this.ScaleForDpi(_settings.Typography.AiPanelWidth), FixedPanel.Panel2);
        // Use WinForms' preview-line drag behavior for this text-heavy pane.
        // The controls reflow once on mouse-up instead of on every pointer pixel.
        split.SplitterMoved += OnAiSplitterMoved;
        split.Panel2Collapsed = true;
        return split;
    }

    private void OnAiSplitterMoved(object? sender, EventArgs eventArgs)
    {
        if (sender is not SplitContainer split || split.Panel2Collapsed) return;
        var physicalWidth = split.ClientSize.Width - split.SplitterDistance - split.SplitterWidth;
        _settings.Typography.AiPanelWidth = Math.Clamp(
            WindowPlacementCalculator.ToLogicalPixels(physicalWidth, _effectiveDpi), 340, 720);
    }

    private void ToggleAiMode() => SetAiMode(!_aiMode);

    private void SetAiMode(bool enabled)
    {
        if (_aiMode == enabled) return;
        if (enabled && _focusMode) ToggleFocusMode();

        if (enabled)
        {
            _aiSplit.Panel2Collapsed = false;
            SetSplitterDistanceSafely(
                _aiSplit, this.ScaleForDpi(_settings.Typography.AiPanelWidth), FixedPanel.Panel2);
            _aiMode = true;
            _aiPanel.RefreshContext();
            SetStatus(Loc.Get("status.aiModeOn"));
        }
        else
        {
            OnAiSplitterMoved(_aiSplit, EventArgs.Empty);
            _aiSplit.Panel2Collapsed = true;
            _aiMode = false;
            SetStatus(Loc.Get("status.aiModeOff"));
            FocusEditorAfterWindowModeChange();
        }

        _menuService.RefreshStates();
    }

    private async Task<TypographyReviewProposal> PrepareTypographyReviewAsync(TypographyConstraints constraints)
    {
        if (_document is null || _document.IsReadOnly || _editorHost?.IsDocumentLoaded != true)
            throw new InvalidOperationException("当前文档不可编辑。");

        var snapshot = await _editorHost.RequestSnapshotAsync(TimeSpan.FromSeconds(10));
        var measurements = await _editorHost.MeasureTypographyAsync(snapshot.Markdown);
        var store = new TypographyTemplateStore(_paths.DataDirectory);
        var templates = store.LoadAll();
        var template = templates.FirstOrDefault(item =>
            item.Id == _settings.Typography.SelectedTemplateId) ?? templates.First();
        var result = new TypographyFeedbackEngine().Run(
            snapshot.Markdown, template, constraints, measurements);
        var changes = result.Iterations.SelectMany(iteration => iteration.Changes).ToArray();
        return new TypographyReviewProposal(
            result, changes, snapshot.Revision, template.Id, template.Name,
            constraints.AllowNonImprovingChanges, _document.IsDirty);
    }

    private async Task ApplyTypographyReviewAsync(
        TypographyReviewProposal proposal, IReadOnlyCollection<string> acceptedChangeIds)
    {
        if (_document is null || _document.IsReadOnly || _editorHost?.IsDocumentLoaded != true)
            throw new InvalidOperationException("当前文档不可编辑。");

        var snapshot = await _editorHost.RequestSnapshotAsync(TimeSpan.FromSeconds(10));
        if (snapshot.Revision != proposal.SourceRevision
            || !string.Equals(snapshot.Markdown, proposal.Result.OriginalMarkdown, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("生成方案后文档已经发生变化，请重新让 Agent 生成改动。");
        }

        var accepted = proposal.Changes
            .Where(change => acceptedChangeIds.Contains(change.Id))
            .ToArray();
        var markdown = proposal.ResolveAcceptedMarkdown(acceptedChangeIds);
        if (string.Equals(snapshot.Markdown, markdown, StringComparison.Ordinal))
        {
            SetStatus("未应用排版改动。");
            return;
        }

        _document.Markdown = markdown;
        _document.Revision = Math.Max(_document.Revision, snapshot.Revision) + 1;
        _document.IsDirty = true;
        LoadDocumentIntoEditor(_document, restoreScrollPosition: false);
        SetStatus($"已应用 {accepted.Length} 项排版改动。");
    }

    private async Task UndoTypographyReviewAsync(
        TypographyReviewProposal proposal,
        IReadOnlyCollection<string> appliedChangeIds)
    {
        if (_document is null || _document.IsReadOnly || _editorHost?.IsDocumentLoaded != true)
            throw new InvalidOperationException("当前文档不可编辑。");

        var snapshot = await _editorHost.RequestSnapshotAsync(TimeSpan.FromSeconds(10));
        var restored = proposal.RestoreOriginalMarkdown(snapshot.Markdown, appliedChangeIds);
        var appliedVersionWasSaved = !_document.IsDirty;
        _document.Markdown = restored;
        _document.Revision = Math.Max(_document.Revision, snapshot.Revision) + 1;
        _document.IsDirty = appliedVersionWasSaved || proposal.SourceWasDirty;
        LoadDocumentIntoEditor(_document, restoreScrollPosition: false);
        SetStatus("已撤销本次 Agent 排版改动。");
    }

    private async Task ShowTypographyAssistantAsync()
    {
        if (_document is null || _document.IsReadOnly || _editorHost?.IsDocumentLoaded != true) return;
        try
        {
            var snapshot = await _editorHost.RequestSnapshotAsync(TimeSpan.FromSeconds(10));
            var measurements = await _editorHost.MeasureTypographyAsync(snapshot.Markdown);
            using var dialog = new TypographyWorkbenchDialog(snapshot.Markdown,
                new TypographyTemplateStore(_paths.DataDirectory), measurements,
                _settings.Typography.SelectedTemplateId);
            var result = dialog.ShowDialog(this);
            _settings.Typography.SelectedTemplateId = dialog.SelectedTemplateId;
            SaveSettings();
            if (result != DialogResult.OK) return;
            if (snapshot.Markdown == dialog.AcceptedMarkdown) { SetStatus("未应用排版改动。"); return; }
            _document.Markdown = dialog.AcceptedMarkdown; _document.Revision = Math.Max(_document.Revision, snapshot.Revision) + 1; _document.IsDirty = true;
            LoadDocumentIntoEditor(_document, restoreScrollPosition: false);
            SetStatus("已应用所选排版改动。");
        }
        catch (Exception ex) when (ex is TimeoutException or InvalidOperationException or System.Text.Json.JsonException)
        {
            _logger.Warning($"Typography workbench failed: {ex.Message}");
            MessageBox.Show(this, ex.Message, "排版智能体", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task<string?> ImportTextOrHtmlAsync()
    {
        using var picker = new OpenFileDialog { Filter = "可导入文档 (*.txt;*.html;*.htm)|*.txt;*.html;*.htm", Title = "导入为 Markdown" };
        if (picker.ShowDialog(this) != DialogResult.OK) return null;
        try
        {
            var document = new MarkdownDocument { Kind = NewDocumentKind.Markdown,
                Markdown = new SimpleDocumentImportService().Import(picker.FileName), IsDirty = true };
            await AddAndActivateDocumentAsync(document, forceNewTab: true);
            SetStatus($"已导入 {Path.GetFileName(picker.FileName)}，请另存为 Markdown。");
            return picker.FileName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            MessageBox.Show(this, ex.Message, "导入失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }
    }

    private async Task<AgentLocalToolResult> RunAgentLocalDocumentToolAsync(TypographyAgentIntent intent)
    {
        if (intent == TypographyAgentIntent.ImportDocument)
        {
            var importedPath = await ImportTextOrHtmlAsync();
            return importedPath is null
                ? AgentLocalToolResult.Cancelled("没有选择可导入的文件，因此未创建新文档。")
                : AgentLocalToolResult.Success(
                    $"已导入 {Path.GetFileName(importedPath)} 并转换为 Markdown；结果已在新标签页打开。");
        }

        if (_editorHost?.IsDocumentLoaded != true || _document is null)
            throw new InvalidOperationException("当前没有可操作的文档。");

        if (intent == TypographyAgentIntent.Print)
        {
            PrintDocument();
            return AgentLocalToolResult.Success("已打开系统打印界面；文档正文没有修改。");
        }

        if (intent == TypographyAgentIntent.ExportMarkdown)
        {
            var saved = await SaveDocumentAsync(saveAs: true);
            return saved && !string.IsNullOrWhiteSpace(_document.FilePath)
                ? AgentLocalToolResult.Success($"Markdown 已保存到：\n{_document.FilePath}")
                : AgentLocalToolResult.Cancelled("没有选择保存位置，因此未生成 Markdown 文件。");
        }

        var defaultName = _document.FilePath is not null
            ? Path.GetFileNameWithoutExtension(_document.FilePath)
            : Loc.Get("common.unnamed");
        var format = intent switch
        {
            TypographyAgentIntent.ExportPdf => "pdf",
            TypographyAgentIntent.ExportImage => "image",
            TypographyAgentIntent.ExportHtml => "html",
            _ => throw new InvalidOperationException("当前工具不属于可执行的导出类型。"),
        };
        var outputPath = PromptExportPath(format, defaultName);
        if (string.IsNullOrWhiteSpace(outputPath))
            return AgentLocalToolResult.Cancelled("你取消了保存位置选择，因此没有生成文件。文档正文没有修改。");

        var options = BuildLastExportOptions(outputPath) with { Format = format };
        SaveLastExportOptions(options);
        var exportedPath = await RunExportAsync(options, defaultName, showCompletionDialog: false);
        return string.IsNullOrWhiteSpace(exportedPath)
            ? AgentLocalToolResult.Cancelled("导出没有完成，未生成文件；文档正文没有修改。")
            : AgentLocalToolResult.Success($"{format.ToUpperInvariant()} 已导出到：\n{exportedPath}");
    }
}
