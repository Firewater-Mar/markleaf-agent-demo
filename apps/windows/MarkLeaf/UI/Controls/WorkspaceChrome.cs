using System.Drawing.Drawing2D;
using MarkLeaf.Commands;

namespace MarkLeaf.UI.Controls;

internal enum WorkspaceDestination
{
    Documents,
    Search,
    Sources,
    Tasks,
    Settings,
    Help,
    Results,
}

internal enum WorkspaceWindowCommand
{
    Minimize,
    ToggleMaximize,
    Close,
}

internal sealed class WorkspaceNavigationRail : Control
{
    private sealed record Item(WorkspaceDestination Destination, string Icon, string Label, Rectangle Bounds);

    private readonly Font _iconFont = new(SystemIconProvider.IconFontName, 14F);
    private readonly Font _labelFont = new("Microsoft YaHei UI", 7.5F, FontStyle.Regular);
    private readonly Image? _brandMark;
    private WorkspaceDestination _selected = WorkspaceDestination.Documents;
    private WorkspaceDestination? _hovered;
    private IReadOnlyList<Item> _items = [];

    public WorkspaceNavigationRail()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Dock = DockStyle.Fill;
        TabStop = true;
        AccessibleName = "主功能导航";
        var logoPath = Path.Combine(AppContext.BaseDirectory, "Resources", "App", "App.png");
        if (File.Exists(logoPath))
        {
            try { _brandMark = Image.FromFile(logoPath); }
            catch { _brandMark = null; }
        }
    }

    public event EventHandler<WorkspaceDestination>? DestinationSelected;

    public void SelectDestination(WorkspaceDestination destination)
    {
        _selected = destination;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        e.Graphics.Clear(Color.FromArgb(4, 101, 77));

        var logoSize = ScalePx(42);
        var logoBounds = new Rectangle((ClientSize.Width - logoSize) / 2, ScalePx(7), logoSize, logoSize);
        if (_brandMark is not null)
            e.Graphics.DrawImage(_brandMark, logoBounds);

        var top = ScalePx(61);
        var itemHeight = ScalePx(56);
        var gap = ScalePx(3);
        var side = ScalePx(6);
        var definitions = new[]
        {
            (WorkspaceDestination.Documents, "\uE8A5", "编辑"),
            (WorkspaceDestination.Search, "\uE721", "搜索"),
            (WorkspaceDestination.Sources, "\uE8B7", "资料库"),
            (WorkspaceDestination.Tasks, "\uE9D5", "任务"),
            (WorkspaceDestination.Settings, "\uE713", "设置"),
            (WorkspaceDestination.Help, "\uE897", "帮助"),
        };
        var items = new List<Item>(definitions.Length + 1);
        foreach (var definition in definitions)
        {
            var bounds = new Rectangle(side, top, ClientSize.Width - side * 2, itemHeight);
            items.Add(new Item(definition.Item1, definition.Item2, definition.Item3, bounds));
            top += itemHeight + gap;
        }

        var resultBounds = new Rectangle(
            side,
            Math.Max(top, ClientSize.Height - itemHeight - ScalePx(8)),
            ClientSize.Width - side * 2,
            itemHeight);
        items.Add(new Item(WorkspaceDestination.Results, "\uE9D2", "交付", resultBounds));
        _items = items;

        foreach (var item in items)
        {
            var active = item.Destination == _selected;
            var hovered = item.Destination == _hovered;
            if (active || hovered)
            {
                using var fill = new SolidBrush(active
                    ? Color.FromArgb(0, 77, 58)
                    : Color.FromArgb(20, 117, 92));
                SidebarGdi.FillRoundedRect(e.Graphics, item.Bounds, ScalePx(9), fill);
            }

            var iconBounds = new Rectangle(
                item.Bounds.X,
                item.Bounds.Y + ScalePx(6),
                item.Bounds.Width,
                ScalePx(23));
            var labelBounds = new Rectangle(
                item.Bounds.X,
                item.Bounds.Y + ScalePx(31),
                item.Bounds.Width,
                ScalePx(17));
            TextRenderer.DrawText(e.Graphics, item.Icon, _iconFont, iconBounds, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            TextRenderer.DrawText(e.Graphics, item.Label, _labelFont, labelBounds, Color.FromArgb(239, 249, 245),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var next = _items.FirstOrDefault(item => item.Bounds.Contains(e.Location))?.Destination;
        if (next == _hovered) return;
        _hovered = next;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = null;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        var item = _items.FirstOrDefault(candidate => candidate.Bounds.Contains(e.Location));
        if (item is null) return;
        _selected = item.Destination;
        Invalidate();
        DestinationSelected?.Invoke(this, item.Destination);
    }

    private int ScalePx(int logical) => Math.Max(1, (int)Math.Round(logical * DeviceDpi / 96F));

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _iconFont.Dispose();
            _labelFont.Dispose();
            _brandMark?.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class WorkspaceHeaderBar : UserControl
{
    private readonly Label _brand = new();
    private readonly Label _context = new();
    private readonly Label _saved = new();
    private readonly WorkspaceSearchBox _search = new();
    private readonly WorkspaceModelButton _model = new();
    private readonly HeaderWindowButton _minimize = new(HeaderWindowButtonKind.Minimize);
    private readonly HeaderWindowButton _maximize = new(HeaderWindowButtonKind.Maximize);
    private readonly HeaderWindowButton _close = new(HeaderWindowButtonKind.Close);

    public WorkspaceHeaderBar()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(251, 252, 251);
        Padding = Padding.Empty;

        _brand.Text = "MarkLeaf Agent";
        _brand.Font = new Font("Segoe UI Variable Display", 10.5F, FontStyle.Bold);
        _brand.ForeColor = Color.FromArgb(22, 33, 29);
        _brand.AutoSize = false;
        _brand.TextAlign = ContentAlignment.MiddleLeft;

        _context.Font = new Font("Microsoft YaHei UI", 8.5F);
        _context.ForeColor = Color.FromArgb(84, 99, 92);
        _context.AutoEllipsis = true;
        _context.TextAlign = ContentAlignment.MiddleLeft;

        _saved.Text = "已自动保存";
        _saved.Font = new Font("Microsoft YaHei UI", 8F);
        _saved.ForeColor = Color.FromArgb(70, 126, 106);
        _saved.TextAlign = ContentAlignment.MiddleRight;

        _search.SearchTextChanged += (_, text) => SearchTextChanged?.Invoke(this, text);
        _minimize.Clicked += (_, _) => WindowCommandRequested?.Invoke(this, WorkspaceWindowCommand.Minimize);
        _maximize.Clicked += (_, _) => WindowCommandRequested?.Invoke(this, WorkspaceWindowCommand.ToggleMaximize);
        _close.Clicked += (_, _) => WindowCommandRequested?.Invoke(this, WorkspaceWindowCommand.Close);

        Controls.AddRange([_brand, _context, _saved, _search, _model, _minimize, _maximize, _close]);
        Resize += (_, _) => Arrange();
        MouseDown += HeaderMouseDown;
        MouseDoubleClick += HeaderMouseDoubleClick;
        _brand.MouseDown += HeaderMouseDown;
        _context.MouseDown += HeaderMouseDown;
        _saved.MouseDown += HeaderMouseDown;
        Arrange();
    }

    public event EventHandler<string>? SearchTextChanged;
    public event EventHandler? DragRequested;
    public event EventHandler? ToggleMaximizeRequested;
    public event EventHandler<WorkspaceWindowCommand>? WindowCommandRequested;

    public void UpdateContext(string? workspace, string? document, bool dirty)
    {
        var project = string.IsNullOrWhiteSpace(workspace) ? "未打开项目" : workspace;
        var file = string.IsNullOrWhiteSpace(document) ? "新建文档" : document;
        _context.Text = $"{project}   /   {file}";
        _saved.Text = dirty ? "有未保存修改" : "已自动保存";
        _saved.ForeColor = dirty ? Color.FromArgb(167, 101, 32) : Color.FromArgb(70, 126, 106);
    }

    public void SetModel(string model, bool local) => _model.SetModel(model, local);

    public void FocusSearch() => _search.FocusInput();

    public void SetMaximized(bool maximized) => _maximize.SetMaximized(maximized);

    private void Arrange()
    {
        var s = DeviceDpi / 96F;
        int S(int value) => Math.Max(1, (int)Math.Round(value * s));
        var height = ClientSize.Height;
        if (height <= 0) return;

        var captionWidth = S(46);
        _close.Bounds = new Rectangle(ClientSize.Width - captionWidth, 0, captionWidth, height);
        _maximize.Bounds = new Rectangle(_close.Left - captionWidth, 0, captionWidth, height);
        _minimize.Bounds = new Rectangle(_maximize.Left - captionWidth, 0, captionWidth, height);

        var modelWidth = S(145);
        var searchWidth = S(238);
        var gap = S(10);
        var right = _minimize.Left - S(14);
        _model.Bounds = new Rectangle(right - modelWidth, S(7), modelWidth, Math.Max(S(32), height - S(14)));
        right = _model.Left - gap;
        _search.Bounds = new Rectangle(right - searchWidth, S(7), searchWidth, Math.Max(S(32), height - S(14)));
        right = _search.Left - S(16);
        _saved.Bounds = new Rectangle(right - S(104), 0, S(104), height);

        _brand.Bounds = new Rectangle(S(16), 0, S(154), height);
        var contextLeft = _brand.Right + S(10);
        _context.Bounds = new Rectangle(contextLeft, 0, Math.Max(0, _saved.Left - S(18) - contextLeft), height);
    }

    private void HeaderMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) DragRequested?.Invoke(this, EventArgs.Empty);
    }

    private void HeaderMouseDoubleClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) ToggleMaximizeRequested?.Invoke(this, EventArgs.Empty);
    }
}

