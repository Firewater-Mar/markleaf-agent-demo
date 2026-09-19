using MarkLeaf.Services.ExternalLinks;

namespace MarkLeaf.UI;

internal sealed partial class MainForm
{
    private Task ShowAiAssistantAsync()
    {
        _agentSplit.Panel2Collapsed = false;
        _agentPanel.FocusComposer();
        return Task.CompletedTask;
    }

    private async Task RevealAgentSourceAsync(string path, int line)
    {
        if (Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            ExternalLinkService.OpenLocal(path);
            return;
        }

        await OpenDocumentPathAsync(path);
        if (_editorHost?.IsDocumentLoaded != true) return;
        var snapshot = await _editorHost.RequestSnapshotAsync(TimeSpan.FromSeconds(5));
        var normalized = snapshot.Markdown.Replace("\r\n", "\n").Replace('\r', '\n');
        var offset = 0;
        for (var current = 1; current < line && offset < normalized.Length; current++)
        {
            var newline = normalized.IndexOf('\n', offset);
            offset = newline < 0 ? normalized.Length : newline + 1;
        }
        if (!_editorCommandStatus.SourceMode) _editorHost.ExecuteCommand("toggleSourceMode");
        _editorHost.ExecuteCommand("setSourceSelection", $"{offset},{offset}");
        _webView?.Focus();
        SetStatus($"已定位到 {Path.GetFileName(path)} 第 {line} 行");
    }
}
