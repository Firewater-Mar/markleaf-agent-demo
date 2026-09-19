using MarkLeaf.Native;
using MarkLeaf.Services.Styles;

namespace MarkLeaf.UI.Dialogs;

/// <summary>
/// Focused export dialog shared by the Agent and the editor toolbar. Advanced
/// pagination and image options remain in the File menu's full export dialog.
/// </summary>
internal sealed class AgentExportDialog : Form
{
    private static readonly Color Accent = Color.FromArgb(23, 107, 85);
    private static readonly Color AccentHover = Color.FromArgb(14, 87, 67);
    private static readonly Color AccentSoft = Color.FromArgb(228, 241, 236);
    private static readonly Color Danger = Color.FromArgb(179, 67, 62);

    private readonly TextBox _fileNameBox = new();
    private readonly TextBox _folderBox = new();
    private readonly Label _validation = new();
    private readonly Button _htmlButton = new();
    private readonly Button _pdfButton = new();
    private readonly Button _wordButton = new();
    private readonly Button _exportButton = new();
    private readonly Color _background;
    private readonly Color _surface;
    private readonly Color _border;
    private readonly Color _ink;
    private readonly Color _muted;
    private string _format;
    private bool _overwritePending;

    public AgentExportDialog(
        string documentName,
        string defaultFileName,
        string defaultFolder,
        string requestedFormat)
    {
        _format = NormalizeFormat(requestedFormat);
        var colors = ColorThemeService.GetActiveColors();
        _background = GetColor(colors, "bg-primary", Color.White);
        _surface = GetColor(colors, "bg-secondary", Color.FromArgb(247, 249, 248));
        _border = GetColor(colors, "bg-selected", Color.FromArgb(220, 227, 223));
        _ink = GetColor(colors, "text-primary", Color.FromArgb(22, 33, 29));
        _muted = GetColor(colors, "text-secondary", Color.FromArgb(93, 107, 101));

        Text = "导出文档";
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(700, 610);
        MinimumSize = new Size(660, 580);
        BackColor = _background;
        ForeColor = _ink;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = _background,
            Padding = new Padding(32, 28, 32, 24),
            Margin = Padding.Empty,
            ColumnCount = 1,
            RowCount = 8,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 14));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(BuildHeading(documentName), 0, 0);
        root.Controls.Add(BuildFormatSection(), 0, 2);
        root.Controls.Add(BuildDestinationSection(defaultFileName, defaultFolder), 0, 4);
        root.Controls.Add(BuildFeedbackPanel(), 0, 6);
        root.Controls.Add(BuildFooter(), 0, 7);
        Controls.Add(root);

        AcceptButton = _exportButton;
        _fileNameBox.TextChanged += (_, _) => ResetOverwriteAndRefresh();
        _folderBox.TextChanged += (_, _) => ResetOverwriteAndRefresh();
        UpdateFormatSelection(updateFileExtension: true);

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "App", "App.ico");
        if (File.Exists(iconPath))
        {
            try { Icon = new Icon(iconPath); } catch { }
        }

        Shown += (_, _) =>
        {
            if (ColorThemeService.IsActiveThemeDark())
            {
                DarkModeService.ApplyDialogDarkMode(this, _background, _ink);
                DarkModeService.SetWindowDarkTitleBar(this);
            }
            _fileNameBox.Select(0, Math.Max(0, Path.GetFileNameWithoutExtension(_fileNameBox.Text).Length));
            _fileNameBox.Focus();
        };
    }

    public string Format => _format;

    public string OutputPath => Path.Combine(_folderBox.Text.Trim(), _fileNameBox.Text.Trim());

    private Control BuildHeading(string documentName)
    {
        var heading = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            Margin = Padding.Empty,
        };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        heading.Controls.Add(new Label
        {
            Text = "导出当前文档",
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold),
            ForeColor = _ink,
            Margin = new Padding(0, 0, 0, 6),
        }, 0, 0);
        heading.Controls.Add(new Label
        {
            Text = $"{documentName}  ·  选择格式和保存位置",
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular),
            ForeColor = _muted,
            Margin = Padding.Empty,
        }, 0, 1);
        return heading;
    }

    private Control BuildFormatSection()
    {
        var section = NewSection();
        section.Controls.Add(NewSectionLabel("导出格式"), 0, 0);
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 92,
            ColumnCount = 3,
            Margin = new Padding(0, 10, 0, 0),
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.334F));
        ConfigureFormatButton(_htmlButton, "HTML", "适合网页和分享", "html");
        ConfigureFormatButton(_pdfButton, "PDF", "固定排版和打印", "pdf");
        ConfigureFormatButton(_wordButton, "Word", "便于继续编辑", "docx");
        _htmlButton.Margin = new Padding(0, 0, 7, 0);
        _pdfButton.Margin = new Padding(4, 0, 4, 0);
        _wordButton.Margin = new Padding(7, 0, 0, 0);
        row.Controls.Add(_htmlButton, 0, 0);
        row.Controls.Add(_pdfButton, 1, 0);
        row.Controls.Add(_wordButton, 2, 0);
        section.Controls.Add(row, 0, 1);
        return section;
    }

    private Control BuildDestinationSection(string defaultFileName, string defaultFolder)
    {
        var section = NewSection();
        section.Controls.Add(NewSectionLabel("文件名"), 0, 0);
        ConfigureTextBox(_fileNameBox, defaultFileName + ExtensionFor(_format));
        _fileNameBox.Margin = new Padding(0, 8, 0, 14);
        section.Controls.Add(_fileNameBox, 0, 1);
        section.Controls.Add(NewSectionLabel("保存到"), 0, 2);

        var folderRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Margin = new Padding(0, 8, 0, 0),
        };
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ConfigureTextBox(_folderBox, defaultFolder);
        var browse = CreateSecondaryButton("浏览…");
        browse.AutoSize = true;
        browse.MinimumSize = new Size(96, 42);
        browse.Margin = new Padding(10, 0, 0, 0);
        browse.Click += (_, _) => BrowseFolder();
        folderRow.Controls.Add(_folderBox, 0, 0);
        folderRow.Controls.Add(browse, 1, 0);
        section.Controls.Add(folderRow, 0, 3);
        return section;
    }

    private Control BuildFeedbackPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = _background,
            Padding = Padding.Empty,
            Margin = Padding.Empty,
        };
        _validation.AutoSize = true;
        _validation.Dock = DockStyle.Top;
        _validation.TextAlign = ContentAlignment.MiddleLeft;
        _validation.ForeColor = Danger;
        _validation.Margin = Padding.Empty;
        panel.Controls.Add(_validation);
        return panel;
    }

    private Control BuildFooter()
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            Margin = new Padding(0, 20, 0, 0),
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var advancedHint = new Label
        {
            Text = "高级排版：文件 > 导出",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = _muted,
            AutoEllipsis = true,
        };
        var cancel = CreateSecondaryButton("取消");
        cancel.AutoSize = true;
        cancel.MinimumSize = new Size(96, 42);
        cancel.Margin = new Padding(8, 0, 8, 0);
        cancel.DialogResult = DialogResult.Cancel;
        ConfigurePrimaryButton(_exportButton);
        _exportButton.AutoSize = true;
        _exportButton.MinimumSize = new Size(118, 42);
        _exportButton.Margin = new Padding(4, 0, 0, 0);
        _exportButton.Click += (_, _) => ConfirmExport();
        row.Controls.Add(advancedHint, 0, 0);
        row.Controls.Add(cancel, 1, 0);
        row.Controls.Add(_exportButton, 2, 0);
        CancelButton = cancel;
        return row;
    }

    private static TableLayoutPanel NewSection()
    {
        var section = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Margin = Padding.Empty,
        };
        section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return section;
    }

    private Label NewSectionLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
        ForeColor = _ink,
        Margin = Padding.Empty,
    };

    private void ConfigureFormatButton(Button button, string title, string description, string format)
    {
        button.Text = $"{title}\r\n{description}";
        button.Dock = DockStyle.Fill;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = _border;
        button.FlatAppearance.BorderSize = 1;
        button.BackColor = _background;
        button.ForeColor = _ink;
        button.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);
        button.Cursor = Cursors.Hand;
        button.AccessibleName = $"导出为 {title}";
        button.Click += (_, _) =>
        {
            _format = format;
            UpdateFormatSelection(updateFileExtension: true);
        };
    }

    private void UpdateFormatSelection(bool updateFileExtension)
    {
        StyleFormatButton(_htmlButton, _format == "html");
        StyleFormatButton(_pdfButton, _format == "pdf");
        StyleFormatButton(_wordButton, _format == "docx");
        if (updateFileExtension && !string.IsNullOrWhiteSpace(_fileNameBox.Text))
        {
            _fileNameBox.Text = Path.GetFileNameWithoutExtension(_fileNameBox.Text.Trim()) + ExtensionFor(_format);
        }
        ResetOverwriteAndRefresh();
    }

    private void StyleFormatButton(Button button, bool selected)
    {
        button.BackColor = selected ? AccentSoft : _background;
        button.ForeColor = selected ? Accent : _ink;
        button.FlatAppearance.BorderColor = selected ? Accent : _border;
        button.FlatAppearance.BorderSize = selected ? 2 : 1;
        button.Font = new Font("Microsoft YaHei UI", 9F, selected ? FontStyle.Bold : FontStyle.Regular);
    }

    private void ConfigureTextBox(TextBox box, string text)
    {
        box.Text = text;
        box.Dock = DockStyle.Top;
        box.AutoSize = true;
        box.BorderStyle = BorderStyle.FixedSingle;
        box.BackColor = _background;
        box.ForeColor = _ink;
        box.Font = new Font("Microsoft YaHei UI", 10F);
        box.Margin = Padding.Empty;
    }

    private Button CreateSecondaryButton(string text)
    {
        var button = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = _background,
            ForeColor = _ink,
            Cursor = Cursors.Hand,
            Font = new Font("Microsoft YaHei UI", 9F),
        };
        button.FlatAppearance.BorderColor = _border;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = _surface;
        return button;
    }

    private static void ConfigurePrimaryButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = Accent;
        button.ForeColor = Color.White;
        button.Cursor = Cursors.Hand;
        button.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        button.FlatAppearance.BorderColor = Accent;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = AccentHover;
        button.AccessibleName = "导出文档";
    }

    private void BrowseFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择导出文件的保存位置",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            InitialDirectory = Directory.Exists(_folderBox.Text.Trim())
                ? _folderBox.Text.Trim()
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) _folderBox.Text = dialog.SelectedPath;
    }

    private void ConfirmExport()
    {
        _validation.Text = string.Empty;
        var fileName = _fileNameBox.Text.Trim();
        var folder = _folderBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(fileName))
        {
            ShowValidation("请输入文件名。", _fileNameBox);
            return;
        }
        if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            ShowValidation("文件名包含 Windows 不支持的字符。", _fileNameBox);
            return;
        }
        if (!fileName.EndsWith(ExtensionFor(_format), StringComparison.OrdinalIgnoreCase))
        {
            _fileNameBox.Text = Path.GetFileNameWithoutExtension(fileName) + ExtensionFor(_format);
            fileName = _fileNameBox.Text;
        }
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            ShowValidation("请选择一个存在的保存文件夹。", _folderBox);
            return;
        }
        if (File.Exists(Path.Combine(folder, fileName)) && !_overwritePending)
        {
            _overwritePending = true;
            _validation.Text = "同名文件已存在。再次点击“确认覆盖”即可替换。";
            _exportButton.Text = "确认覆盖";
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private void ShowValidation(string message, Control focus)
    {
        _validation.Text = message;
        focus.Focus();
    }

    private void ResetOverwriteAndRefresh()
    {
        _overwritePending = false;
        _validation.Text = string.Empty;
        _exportButton.Text = $"导出 {DisplayFormat(_format)}";
    }

    private static string NormalizeFormat(string? format) =>
        string.Equals(format, "pdf", StringComparison.OrdinalIgnoreCase) ? "pdf"
        : string.Equals(format, "docx", StringComparison.OrdinalIgnoreCase)
            || string.Equals(format, "word", StringComparison.OrdinalIgnoreCase) ? "docx"
        : "html";

    private static string ExtensionFor(string format) => format switch
    {
        "pdf" => ".pdf",
        "docx" => ".docx",
        _ => ".html",
    };

    private static string DisplayFormat(string format) => format switch
    {
        "pdf" => "PDF",
        "docx" => "Word",
        _ => "HTML",
    };

    private static Color GetColor(
        IReadOnlyDictionary<string, Color> colors,
        string name,
        Color fallback) => colors.TryGetValue(name, out var color) ? color : fallback;
}
