using MarkLeaf.UI.Controls;
using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace MarkLeaf.UI.Proof;

internal static class ProofPalette
{
    public static readonly Color Canvas = Color.FromArgb(246, 248, 247);
    public static readonly Color Sidebar = Color.FromArgb(235, 240, 238);
    public static readonly Color Surface = Color.FromArgb(255, 255, 255);
    public static readonly Color SurfaceMuted = Color.FromArgb(240, 244, 242);
    public static readonly Color Border = Color.FromArgb(216, 224, 220);
    public static readonly Color Ink = Color.FromArgb(29, 42, 38);
    public static readonly Color Muted = Color.FromArgb(91, 107, 101);
    public static readonly Color Accent = Color.FromArgb(25, 111, 91);
    public static readonly Color AccentHover = Color.FromArgb(19, 92, 75);
    public static readonly Color AccentSoft = Color.FromArgb(216, 237, 230);
    public static readonly Color Danger = Color.FromArgb(176, 62, 58);
    public static readonly Color Warning = Color.FromArgb(171, 105, 34);
    public static readonly Color Info = Color.FromArgb(54, 99, 145);
}

internal sealed class ProofNavButton : Button
{
    private bool _selected;

    public ProofNavButton(string text)
    {
        Text = text;
        Dock = DockStyle.Top;
        Height = 44;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        TextAlign = ContentAlignment.MiddleLeft;
        Padding = new Padding(18, 0, 8, 0);
        Font = new Font("Segoe UI Variable Text", 10F, FontStyle.Regular);
        Cursor = Cursors.Hand;
        BackColor = ProofPalette.Sidebar;
        ForeColor = ProofPalette.Ink;
        TabStop = true;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            BackColor = value ? ProofPalette.AccentSoft : ProofPalette.Sidebar;
            Font = new Font(Font, value ? FontStyle.Bold : FontStyle.Regular);
            Invalidate();
        }
    }

    protected override void OnMouseEnter(EventArgs eventArgs)
    {
        base.OnMouseEnter(eventArgs);
        if (!Selected) BackColor = Color.FromArgb(225, 233, 230);
    }

    protected override void OnMouseLeave(EventArgs eventArgs)
    {
        base.OnMouseLeave(eventArgs);
        BackColor = Selected ? ProofPalette.AccentSoft : ProofPalette.Sidebar;
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        if (!Selected) return;
        using var brush = new SolidBrush(ProofPalette.Accent);
        eventArgs.Graphics.FillRectangle(brush, 0, 7, 4, Height - 14);
    }
}

internal class ProofCard : Panel
{
    public ProofCard()
    {
        BackColor = ProofPalette.Surface;
        Padding = new Padding(18);
        Margin = new Padding(0, 0, 14, 14);
        DoubleBuffered = true;
    }

    protected override void OnResize(EventArgs eventArgs)
    {
        base.OnResize(eventArgs);
        using var path = SidebarGdi.CreateRoundedRect(new Rectangle(0, 0, Math.Max(1, Width), Math.Max(1, Height)), 12);
        Region = new Region(path);
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using var pen = new Pen(ProofPalette.Border);
        SidebarGdi.DrawRoundedRect(eventArgs.Graphics, bounds, 12, pen);
    }
}

internal sealed class ProofProgressRing : Control
{
    private int _value;

    public ProofProgressRing()
    {
        Size = new Size(72, 72);
        MinimumSize = Size;
        Font = new Font("Segoe UI Variable Display", 13F, FontStyle.Bold);
        ForeColor = ProofPalette.Ink;
        DoubleBuffered = true;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, 0, 100);
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var stroke = Math.Max(5, Width / 12);
        var bounds = new Rectangle(stroke, stroke, Width - stroke * 2 - 1, Height - stroke * 2 - 1);
        using var track = new Pen(ProofPalette.SurfaceMuted, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var fill = new Pen(ProofPalette.Accent, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        eventArgs.Graphics.DrawArc(track, bounds, -90, 360);
        if (Value > 0) eventArgs.Graphics.DrawArc(fill, bounds, -90, Value * 3.6f);
        TextRenderer.DrawText(
            eventArgs.Graphics,
            $"{Value}%",
            Font,
            ClientRectangle,
            ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

internal sealed class ProofStatCard : ProofCard
{
    private readonly Label _value;
    private readonly Label _detail;

    public ProofStatCard(string title)
    {
        Height = 118;
        var titleLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 24,
            Text = title,
            Font = new Font("Segoe UI Variable Text", 9F, FontStyle.Regular),
            ForeColor = ProofPalette.Muted,
        };
        _value = new Label
        {
            Dock = DockStyle.Top,
            Height = 44,
            Text = "0",
            Font = new Font("Segoe UI Variable Display", 22F, FontStyle.Bold),
            ForeColor = ProofPalette.Ink,
        };
        _detail = new Label
        {
            Dock = DockStyle.Fill,
            Text = "尚无数据",
            Font = new Font("Segoe UI Variable Text", 8.5F),
            ForeColor = ProofPalette.Muted,
        };
        Controls.Add(_detail);
        Controls.Add(_value);
        Controls.Add(titleLabel);
    }

    public void SetValue(string value, string detail, Color? color = null)
    {
        _value.Text = value;
        _value.ForeColor = color ?? ProofPalette.Ink;
        _detail.Text = detail;
    }
}
