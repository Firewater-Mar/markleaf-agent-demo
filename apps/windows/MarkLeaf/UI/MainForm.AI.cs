namespace MarkLeaf.UI;

internal sealed partial class MainForm
{
    private Task ShowAiAssistantAsync()
    {
        _agentSplit.Panel2Collapsed = false;
        _agentPanel.FocusComposer();
        return Task.CompletedTask;
    }
}