internal sealed class WorkspaceSearchBox : UserControl
{
    private readonly TextBox _input = new();

    public WorkspaceSearchBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.IBeam;
        _input.BorderStyle = BorderStyle.None;
        _input.BackColor = Color.FromArgb(247, 249, 248);
        _input.ForeColor = Color.FromArgb(35, 49, 43);
        _input.Font = new Font("Microsoft YaHei UI", 8.5F);
        _input.PlaceholderText = "搜索文件或命令";
        _input.TextChanged += (_, _) => SearchTextChanged?.Invoke(this, _input.Text);
        _input.GotFocus += (_, _) => Invalidate();
        _input.LostFocus += (_, _) => Invalidate();
        Controls.Add(_input);
        Resize += (_, _) => ArrangeInput();
        Click += (_, _) => _input.Focus();
    }

    public event EventHandler<string>? SearchTextChanged;

    public void FocusInput()
    {
        _input.Focus();
        _input.SelectAll();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        using var fill = new SolidBrush(Color.FromArgb(247, 249, 248));
        using var border = new Pen(_input.Focused ? Color.FromArgb(104, 137, 125) : Color.FromArgb(218, 225, 221));
        SidebarGdi.FillRoundedRect(e.Graphics, rect, ScalePx(8), fill);
        using var path = RoundedPath(rect, ScalePx(8));
        e.Graphics.DrawPath(border, path);

        using var iconPen = new Pen(Color.FromArgb(101, 116, 109), Math.Max(1.2F, DeviceDpi / 96F * 1.2F));
        var cx = ScalePx(15);
        var cy = Height / 2 - ScalePx(2);
        var radius = ScalePx(5);
        e.Graphics.DrawEllipse(iconPen, cx - radius, cy - radius, radius * 2, radius * 2);
        e.Graphics.DrawLine(iconPen, cx + radius - ScalePx(1), cy + radius - ScalePx(1), cx + radius + ScalePx(4), cy + radius + ScalePx(4));
    }

    private void ArrangeInput()
    {
        var left = ScalePx(31);
        _input.Bounds = new Rectangle(left, Math.Max(1, (Height - _input.PreferredHeight) / 2), Math.Max(0, Width - left - ScalePx(9)), _input.PreferredHeight);
    }

    private int ScalePx(int logical) => Math.Max(1, (int)Math.Round(logical * DeviceDpi / 96F));

    private static GraphicsPath RoundedPath(Rectangle rect, int radius)
    {
        var diameter = Math.Min(Math.Min(rect.Width, rect.Height), radius * 2);
        var path = new GraphicsPath();
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class WorkspaceModelButton : Control
{
    private string _model = "本地模型";
    private bool _local = true;

    public WorkspaceModelButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Default;
        TabStop = false;
        AccessibleName = "模型连接状态";
    }

    public void SetModel(string model, bool local)
    {
        _local = local;
        _model = local ? "本地模型" : "云端已配置";
        AccessibleDescription = _local ? "已选择本地模型配置" : "已选择云端模型配置；实际连接状态以测试结果为准";
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        using var fill = new SolidBrush(Color.FromArgb(240, 247, 244));
        using var border = new Pen(Color.FromArgb(199, 216, 208));
        SidebarGdi.FillRoundedRect(e.Graphics, rect, ScalePx(8), fill);
        using var path = RoundedPath(rect, ScalePx(8));
        e.Graphics.DrawPath(border, path);

        using var dot = new SolidBrush(_local ? Color.FromArgb(17, 121, 91) : Color.FromArgb(73, 104, 151));
        e.Graphics.FillEllipse(dot, ScalePx(11), Height / 2 - ScalePx(3), ScalePx(6), ScalePx(6));
        var textRect = new Rectangle(ScalePx(23), 0, Math.Max(0, Width - ScalePx(31)), Height);
        using var modelFont = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular);
        TextRenderer.DrawText(e.Graphics, _model, modelFont, textRect,
            Color.FromArgb(31, 49, 42), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }

    private int ScalePx(int logical) => Math.Max(1, (int)Math.Round(logical * DeviceDpi / 96F));
    private static GraphicsPath RoundedPath(Rectangle rect, int radius)
    {
        var diameter = Math.Min(Math.Min(rect.Width, rect.Height), radius * 2);
        var path = new GraphicsPath();
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal enum HeaderWindowButtonKind { Minimize, Maximize, Close }

internal sealed class HeaderWindowButton : Control
{
    private readonly HeaderWindowButtonKind _kind;
    private bool _hovered;
    private bool _pressed;
    private bool _isMaximized;

    public HeaderWindowButton(HeaderWindowButtonKind kind)
    {
        _kind = kind;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Cursor = Cursors.Default;
        TabStop = true;
        AccessibleName = kind switch
        {
            HeaderWindowButtonKind.Minimize => "最小化",
            HeaderWindowButtonKind.Maximize => "最大化或还原",
            _ => "关闭",
        };
    }

    public event EventHandler? Clicked;

    public void SetMaximized(bool maximized)
    {
        _isMaximized = maximized;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_hovered || _pressed)
        {
            using var fill = new SolidBrush(_kind == HeaderWindowButtonKind.Close
                ? (_pressed ? Color.FromArgb(190, 42, 46) : Color.FromArgb(225, 68, 72))
                : (_pressed ? Color.FromArgb(224, 229, 226) : Color.FromArgb(239, 242, 240)));
            e.Graphics.FillRectangle(fill, ClientRectangle);
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(_kind == HeaderWindowButtonKind.Close && _hovered ? Color.White : Color.FromArgb(42, 52, 48), Math.Max(1F, DeviceDpi / 96F));
        var cx = Width / 2;
        var cy = Height / 2;
        var half = Math.Max(4, (int)Math.Round(5 * DeviceDpi / 96F));
        switch (_kind)
        {
            case HeaderWindowButtonKind.Minimize:
                e.Graphics.DrawLine(pen, cx - half, cy + half / 2, cx + half, cy + half / 2);
                break;
            case HeaderWindowButtonKind.Maximize:
                if (_isMaximized)
                {
                    e.Graphics.DrawRectangle(pen, cx - half + 2, cy - half, half * 2 - 2, half * 2 - 2);
                    e.Graphics.DrawRectangle(pen, cx - half, cy - half + 2, half * 2 - 2, half * 2 - 2);
                }
                else
                {
                    e.Graphics.DrawRectangle(pen, cx - half, cy - half, half * 2, half * 2);
                }
                break;
            case HeaderWindowButtonKind.Close:
                e.Graphics.DrawLine(pen, cx - half, cy - half, cx + half, cy + half);
                e.Graphics.DrawLine(pen, cx + half, cy - half, cx - half, cy + half);
                break;
        }
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { var clicked = _pressed && ClientRectangle.Contains(e.Location); _pressed = false; Invalidate(); if (clicked) Clicked?.Invoke(this, EventArgs.Empty); base.OnMouseUp(e); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode is Keys.Enter or Keys.Space) { Clicked?.Invoke(this, EventArgs.Empty); e.Handled = true; } base.OnKeyDown(e); }
}

internal sealed class ProjectSidebarHeader : UserControl
{
    private readonly Label _title = new();
    private readonly Label _subtitle = new();

    public ProjectSidebarHeader()
    {
        Dock = DockStyle.Fill;
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleName = "切换项目文件夹";
        AccessibleDescription = "打开其他项目文件夹，快捷键 Ctrl+Shift+O";
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(250, 252, 251);
        Padding = Padding.Empty;
        _title.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
        _title.ForeColor = Color.FromArgb(22, 33, 29);
        _title.Text = "项目方案  ⌄";
        _title.AutoEllipsis = true;
        _title.TextAlign = ContentAlignment.BottomLeft;
        _subtitle.Font = new Font("Microsoft YaHei UI", 8F);
        _subtitle.ForeColor = Color.FromArgb(100, 114, 108);
        _subtitle.Text = "Markdown 文档与资料工作区";
        _subtitle.AutoEllipsis = true;
        _subtitle.TextAlign = ContentAlignment.TopLeft;
        Controls.AddRange([_title, _subtitle]);
        _title.Cursor = Cursors.Hand;
        _subtitle.Cursor = Cursors.Hand;
        _title.Click += (_, _) => Clicked?.Invoke(this, EventArgs.Empty);
        _subtitle.Click += (_, _) => Clicked?.Invoke(this, EventArgs.Empty);
        Click += (_, _) => Clicked?.Invoke(this, EventArgs.Empty);
        Resize += (_, _) => ArrangeLabels();
        ArrangeLabels();
    }

    public event EventHandler? Clicked;

    public void SetProject(string? name)
        => _title.Text = string.IsNullOrWhiteSpace(name) ? "打开项目  ⌄" : $"{name}  ⌄";

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            Clicked?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    private void ArrangeLabels()
    {
        var scale = DeviceDpi / 96F;
        int S(int value) => Math.Max(1, (int)Math.Round(value * scale));
        var left = S(16);
        var width = Math.Max(0, ClientSize.Width - left - S(12));
        _title.Bounds = new Rectangle(left, S(8), width, S(29));
        _subtitle.Bounds = new Rectangle(left, S(38), width, S(22));
    }
}

internal sealed class EditorFormatToolbar : UserControl
{
    private readonly FlowLayoutPanel _flow = new();
    private readonly ToolTip _toolTip = new();
    private readonly Font _iconFont = new(SystemIconProvider.IconFontName, 12F);

    public EditorFormatToolbar()
    {
        Dock = DockStyle.Top;
        AutoScaleMode = AutoScaleMode.None;
        Height = 50;
        BackColor = Color.White;
        Padding = new Padding(10, 6, 8, 6);
        _flow.Dock = DockStyle.Fill;
        _flow.WrapContents = false;
        _flow.AutoScroll = false;
        _flow.BackColor = Color.White;
        Controls.Add(_flow);

        var format = CreateIconButton("\uE8D2", "段落与标题样式", 40);
        var formatMenu = new ContextMenuStrip();
        AddMenu(formatMenu, "正文", AppCommand.SetParagraph);
        AddMenu(formatMenu, "标题 1", AppCommand.SetHeading1);
        AddMenu(formatMenu, "标题 2", AppCommand.SetHeading2);
        AddMenu(formatMenu, "标题 3", AppCommand.SetHeading3);
        format.Click += (_, _) => formatMenu.Show(format, new Point(0, format.Height));
        _flow.Controls.Add(format);

        AddSeparator();
        AddButton("\uE7A7", "撤销", AppCommand.Undo);
        AddButton("\uE7A6", "重做", AppCommand.Redo);
        AddSeparator();
        AddButton("\uE8DD", "粗体", AppCommand.ToggleBold);
        AddButton("\uE8DB", "斜体", AppCommand.ToggleItalic);
        AddButton("\uE8DC", "下划线", AppCommand.ToggleUnderline);
        AddButton("\uEDE0", "删除线", AppCommand.ToggleStrike);
        AddButton("\uE943", "行内代码", AppCommand.ToggleInlineCode);
        AddSeparator();
        AddButton("\uE71B", "插入链接", AppCommand.InsertLink);
        AddButton("\uE91B", "插入图片", AppCommand.InsertImage);
        AddButton("\uE8A9", "插入表格", AppCommand.InsertTable);
        AddSeparator();
        AddButton("\uE8FD", "项目符号列表", AppCommand.ToggleBulletList);
        AddButton("\uE8EF", "编号列表", AppCommand.ToggleOrderedList);
        AddSeparator();
        AddButton("\uE8B2", "引用", AppCommand.ToggleQuote);
        AddButton("\uE9D5", "任务列表", AppCommand.ToggleTaskList);
        AddButton("\uE7F8", "插入分隔线", AppCommand.InsertHorizontalRule);
        AddSeparator();
        var export = CreateIconButton(SystemIconProvider.ExportIcon, "导出文档", 38);
        export.Click += (_, _) => ExportRequested?.Invoke(this, EventArgs.Empty);
        _flow.Controls.Add(export);

        HandleCreated += (_, _) => ApplyDpiMetrics();
        DpiChangedAfterParent += (_, _) => ApplyDpiMetrics();
        Resize += (_, _) => FitSingleRowToWidth();
    }

    public event EventHandler<AppCommand>? CommandRequested;

    public event EventHandler? ExportRequested;

    private void AddButton(string icon, string label, AppCommand command)
    {
        var button = CreateIconButton(icon, label, 34);
        button.Click += (_, _) => CommandRequested?.Invoke(this, command);
        _flow.Controls.Add(button);
    }

    private void AddSeparator()
    {
        var separator = new Panel
        {
            Width = 1,
            Height = 19,
            Margin = new Padding(5, 7, 5, 0),
            BackColor = Color.FromArgb(224, 230, 227),
            TabStop = false,
            Tag = "separator",
        };
        _flow.Controls.Add(separator);
    }

    private Button CreateIconButton(string icon, string label, int width)
    {
        var button = new Button
        {
            Text = icon,
            Width = width,
            Height = 34,
            Margin = new Padding(1, 1, 1, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(42, 55, 49),
            Cursor = Cursors.Hand,
            TabStop = true,
            AccessibleName = label,
            Font = _iconFont,
            Tag = width,
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(242, 247, 245);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(229, 241, 236);
        _toolTip.SetToolTip(button, label);
        return button;
    }

    private void ApplyDpiMetrics()
    {
        var scale = DeviceDpi / 96F;
        var horizontalScale = Math.Min(scale, 1.25F);
        int S(int value) => Math.Max(1, (int)Math.Round(value * scale));
        int SX(int value) => Math.Max(1, (int)Math.Round(value * horizontalScale));
        Height = S(50);
        Padding = new Padding(SX(8), S(6), SX(6), S(6));
        foreach (Control control in _flow.Controls)
        {
            if (control is Button button && button.Tag is int logicalWidth)
            {
                button.Size = new Size(SX(logicalWidth), S(36));
                button.Margin = new Padding(SX(1), S(1), SX(1), 0);
            }
            else if (string.Equals(control.Tag as string, "separator", StringComparison.Ordinal))
            {
                control.Size = new Size(S(1), S(22));
                control.Margin = new Padding(SX(3), S(7), SX(3), 0);
            }
        }
        FitSingleRowToWidth();
    }

    private void FitSingleRowToWidth()
    {
        if (!IsHandleCreated || _flow.ClientSize.Width <= 0) return;
        var buttons = _flow.Controls.OfType<Button>().ToArray();
        if (buttons.Length == 0) return;

        var occupiedBySeparators = _flow.Controls
            .Cast<Control>()
            .Where(control => control is not Button)
            .Sum(control => control.Width + control.Margin.Horizontal);
        var buttonMargins = buttons.Sum(button => button.Margin.Horizontal);
        var availableForButtons = Math.Max(
            buttons.Length * 28,
            _flow.ClientSize.Width - occupiedBySeparators - buttonMargins - 2);
        var equalBudget = Math.Max(28, availableForButtons / buttons.Length);
        var horizontalScale = Math.Min(DeviceDpi / 96F, 1.25F);

        foreach (var button in buttons)
        {
            var logicalWidth = button.Tag is int taggedWidth ? taggedWidth : 34;
            var preferredWidth = Math.Max(28, (int)Math.Round(logicalWidth * horizontalScale));
            button.Width = Math.Min(preferredWidth, equalBudget);
        }
    }

    private void AddMenu(ContextMenuStrip menu, string text, AppCommand command)
    {
        var item = menu.Items.Add(text);
        item.Click += (_, _) => CommandRequested?.Invoke(this, command);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _toolTip.Dispose();
            _iconFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
