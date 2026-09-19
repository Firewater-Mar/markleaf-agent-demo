using MarkLeaf.Commands;
using MarkLeaf.UI.Controls;
using System.Runtime.InteropServices;

namespace MarkLeaf.UI;

internal sealed partial class MainForm
{
    private static readonly bool UseWorkspaceShellChrome = true;
    private readonly WorkspaceNavigationRail _workspaceNavigationRail = new();
    private readonly WorkspaceHeaderBar _workspaceHeaderBar = new();
    private readonly ProjectSidebarHeader _projectSidebarHeader = new();
    private readonly EditorFormatToolbar _editorFormatToolbar = new();

    private void ConfigureWorkspaceChrome()
    {
        _projectSidebarHeader.Clicked += (_, _) => _ = SelectWorkspaceFolderAsync();
        _workspaceNavigationRail.DestinationSelected += (_, destination) =>
            NavigateWorkspace(destination);
        _workspaceHeaderBar.SearchTextChanged += (_, text) =>
            _sidebarSearchBar.SearchText = text;
        _workspaceHeaderBar.DragRequested += (_, _) => BeginWorkspaceWindowDrag();
        _workspaceHeaderBar.ToggleMaximizeRequested += (_, _) => ToggleWorkspaceWindowMaximize();
        _workspaceHeaderBar.WindowCommandRequested += (_, command) =>
        {
            switch (command)
            {
                case WorkspaceWindowCommand.Minimize:
                    WindowState = FormWindowState.Minimized;
                    break;
                case WorkspaceWindowCommand.ToggleMaximize:
                    ToggleWorkspaceWindowMaximize();
                    break;
                case WorkspaceWindowCommand.Close:
                    Close();
                    break;
            }
        };
        SizeChanged += (_, _) => _workspaceHeaderBar.SetMaximized(WindowState == FormWindowState.Maximized);
        LocationChanged += (_, _) =>
        {
            if (WindowState == FormWindowState.Normal) UpdateWorkspaceMaximizedBounds();
        };
        _editorFormatToolbar.CommandRequested += (_, command) => ExecuteCommand(command);
        _editorFormatToolbar.ExportRequested += async (_, _) =>
        {
            var opened = await ScheduleAgentExportAsync(_settings.Export.Format);
            if (!opened) SetStatus("请先打开要导出的文档。");
        };
        _agentPanel.ModelChanged += (_, model) =>
            _workspaceHeaderBar.SetModel(model, IsLocalAiEndpoint());
        _workspaceHeaderBar.SetModel(_settings.Ai.Model, IsLocalAiEndpoint());
        UpdateWorkspaceMaximizedBounds();
        UpdateWorkspaceChromeContext();
    }

    private Control CreateWorkspaceShell(Control workspace)
    {
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = Color.FromArgb(247, 249, 248),
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, this.ScaleForDpi(64)));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, this.ScaleForDpi(47)));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        shell.Controls.Add(_workspaceNavigationRail, 0, 0);
        shell.SetRowSpan(_workspaceNavigationRail, 2);
        shell.Controls.Add(_workspaceHeaderBar, 1, 0);
        shell.Controls.Add(workspace, 1, 1);
        _documentTabBar.SetDisplaySuppressed(true);
        return shell;
    }

    private void NavigateWorkspace(WorkspaceDestination destination)
    {
        switch (destination)
        {
            case WorkspaceDestination.Documents:
                _agentPanel.SwitchPage("agent");
                _editorPanel.Focus();
                break;
            case WorkspaceDestination.Search:
                if (_sidebarSplit.Panel1Collapsed) ExpandSidebar();
                ShowSidebarView(outline: false);
                _workspaceHeaderBar.FocusSearch();
                break;
            case WorkspaceDestination.Sources:
                _agentSplit.Panel2Collapsed = false;
                _agentPanel.SwitchPage("context");
                break;
            case WorkspaceDestination.Tasks:
                _agentSplit.Panel2Collapsed = false;
                _agentPanel.SwitchPage("check");
                break;
            case WorkspaceDestination.Results:
                _agentSplit.Panel2Collapsed = false;
                _agentPanel.SwitchPage("deliver");
                break;
            case WorkspaceDestination.Settings:
                _agentSplit.Panel2Collapsed = false;
                _agentPanel.OpenSettings();
                break;
            case WorkspaceDestination.Help:
                _agentSplit.Panel2Collapsed = false;
                _agentPanel.SwitchPage("help");
                break;
        }
    }

    private void UpdateWorkspaceChromeContext()
    {
        var workspaceName = string.IsNullOrWhiteSpace(_workspaceRoot)
            ? string.Empty
            : Path.GetFileName(_workspaceRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        _projectSidebarHeader.SetProject(workspaceName);
        _workspaceHeaderBar.UpdateContext(workspaceName, _document?.DisplayName, _document?.IsDirty == true);
    }

    private bool IsLocalAiEndpoint()
    {
        if (!Uri.TryCreate(_settings.Ai.Endpoint, UriKind.Absolute, out var uri)) return false;
        return uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
    }

    private void ToggleWorkspaceWindowMaximize()
    {
        UpdateWorkspaceMaximizedBounds();
        WindowState = WindowState == FormWindowState.Maximized
            ? FormWindowState.Normal
            : FormWindowState.Maximized;
        _workspaceHeaderBar.SetMaximized(WindowState == FormWindowState.Maximized);
    }

    private void UpdateWorkspaceMaximizedBounds()
    {
        var screen = Screen.FromHandle(Handle);
        MaximizedBounds = screen.WorkingArea;
    }

    private void BeginWorkspaceWindowDrag()
    {
        if (WindowState == FormWindowState.Maximized)
        {
            var cursorRatio = Math.Clamp((Cursor.Position.X - Left) / (double)Math.Max(1, Width), .15, .85);
            WindowState = FormWindowState.Normal;
            Location = new Point(
                Cursor.Position.X - (int)Math.Round(Width * cursorRatio),
                Math.Max(0, Cursor.Position.Y - this.ScaleForDpi(22)));
        }
        ReleaseCapture();
        SendMessage(Handle, 0x00A1, (nint)2, nint.Zero);
    }

    private bool HandleWorkspaceChromeHitTest(ref Message message)
    {
        if (!UseWorkspaceShellChrome || message.Msg != 0x0084 || WindowState == FormWindowState.Maximized)
            return false;

        var packed = message.LParam.ToInt64();
        var screenPoint = new Point(unchecked((short)(packed & 0xffff)), unchecked((short)((packed >> 16) & 0xffff)));
        var point = PointToClient(screenPoint);
        var edge = this.ScaleForDpi(6);
        var left = point.X < edge;
        var right = point.X >= ClientSize.Width - edge;
        var top = point.Y < edge;
        var bottom = point.Y >= ClientSize.Height - edge;
        message.Result = (nint)(top && left ? 13
            : top && right ? 14
            : bottom && left ? 16
            : bottom && right ? 17
            : left ? 10
            : right ? 11
            : top ? 12
            : bottom ? 15
            : 1);
        return message.Result != (nint)1;
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint window, int message, nint wParam, nint lParam);
}
