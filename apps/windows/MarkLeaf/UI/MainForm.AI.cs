using MarkLeaf.UI.Dialogs;

namespace MarkLeaf.UI;

internal sealed partial class MainForm
{
    private async Task ShowAiAssistantAsync()
    {
        var currentMarkdown = string.Empty;
        if (_editorHost?.IsDocumentLoaded == true)
        {
            try
            {
                currentMarkdown = (await _editorHost.RequestSnapshotAsync()).Markdown;
            }
            catch (Exception exception) when (exception is OperationCanceledException or InvalidOperationException)
            {
                _logger.Warning($"AI assistant could not read the current document: {exception.Message}");
            }
        }

        if (string.IsNullOrWhiteSpace(currentMarkdown)
            && (string.IsNullOrWhiteSpace(_workspaceRoot) || !Directory.Exists(_workspaceRoot)))
        {
            MessageBox.Show(
                this,
                "请先打开一个文档或工作区，再使用 MarkLeaf AI。",
                "MarkLeaf AI",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        using var dialog = new AiAssistantDialog(
            _workspaceRoot,
            _document?.FilePath,
            currentMarkdown,
            _settings.Ai,
            _aiSessionApiKey,
            markdown =>
            {
                if (_editorHost?.IsDocumentLoaded != true) return;
                _editorHost.ExecuteCommand("pasteMarkdown", $"\n\n{markdown.Trim()}\n");
                SetStatus("AI 内容已插入，请复核后保存。");
            },
            apiKey => _aiSessionApiKey = apiKey,
            SaveSettings);
        ShowModal(() => dialog.ShowDialog(this));
    }
}
