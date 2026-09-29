using MarkLeaf.Services.Typography;

namespace MarkLeaf.UI.Dialogs;

internal sealed class TypographyWorkbenchDialog : Form
{
    private static readonly Color CanvasColor = Color.FromArgb(248, 246, 242);
    private static readonly Color SurfaceColor = Color.White;
    private static readonly Color MutedSurfaceColor = Color.FromArgb(242, 239, 234);
    private static readonly Color BorderColor = Color.FromArgb(218, 212, 204);
    private static readonly Color TextColor = Color.FromArgb(48, 43, 38);
    private static readonly Color MutedTextColor = Color.FromArgb(105, 98, 90);
    private static readonly Color AccentColor = Color.FromArgb(39, 125, 84);
    private static readonly Color WarningColor = Color.FromArgb(148, 84, 23);
    private const string DefaultAssessmentHint = "检查结果按严重程度排序。选择其中一项可查看完整说明。";

    private readonly string _original;
    private readonly TypographyTemplateStore _store;
    private readonly IReadOnlyList<DomBlockMeasurement> _measurements;
    private readonly ComboBox _templates = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        DisplayMember = nameof(TypographyTemplate.Name),
        Width = 210,
        Margin = new Padding(8, 3, 8, 3),
    };
    private readonly Label _score = new()
    {
        AutoSize = true,
        Font = new Font("Segoe UI", 11F, FontStyle.Bold),
        Padding = new Padding(12, 7, 12, 7),
        Margin = new Padding(12, 2, 0, 2),
        TextAlign = ContentAlignment.MiddleCenter,
    };
    private readonly ListView _defects = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        HideSelection = false,
        BorderStyle = BorderStyle.FixedSingle,
    };
    private readonly ListView _changes = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        CheckBoxes = true,
        FullRowSelect = true,
        HideSelection = false,
        BorderStyle = BorderStyle.FixedSingle,
    };
    private readonly TextBox _before = PreviewBox();
    private readonly TextBox _after = PreviewBox();
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly TabPage _assessmentPage = new("1  质量评估");
    private readonly TabPage _reviewPage = new("2  审阅改动  (0/0)");
    private readonly Label _selectionSummary = new()
    {
        AutoSize = true,
        Font = new Font("Segoe UI", 9F, FontStyle.Bold),
        ForeColor = TextColor,
        Margin = new Padding(0, 7, 12, 0),
    };
    private readonly Label _assessmentHint = new()
    {
        Text = DefaultAssessmentHint,
        AutoSize = true,
        ForeColor = MutedTextColor,
        Margin = new Padding(0, 0, 0, 9),
    };
    private readonly Button _applyButton;
    private readonly Button _replanButton;
    private FeedbackResult? _result;

    public string AcceptedMarkdown { get; private set; }
    public string SelectedTemplateId => CurrentTemplate.Id;
    private TypographyTemplate CurrentTemplate => (TypographyTemplate)_templates.SelectedItem!;

    public TypographyWorkbenchDialog(string markdown, TypographyTemplateStore store,
        IReadOnlyList<DomBlockMeasurement> measurements, string? selectedId,
        TypographyConstraints? initialConstraints = null)
    {
        _original = markdown;
        AcceptedMarkdown = markdown;
        _store = store;
        _measurements = measurements;
        Text = "MarkLeaf · A4 排版检查与优化";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = CanvasColor;
        ForeColor = TextColor;
        Font = new Font("Segoe UI", 9F);
        MinimumSize = new Size(780, 560);
        Size = new Size(1040, 720);

        _defects.Columns.Add("级别", 76);
        _defects.Columns.Add("类型", 150);
        _defects.Columns.Add("位置", 96);
        _defects.Columns.Add("说明", 500);
        _changes.Columns.Add("应用", 58);
        _changes.Columns.Add("位置", 86);
        _changes.Columns.Add("改动内容", 250);

        var manage = SecondaryButton("模板库…");
        var evaluate = SecondaryButton("重新评估");
        var optimize = PrimaryButton("生成优化方案");
        _replanButton = SecondaryButton("按未选项重新规划");
        _replanButton.Enabled = false;
        _applyButton = PrimaryButton("应用已选改动");
        _applyButton.Enabled = false;
        var cancel = SecondaryButton("取消");
        cancel.DialogResult = DialogResult.Cancel;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 14, 16, 14),
            ColumnCount = 1,
            RowCount = 4,
            BackColor = CanvasColor,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = BuildHeader();
        var toolbar = BuildToolbar(manage, evaluate, optimize, _replanButton);
        BuildAssessmentPage();
        BuildReviewPage();
        _tabs.TabPages.AddRange([_assessmentPage, _reviewPage]);
        _tabs.Margin = new Padding(0, 12, 0, 12);
        var footer = BuildFooter(cancel, _applyButton);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(toolbar, 0, 1);
        root.Controls.Add(_tabs, 0, 2);
        root.Controls.Add(footer, 0, 3);
        Controls.Add(root);
        CancelButton = cancel;
        AcceptButton = _applyButton;

        var items = _store.LoadAll();
        _templates.DataSource = items.ToList();
        _templates.SelectedItem = items.FirstOrDefault(x => x.Id == selectedId) ?? items.First();
        _templates.SelectedIndexChanged += (_, _) => Evaluate();
        manage.Click += (_, _) => ManageTemplates();
        evaluate.Click += (_, _) => Evaluate();
        optimize.Click += (_, _) => Optimize(null);
        _replanButton.Click += (_, _) => Replan();
        _changes.SelectedIndexChanged += (_, _) => ShowChange();
        _changes.ItemChecked += (_, _) => BeginInvoke(UpdateSelectionState);
        _defects.Resize += (_, _) => ResizeColumns();
        _changes.Resize += (_, _) => ResizeColumns();
        _applyButton.Click += (_, _) => ApplySelected();
        Shown += (_, _) =>
        {
            FitToWorkingArea();
            if (initialConstraints is not null) Optimize(initialConstraints);
        };
        Resize += (_, _) => ResizeColumns();
        Evaluate();
    }

    private Control BuildHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 12),
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var heading = new Label
        {
            Text = "A4 排版检查与优化",
            AutoSize = true,
            Font = new Font("Segoe UI", 16F, FontStyle.Bold),
            ForeColor = TextColor,
            Margin = new Padding(0),
        };
        header.Controls.Add(heading, 0, 0);
        header.Controls.Add(_score, 1, 0);
        return header;
    }

    private Control BuildToolbar(Button manage, Button evaluate, Button optimize, Button replan)
    {
        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = MutedSurfaceColor,
            Padding = new Padding(12, 9, 12, 9),
            ColumnCount = 4,
            RowCount = 2,
            Margin = new Padding(0),
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        toolbar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        toolbar.Controls.Add(new Label
        {
            Text = "规则模板",
            AutoSize = true,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 10, 0),
        }, 0, 0);
        _templates.Dock = DockStyle.Fill;
        _templates.Margin = new Padding(0, 0, 8, 5);
        manage.Anchor = AnchorStyles.Left;
        manage.Margin = new Padding(0, 0, 8, 5);
        toolbar.Controls.Add(_templates, 1, 0);
        toolbar.Controls.Add(manage, 2, 0);
        evaluate.Anchor = AnchorStyles.Left;
        optimize.Anchor = AnchorStyles.Left;
        replan.Anchor = AnchorStyles.Left;
        evaluate.Margin = new Padding(0, 5, 8, 0);
        optimize.Margin = new Padding(0, 5, 8, 0);
        replan.Margin = new Padding(0, 5, 8, 0);
        toolbar.Controls.Add(evaluate, 0, 1);
        toolbar.Controls.Add(optimize, 1, 1);
        toolbar.Controls.Add(replan, 2, 1);
        return toolbar;
    }

    private void BuildAssessmentPage()
    {
        _assessmentPage.BackColor = SurfaceColor;
        _assessmentPage.Padding = new Padding(10);
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.Controls.Add(_assessmentHint, 0, 0);
        content.Controls.Add(_defects, 0, 1);
        _assessmentPage.Controls.Add(content);
    }

    private void BuildReviewPage()
    {
        _reviewPage.BackColor = SurfaceColor;
        _reviewPage.Padding = new Padding(10);
        var page = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 45));

        var reviewBar = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            WrapContents = true,
            Margin = new Padding(0, 0, 0, 9),
        };
        var selectAll = LinkButton("全选");
        var selectNone = LinkButton("全不选");
        selectAll.Click += (_, _) => SetAllChangesChecked(true);
        selectNone.Click += (_, _) => SetAllChangesChecked(false);
        reviewBar.Controls.Add(_selectionSummary);
        reviewBar.Controls.Add(selectAll);
        reviewBar.Controls.Add(selectNone);

        var changesPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 10),
        };
        changesPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        changesPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        changesPanel.Controls.Add(_changes, 0, 0);

        var preview = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
        };
        preview.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        preview.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        preview.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        preview.Controls.Add(Wrap("修改前", _before), 0, 0);
        preview.Controls.Add(Wrap("修改后", _after), 1, 0);
        page.Controls.Add(reviewBar, 0, 0);
        page.Controls.Add(changesPanel, 0, 1);
        page.Controls.Add(preview, 0, 2);
        _reviewPage.Controls.Add(page);
    }

    private Control BuildFooter(Button cancel, Button apply)
    {
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0, 4, 0, 0),
            Margin = new Padding(0),
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.Controls.Add(new Label
        {
            Text = "不会直接修改文档；只有勾选的改动会被应用。",
            AutoSize = true,
            ForeColor = MutedTextColor,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 9, 12, 0),
        }, 0, 0);
        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
        };
        actions.Controls.Add(cancel);
        actions.Controls.Add(apply);
        footer.Controls.Add(actions, 1, 0);
        return footer;
    }

    private void Evaluate()
    {
        _assessmentHint.Text = DefaultAssessmentHint;
        _assessmentHint.ForeColor = MutedTextColor;
        ShowAssessment(new TypographyEvaluator().Evaluate(_original, CurrentTemplate, _measurements));
        _result = null;
        _changes.Items.Clear();
        _before.Clear();
        _after.Clear();
        _replanButton.Enabled = false;
        UpdateSelectionState();
        _tabs.SelectedTab = _assessmentPage;
    }

    private void Optimize(TypographyConstraints? constraints)
    {
        _result = new TypographyFeedbackEngine().Run(_original, CurrentTemplate, constraints, _measurements);
        var changes = _result.Iterations.SelectMany(x => x.Changes).ToArray();
        var hasAcceptedChanges = changes.Length > 0;
        ShowAssessment(_result.FinalAssessment, projected: hasAcceptedChanges);
        if (!hasAcceptedChanges && _result.RejectedAssessment is not null)
        {
            _assessmentHint.Text = $"候选方案会使评分从 {_result.InitialAssessment.Score} 降至 {_result.RejectedAssessment.Score}，已自动放弃；文档不会被修改。";
            _assessmentHint.ForeColor = WarningColor;
        }
        else if (!hasAcceptedChanges)
        {
            _assessmentHint.Text = "当前没有能安全提升评分的自动改动，文档不会被修改。";
            _assessmentHint.ForeColor = MutedTextColor;
        }
        else
        {
            _assessmentHint.Text = DefaultAssessmentHint;
            _assessmentHint.ForeColor = MutedTextColor;
        }
        _changes.BeginUpdate();
        _changes.Items.Clear();
        foreach (var change in changes)
        {
            var item = new ListViewItem(string.Empty) { Checked = true, Tag = new ChangeItem(change) };
            item.SubItems.Add($"第 {change.Line} 行");
            item.SubItems.Add(change.Summary);
            _changes.Items.Add(item);
        }
        _changes.EndUpdate();
        _replanButton.Enabled = _changes.Items.Count > 0;
        UpdateSelectionState();
        ResizeColumns();
        if (_changes.Items.Count > 0)
        {
            _changes.Items[0].Selected = true;
            _changes.Items[0].Focused = true;
            _tabs.SelectedTab = _reviewPage;
            ShowChange((ChangeItem)_changes.Items[0].Tag!);
            _changes.Focus();
        }
        else
        {
            _before.Clear();
            _after.Clear();
            _tabs.SelectedTab = _assessmentPage;
        }
    }

    private void Replan()
    {
        if (_result is null) return;
        var forbidden = _changes.Items.Cast<ListViewItem>()
            .Where(x => !x.Checked)
            .Select(x => ((ChangeItem)x.Tag!).Change.Type)
            .ToHashSet();
        Optimize(new(ForbiddenTaskTypes: forbidden));
    }

    private void ShowAssessment(TypographyAssessment value, bool projected = false)
    {
        _score.Text = projected
            ? $"应用后预计 {value.Score}/100 · {value.Defects.Count} 个问题"
            : $"得分 {value.Score}/100 · {value.Defects.Count} 个问题";
        var passed = value.Score >= CurrentTemplate.Rules.TargetScore;
        _score.ForeColor = passed ? Color.FromArgb(25, 103, 67) : WarningColor;
        _score.BackColor = passed ? Color.FromArgb(225, 242, 233) : Color.FromArgb(251, 236, 217);
        _defects.BeginUpdate();
        _defects.Items.Clear();
        foreach (var defect in value.Defects)
        {
            var item = new ListViewItem(defect.Severity switch { DefectSeverity.Error => "严重", DefectSeverity.Warning => "警告", _ => "建议" });
            item.SubItems.Add(DefectName(defect.Type));
            item.SubItems.Add($"第 {defect.Location.Line} 行");
            item.SubItems.Add(defect.Message);
            _defects.Items.Add(item);
        }
        _defects.EndUpdate();
        ResizeColumns();
    }

    private void ShowChange()
    {
        if (_changes.SelectedItems.Count == 0 || _changes.SelectedItems[0].Tag is not ChangeItem item) return;
        ShowChange(item);
    }

    private void ShowChange(ChangeItem item)
    {
        _before.Text = VisibleWhitespace(item.Change.Before, item.Change.BeforeLineCount);
        _after.Text = VisibleWhitespace(item.Change.After, item.Change.AfterLineCount);
    }

    private static string VisibleWhitespace(string value, int lineCount = 0)
    {
        if (lineCount == 0 && value.Length == 0) return "⟦无内容⟧";
        var lines = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        if (lineCount > lines.Length)
            lines = lines.Concat(Enumerable.Repeat(string.Empty, lineCount - lines.Length)).ToArray();
        if (lineCount > 0 && lines.Length > lineCount) lines = lines.Take(lineCount).ToArray();
        return string.Join(Environment.NewLine, lines.Select(line => line.Length == 0 ? "⟦空行⟧" : line));
    }

    private void ApplySelected()
    {
        if (_result is null) { MessageBox.Show(this, "请先生成优化方案。", Text); return; }
        var selected = _changes.CheckedItems.Cast<ListViewItem>()
            .Select(x => ((ChangeItem)x.Tag!).Change)
            .ToArray();
        AcceptedMarkdown = selected.Length == _changes.Items.Count ? _result.ProposedMarkdown : TypographyExecutor.ApplyAcceptedChanges(_original, selected);
        DialogResult = DialogResult.OK;
        Close();
    }

    private void SetAllChangesChecked(bool isChecked)
    {
        _changes.BeginUpdate();
        foreach (ListViewItem item in _changes.Items) item.Checked = isChecked;
        _changes.EndUpdate();
        UpdateSelectionState();
    }

    private void UpdateSelectionState()
    {
        var selected = _changes.CheckedItems.Count;
        var total = _changes.Items.Count;
        _selectionSummary.Text = total == 0 ? "尚未生成改动" : $"已选择 {selected} / {total} 项改动";
        _reviewPage.Text = $"2  审阅改动  ({selected}/{total})";
        _applyButton.Enabled = _result is not null && selected > 0;
    }
    private void ManageTemplates()
    {
        using var dialog = new TypographyTemplateLibraryDialog(_store, CurrentTemplate.Id);
        var result = dialog.ShowDialog(this); var id = result == DialogResult.OK ? dialog.SelectedTemplate?.Id : CurrentTemplate.Id;
        var items = _store.LoadAll(); _templates.DataSource = items.ToList(); _templates.SelectedItem = items.FirstOrDefault(x => x.Id == id) ?? items.First();
    }
    private static Button PrimaryButton(string text)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(96, 34),
            Padding = new Padding(12, 4, 12, 4),
            Margin = new Padding(0, 0, 8, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = AccentColor,
            ForeColor = Color.White,
            UseVisualStyleBackColor = false,
            Cursor = Cursors.Hand,
        };
        button.FlatAppearance.BorderSize = 0;
        button.EnabledChanged += (_, _) =>
        {
            button.BackColor = button.Enabled ? AccentColor : Color.FromArgb(184, 197, 190);
            button.ForeColor = Color.White;
        };
        return button;
    }

    private static Button SecondaryButton(string text)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(84, 34),
            Padding = new Padding(10, 4, 10, 4),
            Margin = new Padding(0, 0, 8, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = TextColor,
            UseVisualStyleBackColor = false,
            Cursor = Cursors.Hand,
        };
        button.FlatAppearance.BorderColor = BorderColor;
        return button;
    }

    private static Button LinkButton(string text)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8, 2, 8, 2),
            Margin = new Padding(0, 0, 6, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = AccentColor,
            UseVisualStyleBackColor = false,
            Cursor = Cursors.Hand,
        };
        button.FlatAppearance.BorderColor = BorderColor;
        return button;
    }

    private static TextBox PreviewBox() => new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        WordWrap = true,
        Font = new Font("Consolas", 9F),
        BackColor = Color.White,
        BorderStyle = BorderStyle.FixedSingle,
    };

    private static Control Wrap(string title, Control body)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(6, 0, 6, 6),
            ColumnCount = 1,
            RowCount = 2,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label
        {
            Text = title,
            AutoSize = true,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = TextColor,
            Margin = new Padding(0, 0, 0, 7),
        }, 0, 0);
        layout.Controls.Add(body, 0, 1);
        return layout;
    }

    private sealed record ChangeItem(DocumentChange Change);

    private void FitToWorkingArea()
    {
        var area = Screen.FromControl(this).WorkingArea;
        const int edgeClearance = 24;
        var availableWidth = Math.Max(640, area.Width - edgeClearance * 2);
        var availableHeight = Math.Max(460, area.Height - edgeClearance * 2);
        MinimumSize = new Size(Math.Min(MinimumSize.Width, availableWidth), Math.Min(MinimumSize.Height, availableHeight));
        var width = Math.Min(Width, availableWidth);
        var height = Math.Min(Height, availableHeight);
        SetBounds(
            area.Left + Math.Max(edgeClearance, (area.Width - width) / 2),
            area.Top + Math.Max(edgeClearance, (area.Height - height) / 2),
            width,
            height);
    }

    private void ResizeColumns()
    {
        if (_defects.Columns.Count == 4 && _defects.ClientSize.Width > 0)
        {
            _defects.Columns[0].Width = 76;
            _defects.Columns[1].Width = 150;
            _defects.Columns[2].Width = 96;
            _defects.Columns[3].Width = Math.Max(220,
                _defects.ClientSize.Width - _defects.Columns[0].Width - _defects.Columns[1].Width - _defects.Columns[2].Width - 8);
        }
        if (_changes.Columns.Count == 3 && _changes.ClientSize.Width > 0)
        {
            _changes.Columns[0].Width = 58;
            _changes.Columns[1].Width = 86;
            _changes.Columns[2].Width = Math.Max(150, _changes.ClientSize.Width - 152);
        }
    }

    private static string DefectName(TypographyDefectType type) => type switch
    {
        TypographyDefectType.OrphanLine => "孤行",
        TypographyDefectType.WidowLine => "寡行",
        TypographyDefectType.HeadingLevelJump => "标题跳级",
        TypographyDefectType.ExcessBlankLines => "多余空行",
        TypographyDefectType.ImageOverflow => "图片溢出",
        TypographyDefectType.TableOverflow => "表格过宽",
        TypographyDefectType.LongLine => "段落过长",
        TypographyDefectType.FragmentedParagraph => "短段碎片化",
        TypographyDefectType.MissingTableOfContents => "缺少目录",
        _ => type.ToString(),
    };
}

internal sealed class TypographyTemplateLibraryDialog : Form
{
    private static readonly Color Canvas = Color.FromArgb(248, 246, 242);
    private static readonly Color Border = Color.FromArgb(218, 212, 204);
    private static readonly Color TextPrimary = Color.FromArgb(48, 43, 38);
    private static readonly Color TextMuted = Color.FromArgb(105, 98, 90);
    private static readonly Color Accent = Color.FromArgb(39, 125, 84);

    private readonly TypographyTemplateStore _store;
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, DisplayMember = nameof(TypographyTemplate.Name), BorderStyle = BorderStyle.FixedSingle };
    private readonly TabControl _editorTabs = new() { Dock = DockStyle.Fill };
    private readonly TextBox _name = new() { Dock = DockStyle.Fill };
    private readonly TextBox _description = new() { Dock = DockStyle.Fill, Multiline = true, Height = 70, ScrollBars = ScrollBars.Vertical };
    private readonly ComboBox _bodyFont = FontPicker();
    private readonly ComboBox _headingFont = FontPicker();
    private readonly NumericUpDown _bodyFontSize = DecimalNumber(8M, 18M, 0.5M, 1);
    private readonly NumericUpDown _heading1FontSize = DecimalNumber(12M, 32M, 0.5M, 1);
    private readonly NumericUpDown _heading2FontSize = DecimalNumber(10M, 28M, 0.5M, 1);
    private readonly NumericUpDown _heading3FontSize = DecimalNumber(9M, 24M, 0.5M, 1);
    private readonly NumericUpDown _lineSpacing = DecimalNumber(1M, 2.5M, 0.05M, 2);
    private readonly NumericUpDown _paragraphSpacing = DecimalNumber(0M, 24M, 0.5M, 1);
    private readonly NumericUpDown _maxLineChars = Number(20, 160);
    private readonly NumericUpDown _imageWidth = Number(10, 100);
    private readonly NumericUpDown _tableWidth = Number(40, 100);
    private readonly NumericUpDown _blankLimit = Number(0, 5);
    private readonly NumericUpDown _shortParagraph = Number(1, 100);
    private readonly NumericUpDown _tocHeadings = Number(2, 30);
    private readonly NumericUpDown _targetScore = Number(50, 100);
    private readonly NumericUpDown _maxIterations = Number(1, 5);
    private readonly CheckBox _allowOrphan = new() { Text = "允许孤行", AutoSize = true };
    private readonly CheckBox _allowWidow = new() { Text = "允许寡行", AutoSize = true };
    private readonly CheckBox _generateToc = new() { Text = "自动生成目录", AutoSize = true };
    private readonly CheckBox _keepHeading = new() { Text = "标题与下段同页", AutoSize = true };
    private readonly Label _editorStatus = new() { AutoSize = true, ForeColor = TextMuted };
    private readonly ComboBox _draftMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    private readonly TextBox _draftName = new() { Dock = DockStyle.Fill };
    private readonly TextBox _draftSource = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Both, AcceptsTab = true };
    private readonly Label _draftHelp = new() { AutoSize = true, ForeColor = TextMuted };
    private readonly Label _draftStatus = new() { AutoSize = true, ForeColor = Accent };
    private readonly TemplateStandardPreview _preview = new() { Dock = DockStyle.Fill, MinimumSize = new Size(210, 250) };
    private readonly Label _previewDescription = new() { AutoSize = true, ForeColor = TextMuted };
    private readonly Label _previewRules = new() { AutoSize = true, ForeColor = TextMuted };
    private readonly TextBox _json = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ScrollBars = ScrollBars.Both,
        AcceptsTab = true,
        WordWrap = false,
        Font = new Font("Consolas", 10F),
    };
    private bool _loading;

    public TypographyTemplate? SelectedTemplate => _list.SelectedItem as TypographyTemplate;

    public TypographyTemplateLibraryDialog(TypographyTemplateStore store, string selectedId)
    {
        _store = store;
        Text = "排版规则模板库";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        Size = new Size(1160, 760);
        MinimumSize = new Size(900, 600);
        BackColor = Canvas;
        ForeColor = TextPrimary;
        Font = new Font("Segoe UI", 9F);

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 230, FixedPanel = FixedPanel.Panel1 };
        split.Panel1.Padding = new Padding(0, 0, 10, 0);
        split.Panel1.Controls.Add(BuildTemplateList());
        _editorTabs.TabPages.Add(BuildVisualEditor());
        _editorTabs.TabPages.Add(BuildDraftCreator());
        _editorTabs.TabPages.Add(BuildJsonEditor());
        var editorPreview = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 8,
            BackColor = Canvas,
        };
        editorPreview.Panel1.Padding = new Padding(0, 0, 8, 0);
        editorPreview.Panel2.Padding = new Padding(8, 0, 0, 0);
        editorPreview.Panel1.Controls.Add(_editorTabs);
        editorPreview.Panel2.Controls.Add(BuildPreviewHost());
        split.Panel2.Controls.Add(editorPreview);

        var save = SecondaryButton("保存当前模板");
        var import = SecondaryButton("导入 JSON…");
        var export = SecondaryButton("导出 JSON…");
        var use = PrimaryButton("使用所选");
        var close = SecondaryButton("关闭");
        use.DialogResult = DialogResult.OK;
        close.DialogResult = DialogResult.Cancel;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 14, 16, 14),
            ColumnCount = 1,
            RowCount = 3,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(new Label
        {
            Text = "模板决定评估标准和优化策略。可视化编辑即可完成，无需手写 JSON。",
            AutoSize = true,
            ForeColor = TextMuted,
            Margin = new Padding(0, 0, 0, 12),
        }, 0, 0);
        root.Controls.Add(split, 0, 1);
        root.Controls.Add(BuildFooter(use, close, save, export, import), 0, 2);
        Controls.Add(root);
        CancelButton = close;
        AcceptButton = use;

        _list.SelectedIndexChanged += (_, _) => LoadSelected();
        _draftMode.Items.AddRange(["用自然语言描述", "粘贴 Markdown 样例"]);
        _draftMode.SelectedIndexChanged += (_, _) => UpdateDraftHelp();
        _draftMode.SelectedIndex = 0;
        WirePreviewUpdates();
        editorPreview.SizeChanged += (_, _) => ApplyResponsivePreviewLayout(editorPreview);
        Shown += (_, _) => ApplyResponsivePreviewLayout(editorPreview);
        save.Click += (_, _) => { if (_editorTabs.SelectedIndex == 2) SaveJson(); else SaveVisual(); };
        import.Click += (_, _) => Import();
        export.Click += (_, _) => Export();
        Reload(selectedId);
    }

    private Control BuildPreviewHost()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(242, 239, 234),
            Padding = new Padding(14),
            ColumnCount = 1,
            RowCount = 4,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label
        {
            Text = "标准样张",
            AutoSize = true,
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            ForeColor = TextPrimary,
            Margin = new Padding(0, 0, 0, 4),
        }, 0, 0);
        _previewDescription.MaximumSize = new Size(320, 0);
        _previewDescription.Margin = new Padding(0, 0, 0, 10);
        layout.Controls.Add(_previewDescription, 0, 1);
        _preview.Margin = Padding.Empty;
        layout.Controls.Add(_preview, 0, 2);
        _previewRules.MaximumSize = new Size(320, 0);
        _previewRules.Margin = new Padding(0, 10, 0, 0);
        layout.Controls.Add(_previewRules, 0, 3);
        layout.SizeChanged += (_, _) =>
        {
            var textWidth = Math.Max(180, layout.ClientSize.Width - layout.Padding.Horizontal);
            _previewDescription.MaximumSize = new Size(textWidth, 0);
            _previewRules.MaximumSize = new Size(textWidth, 0);
        };
        return layout;
    }

    private static void ApplyResponsivePreviewLayout(SplitContainer split)
    {
        if (split.ClientSize.Width <= 0 || split.ClientSize.Height <= 0) return;
        var stacked = split.ClientSize.Width < 700;
        var orientation = stacked ? Orientation.Horizontal : Orientation.Vertical;
        if (split.Orientation != orientation)
        {
            split.Panel1MinSize = 0;
            split.Panel2MinSize = 0;
            split.Orientation = orientation;
            split.Panel1.Padding = stacked ? new Padding(0, 0, 0, 6) : new Padding(0, 0, 8, 0);
            split.Panel2.Padding = stacked ? new Padding(0, 6, 0, 0) : new Padding(8, 0, 0, 0);
        }

        var available = (stacked ? split.ClientSize.Height : split.ClientSize.Width) - split.SplitterWidth;
        if (available < 360) return;
        var firstMinimum = stacked ? 250 : 360;
        var secondMinimum = stacked ? 170 : 240;
        var minimumTotal = firstMinimum + secondMinimum;
        if (available < minimumTotal)
        {
            firstMinimum = Math.Max(180, (int)(available * 0.58));
            secondMinimum = Math.Max(120, available - firstMinimum);
        }
        split.Panel1MinSize = firstMinimum;
        split.Panel2MinSize = secondMinimum;
        var desired = (int)(available * (stacked ? 0.58 : 0.60));
        split.SplitterDistance = Math.Clamp(desired, firstMinimum, available - secondMinimum);
    }

    private void WirePreviewUpdates()
    {
        _name.TextChanged += (_, _) => RefreshPreviewFromEditor();
        _description.TextChanged += (_, _) => RefreshPreviewFromEditor();
        foreach (var number in new[]
                 {
                     _bodyFontSize, _heading1FontSize, _heading2FontSize, _heading3FontSize,
                     _lineSpacing, _paragraphSpacing,
                     _maxLineChars, _imageWidth, _tableWidth, _blankLimit,
                     _shortParagraph, _tocHeadings, _targetScore, _maxIterations,
                 })
        {
            number.ValueChanged += (_, _) => RefreshPreviewFromEditor();
        }
        _bodyFont.SelectedIndexChanged += (_, _) => RefreshPreviewFromEditor();
        _headingFont.SelectedIndexChanged += (_, _) => RefreshPreviewFromEditor();
        foreach (var check in new[] { _allowOrphan, _allowWidow, _generateToc, _keepHeading })
            check.CheckedChanged += (_, _) => RefreshPreviewFromEditor();
    }

    private void RefreshPreviewFromEditor()
    {
        if (_loading || SelectedTemplate is not { } selected) return;
        var preview = new TypographyTemplate
        {
            Id = selected.Id,
            Name = string.IsNullOrWhiteSpace(_name.Text) ? selected.Name : _name.Text.Trim(),
            Description = _description.Text.Trim(),
            BuiltIn = selected.BuiltIn,
            Rules = new TypographyRules
            {
                BodyFontFamily = _bodyFont.Text,
                HeadingFontFamily = _headingFont.Text,
                BodyFontSizePt = _bodyFontSize.Value,
                Heading1FontSizePt = _heading1FontSize.Value,
                Heading2FontSizePt = _heading2FontSize.Value,
                Heading3FontSizePt = _heading3FontSize.Value,
                LineSpacing = _lineSpacing.Value,
                ParagraphSpacingPt = _paragraphSpacing.Value,
                MaxLineChars = (int)_maxLineChars.Value,
                ImageMaxWidth = (int)_imageWidth.Value,
                TableMaxWidth = (int)_tableWidth.Value,
                ConsecutiveBlankLineLimit = (int)_blankLimit.Value,
                ShortParagraphChars = (int)_shortParagraph.Value,
                MinimumTocHeadings = (int)_tocHeadings.Value,
                TargetScore = (int)_targetScore.Value,
                MaxIterations = (int)_maxIterations.Value,
                AllowOrphan = _allowOrphan.Checked,
                AllowWidow = _allowWidow.Checked,
                GenerateTableOfContents = _generateToc.Checked,
                KeepHeadingsWithNext = _keepHeading.Checked,
            },
        };
        UpdatePreview(preview);
    }

    private void UpdatePreview(TypographyTemplate item)
    {
        _preview.ShowTemplate(item);
        _previewDescription.Text = string.IsNullOrWhiteSpace(item.Description)
            ? $"“{item.Name}”的 A4 规则示意；修改左侧参数会实时刷新。"
            : $"{item.Description} 修改左侧参数会实时刷新。";
        _previewRules.Text =
            $"正文 {item.Rules.BodyFontFamily} {FormatPoint(item.Rules.BodyFontSizePt)} pt · 行距 {item.Rules.LineSpacing:0.##} 倍 · 段后 {FormatPoint(item.Rules.ParagraphSpacingPt)} pt\n" +
            $"一级标题 {FormatPoint(item.Rules.Heading1FontSizePt)} pt · 二级 {FormatPoint(item.Rules.Heading2FontSizePt)} pt · 三级 {FormatPoint(item.Rules.Heading3FontSizePt)} pt\n" +
            $"行长 {item.Rules.MaxLineChars} 字 · 图片 {item.Rules.ImageMaxWidth}% · 表格 {item.Rules.TableMaxWidth}% · " +
            $"目录：{(item.Rules.GenerateTableOfContents ? "自动" : "按需")}";
    }

    private Control BuildTemplateList()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label
        {
            Text = "模板",
            AutoSize = true,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 8),
        }, 0, 0);
        layout.Controls.Add(_list, 0, 1);
        var duplicate = SecondaryButton("基于所选创建副本");
        duplicate.Dock = DockStyle.Fill;
        duplicate.Margin = new Padding(0, 10, 0, 0);
        duplicate.Click += (_, _) =>
        {
            var item = _store.Save((SelectedTemplate ?? TypographyTemplateStore.BuiltIns()[0]).CloneAsUserTemplate());
            Reload(item.Id);
        };
        layout.Controls.Add(duplicate, 0, 2);
        return layout;
    }

    private TabPage BuildVisualEditor()
    {
        var page = new TabPage("可视化编辑") { BackColor = Color.White, Padding = new Padding(6) };
        var scrollHost = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.White,
            Padding = new Padding(10),
        };
        var form = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 20,
        };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(form, 0, "模板名称", _name);
        AddRow(form, 1, "用途描述", _description);
        AddRow(form, 2, "正文字体", _bodyFont);
        AddRow(form, 3, "标题字体", _headingFont);
        AddRow(form, 4, "正文字号", WithSuffix(_bodyFontSize, "pt"));
        AddRow(form, 5, "一级标题字号", WithSuffix(_heading1FontSize, "pt"));
        AddRow(form, 6, "二级标题字号", WithSuffix(_heading2FontSize, "pt"));
        AddRow(form, 7, "三级标题字号", WithSuffix(_heading3FontSize, "pt"));
        AddRow(form, 8, "正文行距", WithSuffix(_lineSpacing, "倍"));
        AddRow(form, 9, "段后间距", WithSuffix(_paragraphSpacing, "pt"));
        AddRow(form, 10, "建议最大行长", WithSuffix(_maxLineChars, "字符"));
        AddRow(form, 11, "图片最大宽度", WithSuffix(_imageWidth, "%"));
        AddRow(form, 12, "表格最大宽度", WithSuffix(_tableWidth, "%"));
        AddRow(form, 13, "连续空行上限", WithSuffix(_blankLimit, "行"));
        AddRow(form, 14, "短段落判定", WithSuffix(_shortParagraph, "字符"));
        AddRow(form, 15, "目录标题数阈值", WithSuffix(_tocHeadings, "个"));
        AddRow(form, 16, "目标得分", WithSuffix(_targetScore, "分"));
        AddRow(form, 17, "最大迭代轮次", WithSuffix(_maxIterations, "轮"));
        var checks = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 6, 0, 6) };
        checks.Controls.AddRange([_generateToc, _keepHeading, _allowOrphan, _allowWidow]);
        form.Controls.Add(new Label { Text = "排版策略", AutoSize = true, Margin = new Padding(0, 10, 18, 0) }, 0, 18);
        form.Controls.Add(checks, 1, 18);
        _editorStatus.Margin = new Padding(0, 8, 0, 14);
        form.Controls.Add(_editorStatus, 0, 19);
        form.SetColumnSpan(_editorStatus, 2);
        scrollHost.Controls.Add(form);
        scrollHost.SizeChanged += (_, _) =>
        {
            _editorStatus.MaximumSize = new Size(Math.Max(240, scrollHost.ClientSize.Width - 30), 0);
            scrollHost.AutoScrollMinSize = new Size(0, form.PreferredSize.Height + scrollHost.Padding.Vertical + 8);
        };
        page.Controls.Add(scrollHost);
        return page;
    }

    private TabPage BuildDraftCreator()
    {
        var page = new TabPage("从描述 / 样例创建") { BackColor = Color.White, Padding = new Padding(14) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        AddRow(layout, 0, "模板名称", _draftName);
        AddRow(layout, 1, "创建方式", _draftMode);
        layout.Controls.Add(_draftHelp, 0, 2);
        layout.SetColumnSpan(_draftHelp, 2);
        layout.Controls.Add(_draftSource, 0, 3);
        layout.SetColumnSpan(_draftSource, 2);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 10, 0, 8) };
        var loadSample = SecondaryButton("从文件载入样例…");
        var create = PrimaryButton("生成规则草稿");
        loadSample.Click += (_, _) => LoadSampleFile();
        create.Click += (_, _) => CreateDraft();
        actions.Controls.Add(loadSample);
        actions.Controls.Add(create);
        layout.Controls.Add(actions, 0, 4);
        layout.SetColumnSpan(actions, 2);
        layout.Controls.Add(_draftStatus, 0, 5);
        layout.SetColumnSpan(_draftStatus, 2);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildJsonEditor()
    {
        var page = new TabPage("高级 JSON") { BackColor = Color.White, Padding = new Padding(10) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label
        {
            Text = "仅供熟悉 MarkLeaf 模板格式的用户使用；普通编辑请使用“可视化编辑”。",
            AutoSize = true,
            ForeColor = TextMuted,
            Margin = new Padding(0, 0, 0, 8),
        }, 0, 0);
        layout.Controls.Add(_json, 0, 1);
        page.Controls.Add(layout);
        return page;
    }

    private Control BuildFooter(params Button[] buttons)
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = true,
            Margin = new Padding(0, 12, 0, 0),
        };
        panel.Controls.AddRange(buttons);
        return panel;
    }

    private void LoadSelected()
    {
        if (SelectedTemplate is not { } item) return;
        _loading = true;
        _name.Text = item.Name;
        _description.Text = item.Description;
        SelectFont(_bodyFont, item.Rules.BodyFontFamily);
        SelectFont(_headingFont, item.Rules.HeadingFontFamily);
        _bodyFontSize.Value = item.Rules.BodyFontSizePt;
        _heading1FontSize.Value = item.Rules.Heading1FontSizePt;
        _heading2FontSize.Value = item.Rules.Heading2FontSizePt;
        _heading3FontSize.Value = item.Rules.Heading3FontSizePt;
        _lineSpacing.Value = item.Rules.LineSpacing;
        _paragraphSpacing.Value = item.Rules.ParagraphSpacingPt;
        _maxLineChars.Value = item.Rules.MaxLineChars;
        _imageWidth.Value = item.Rules.ImageMaxWidth;
        _tableWidth.Value = item.Rules.TableMaxWidth;
        _blankLimit.Value = item.Rules.ConsecutiveBlankLineLimit;
        _shortParagraph.Value = item.Rules.ShortParagraphChars;
        _tocHeadings.Value = item.Rules.MinimumTocHeadings;
        _targetScore.Value = item.Rules.TargetScore;
        _maxIterations.Value = item.Rules.MaxIterations;
        _allowOrphan.Checked = item.Rules.AllowOrphan;
        _allowWidow.Checked = item.Rules.AllowWidow;
        _generateToc.Checked = item.Rules.GenerateTableOfContents;
        _keepHeading.Checked = item.Rules.KeepHeadingsWithNext;
        _json.Text = TypographyTemplateStore.Serialize(item);
        _editorStatus.Text = item.BuiltIn ? "这是内置模板；保存时会自动创建可编辑副本。" : "用户模板，可直接修改并保存。";
        if (string.IsNullOrWhiteSpace(_draftName.Text)) _draftName.Text = $"{item.Name} - 新模板";
        _loading = false;
        UpdatePreview(item);
    }

    private void SaveVisual()
    {
        if (_loading || SelectedTemplate is not { } selected) return;
        try
        {
            var requestedName = string.IsNullOrWhiteSpace(_name.Text) ? "自定义模板" : _name.Text.Trim();
            var item = selected.BuiltIn ? selected.CloneAsUserTemplate($"{requestedName} - 自定义") : selected;
            if (!selected.BuiltIn) item.Name = requestedName;
            item.Description = _description.Text.Trim();
            item.BuiltIn = false;
            item.Rules.BodyFontFamily = _bodyFont.Text;
            item.Rules.HeadingFontFamily = _headingFont.Text;
            item.Rules.BodyFontSizePt = _bodyFontSize.Value;
            item.Rules.Heading1FontSizePt = _heading1FontSize.Value;
            item.Rules.Heading2FontSizePt = _heading2FontSize.Value;
            item.Rules.Heading3FontSizePt = _heading3FontSize.Value;
            item.Rules.LineSpacing = _lineSpacing.Value;
            item.Rules.ParagraphSpacingPt = _paragraphSpacing.Value;
            item.Rules.MaxLineChars = (int)_maxLineChars.Value;
            item.Rules.ImageMaxWidth = (int)_imageWidth.Value;
            item.Rules.TableMaxWidth = (int)_tableWidth.Value;
            item.Rules.ConsecutiveBlankLineLimit = (int)_blankLimit.Value;
            item.Rules.ShortParagraphChars = (int)_shortParagraph.Value;
            item.Rules.MinimumTocHeadings = (int)_tocHeadings.Value;
            item.Rules.TargetScore = (int)_targetScore.Value;
            item.Rules.MaxIterations = (int)_maxIterations.Value;
            item.Rules.AllowOrphan = _allowOrphan.Checked;
            item.Rules.AllowWidow = _allowWidow.Checked;
            item.Rules.GenerateTableOfContents = _generateToc.Checked;
            item.Rules.KeepHeadingsWithNext = _keepHeading.Checked;
            item.Rules.Normalize();
            item = _store.Save(item);
            Reload(item.Id);
            _editorStatus.Text = "已保存。";
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            MessageBox.Show(this, ex.Message, "模板保存失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void CreateDraft()
    {
        try
        {
            var sourceType = _draftMode.SelectedIndex == 1
                ? TypographyTemplateDraftSource.MarkdownSample
                : TypographyTemplateDraftSource.Description;
            _draftStatus.ForeColor = TextMuted;
            _draftStatus.Text = "正在本地分析，不会连接网络。";
            var item = TypographyTemplateDraftBuilder.Create(_draftName.Text, _draftSource.Text, sourceType);
            item = _store.Save(item);
            Reload(item.Id);
            _editorTabs.SelectedIndex = 0;
            _draftStatus.ForeColor = Accent;
            _draftStatus.Text = string.Empty;
            _editorStatus.Text = $"已生成“{item.Name}”，可继续调整后保存。";
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            _draftStatus.ForeColor = Color.Firebrick;
            _draftStatus.Text = $"生成失败：{ex.Message}";
        }
    }

    private void UpdateDraftHelp()
    {
        var sample = _draftMode.SelectedIndex == 1;
        _draftHelp.Text = sample
            ? "粘贴一段排版良好的 Markdown，MarkLeaf 会在本地统计行长、空行和目录规则。"
            : "本地解析适合参数明确的描述，例如：“每行约 42 字，图片不超过 88%，需要自动目录。”更自由的描述可在主界面的 AI 模式中生成。";
        _draftSource.Font = sample ? new Font("Consolas", 9F) : new Font("Segoe UI", 10F);
    }

    private void LoadSampleFile()
    {
        using var dialog = new OpenFileDialog { Filter = "Markdown 样例 (*.md;*.markdown;*.txt)|*.md;*.markdown;*.txt|所有文件 (*.*)|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _draftMode.SelectedIndex = 1;
            _draftSource.Text = File.ReadAllText(dialog.FileName);
            if (string.IsNullOrWhiteSpace(_draftName.Text)) _draftName.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
        }
        catch (IOException ex)
        {
            MessageBox.Show(this, ex.Message, "无法读取样例", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void Reload(string? id)
    {
        var items = _store.LoadAll();
        _list.DataSource = items.ToList();
        _list.SelectedItem = items.FirstOrDefault(x => x.Id == id) ?? items.First();
    }

    private void SaveJson()
    {
        try
        {
            var item = TypographyTemplateStore.Deserialize(_json.Text);
            if (SelectedTemplate?.BuiltIn == true) item = item.CloneAsUserTemplate($"{item.Name} - 自定义");
            item.BuiltIn = false;
            item = _store.Save(item);
            Reload(item.Id);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            MessageBox.Show(this, ex.Message, "模板保存失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void Import()
    {
        using var dialog = new OpenFileDialog { Filter = "MarkLeaf 排版模板 (*.json)|*.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { var item = _store.Import(dialog.FileName); Reload(item.Id); }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            MessageBox.Show(this, ex.Message, "模板导入失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void Export()
    {
        if (SelectedTemplate is not { } item) return;
        using var dialog = new SaveFileDialog { Filter = "MarkLeaf 排版模板 (*.json)|*.json", FileName = $"{item.Id}.json" };
        if (dialog.ShowDialog(this) == DialogResult.OK) _store.Export(item, dialog.FileName);
    }

    private static void AddRow(TableLayoutPanel layout, int row, string label, Control control)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 8, 18, 8) }, 0, row);
        control.Margin = new Padding(0, 4, 0, 4);
        layout.Controls.Add(control, 1, row);
    }

    private static Control WithSuffix(Control control, string suffix)
    {
        var panel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
        panel.Controls.Add(control);
        panel.Controls.Add(new Label { Text = suffix, AutoSize = true, Margin = new Padding(7, 8, 0, 0), ForeColor = TextMuted });
        return panel;
    }

    private static NumericUpDown Number(int minimum, int maximum) => new()
    {
        Minimum = minimum,
        Maximum = maximum,
        Width = 100,
        ThousandsSeparator = false,
    };

    private static NumericUpDown DecimalNumber(decimal minimum, decimal maximum, decimal increment,
        int decimalPlaces) => new()
    {
        Minimum = minimum,
        Maximum = maximum,
        Increment = increment,
        DecimalPlaces = decimalPlaces,
        Width = 100,
        ThousandsSeparator = false,
    };

    private static ComboBox FontPicker()
    {
        var picker = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Dock = DockStyle.Fill,
        };
        picker.Items.AddRange(["宋体", "微软雅黑", "等线", "黑体", "楷体", "仿宋", "Segoe UI"]);
        return picker;
    }

    private static void SelectFont(ComboBox picker, string fontFamily)
    {
        var existing = picker.Items.Cast<string>()
            .FirstOrDefault(item => item.Equals(fontFamily, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            picker.Items.Add(fontFamily);
            existing = fontFamily;
        }
        picker.SelectedItem = existing;
    }

    private static string FormatPoint(decimal value) => value.ToString("0.#",
        System.Globalization.CultureInfo.CurrentCulture);

    private static Button PrimaryButton(string text)
    {
        var button = SecondaryButton(text);
        button.BackColor = Accent;
        button.ForeColor = Color.White;
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

    private static Button SecondaryButton(string text)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(88, 34),
            Padding = new Padding(10, 4, 10, 4),
            Margin = new Padding(8, 0, 0, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = TextPrimary,
            UseVisualStyleBackColor = false,
            Cursor = Cursors.Hand,
        };
        button.FlatAppearance.BorderColor = Border;
        return button;
    }

    private sealed class TemplateStandardPreview : Control
    {
        private TypographyTemplate _template = TypographyTemplateStore.BuiltIns()[0];

        public TemplateStandardPreview()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(232, 229, 223);
            AccessibleName = "模板标准样张";
            AccessibleRole = AccessibleRole.Graphic;
            TabStop = false;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        }

        public void ShowTemplate(TypographyTemplate template)
        {
            _template = template;
            AccessibleDescription = $"{template.Name}标准样张；{template.Description}";
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            base.OnPaint(eventArgs);
            var graphics = eventArgs.Graphics;
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            graphics.Clear(BackColor);
            if (ClientSize.Width < 80 || ClientSize.Height < 100) return;

            const float a4Ratio = 210F / 297F;
            var availableWidth = ClientSize.Width - 24;
            var availableHeight = ClientSize.Height - 24;
            var pageWidth = Math.Min(availableWidth, (int)(availableHeight * a4Ratio));
            var pageHeight = Math.Min(availableHeight, (int)(pageWidth / a4Ratio));
            var page = new Rectangle(
                (ClientSize.Width - pageWidth) / 2,
                (ClientSize.Height - pageHeight) / 2,
                pageWidth,
                pageHeight);
            using var shadow = new SolidBrush(Color.FromArgb(28, 48, 43, 38));
            graphics.FillRectangle(shadow, new Rectangle(page.X + 5, page.Y + 6, page.Width, page.Height));
            using var paper = new SolidBrush(Color.FromArgb(255, 254, 252));
            graphics.FillRectangle(paper, page);
            using var border = new Pen(Color.FromArgb(210, 204, 196));
            graphics.DrawRectangle(border, page);

            var scale = Math.Max(0.52F, page.Width / 420F);
            var margin = Math.Max(12, (int)(28 * scale));
            var content = Rectangle.Inflate(page, -margin, -margin);
            var identity = (_template.Id + " " + _template.Name).ToLowerInvariant();
            if (identity.Contains("resume", StringComparison.Ordinal)
                || identity.Contains("简历", StringComparison.Ordinal))
                DrawResume(graphics, content, scale, _template.Rules);
            else if (identity.Contains("technical", StringComparison.Ordinal)
                || identity.Contains("技术", StringComparison.Ordinal)
                || identity.Contains("笔记", StringComparison.Ordinal))
                DrawTechnical(graphics, content, scale, _template.Rules);
            else
                DrawAcademic(graphics, content, scale, _template.Rules);
        }

        private static void DrawAcademic(Graphics graphics, Rectangle content, float scale, TypographyRules rules)
        {
            var y = content.Top + Px(10, scale);
            DrawText(graphics, "学术报告标准样张", content, ref y, rules.Heading1FontSizePt, scale,
                FontStyle.Bold, StringAlignment.Center, fontFamily: rules.HeadingFontFamily);
            DrawText(graphics, "课程名称 · 姓名 · 2026", content, ref y,
                Math.Max(8M, rules.BodyFontSizePt - 1M), scale, FontStyle.Regular,
                StringAlignment.Center, TextMuted, rules.BodyFontFamily);
            y += Px(8, scale);
            DrawRule(graphics, content.Left, content.Right, y, Color.FromArgb(75, 69, 62));
            y += Px(14, scale);
            if (rules.GenerateTableOfContents)
            {
                DrawText(graphics, "目录", content, ref y, rules.Heading2FontSizePt, scale,
                    FontStyle.Bold, fontFamily: rules.HeadingFontFamily);
                DrawTocLines(graphics, content, ref y, scale, 3);
                y += Px(8, scale);
            }
            DrawText(graphics, "1 研究背景与目标", content, ref y, rules.Heading2FontSizePt, scale,
                FontStyle.Bold, fontFamily: rules.HeadingFontFamily);
            DrawParagraphLines(graphics, content, ref y, scale, rules, 4);
            y += Px(7, scale);
            DrawImage(graphics, content, ref y, scale, rules.ImageMaxWidth, "图 1 研究框架");
            y += Px(9, scale);
            DrawText(graphics, "2 方法与结果", content, ref y, rules.Heading2FontSizePt, scale,
                FontStyle.Bold, fontFamily: rules.HeadingFontFamily);
            DrawTable(graphics, content, ref y, scale, rules.TableMaxWidth);
        }

        private static void DrawTechnical(Graphics graphics, Rectangle content, float scale, TypographyRules rules)
        {
            var y = content.Top + Px(4, scale);
            DrawText(graphics, "技术笔记 · Markdown", content, ref y, rules.Heading1FontSizePt, scale,
                FontStyle.Bold, StringAlignment.Near, Accent, rules.HeadingFontFamily);
            DrawText(graphics, "快速扫描 · 代码与表格优先", content, ref y,
                Math.Max(8M, rules.BodyFontSizePt - 1M), scale, FontStyle.Regular,
                StringAlignment.Near, TextMuted, rules.BodyFontFamily);
            y += Px(6, scale);
            DrawRule(graphics, content.Left, content.Right, y, Accent);
            y += Px(12, scale);
            DrawText(graphics, "01 方案概览", content, ref y, rules.Heading2FontSizePt, scale,
                FontStyle.Bold, fontFamily: rules.HeadingFontFamily);
            DrawParagraphLines(graphics, content, ref y, scale, rules, 3);
            y += Px(7, scale);
            var codeHeight = Px(54, scale);
            using (var code = new SolidBrush(Color.FromArgb(42, 46, 52)))
                graphics.FillRectangle(code, content.Left, y, content.Width, codeHeight);
            using (var codePen = new Pen(Color.FromArgb(126, 211, 167), Math.Max(1, Px(1, scale))))
            {
                for (var index = 0; index < 3; index++)
                {
                    var width = content.Width * (index == 1 ? 0.55F : 0.76F);
                    var lineY = y + Px(13 + index * 12, scale);
                    graphics.DrawLine(codePen, content.Left + Px(10, scale), lineY,
                        content.Left + Px(10, scale) + width, lineY);
                }
            }
            y += codeHeight + Px(12, scale);
            DrawText(graphics, "02 参数对照", content, ref y, rules.Heading2FontSizePt, scale,
                FontStyle.Bold, fontFamily: rules.HeadingFontFamily);
            DrawTable(graphics, content, ref y, scale, rules.TableMaxWidth);
            y += Px(10, scale);
            DrawImage(graphics, content, ref y, scale, rules.ImageMaxWidth, "运行结果示意");
        }

        private static void DrawResume(Graphics graphics, Rectangle content, float scale, TypographyRules rules)
        {
            var y = content.Top + Px(5, scale);
            DrawText(graphics, "张同学", content, ref y, rules.Heading1FontSizePt, scale,
                FontStyle.Bold, fontFamily: rules.HeadingFontFamily);
            DrawText(graphics, "产品设计 / 软件工程", content, ref y, rules.Heading3FontSizePt, scale,
                FontStyle.Regular, StringAlignment.Near, Accent, rules.HeadingFontFamily);
            DrawText(graphics, "邮箱 · 电话 · 城市", content, ref y, rules.BodyFontSizePt, scale,
                FontStyle.Regular, StringAlignment.Near, TextMuted, rules.BodyFontFamily);
            y += Px(7, scale);
            DrawRule(graphics, content.Left, content.Right, y, Accent);
            y += Px(12, scale);
            var gap = Px(16, scale);
            var leftWidth = (int)(content.Width * 0.31F);
            var left = new Rectangle(content.Left, y, leftWidth, content.Bottom - y);
            var right = new Rectangle(left.Right + gap, y, content.Right - left.Right - gap, content.Bottom - y);
            var leftY = y;
            DrawText(graphics, "教育背景", left, ref leftY, rules.Heading2FontSizePt, scale,
                FontStyle.Bold, StringAlignment.Near, Accent, rules.HeadingFontFamily);
            DrawParagraphLines(graphics, left, ref leftY, scale, rules, 3, 28);
            leftY += Px(10, scale);
            DrawText(graphics, "技能", left, ref leftY, rules.Heading2FontSizePt, scale,
                FontStyle.Bold, StringAlignment.Near, Accent, rules.HeadingFontFamily);
            DrawSkillBars(graphics, left, ref leftY, scale);
            leftY += Px(10, scale);
            DrawText(graphics, "证书与奖项", left, ref leftY, rules.Heading2FontSizePt, scale,
                FontStyle.Bold, StringAlignment.Near, Accent, rules.HeadingFontFamily);
            DrawParagraphLines(graphics, left, ref leftY, scale, rules, 3, 24);

            var rightY = y;
            DrawText(graphics, "项目经历", right, ref rightY, rules.Heading2FontSizePt, scale,
                FontStyle.Bold, fontFamily: rules.HeadingFontFamily);
            DrawText(graphics, "MarkLeaf 文档排版智能体", right, ref rightY, rules.Heading3FontSizePt,
                scale, FontStyle.Bold, fontFamily: rules.HeadingFontFamily);
            DrawParagraphLines(graphics, right, ref rightY, scale, rules, 5);
            rightY += Px(10, scale);
            DrawText(graphics, "实践经历", right, ref rightY, rules.Heading2FontSizePt, scale,
                FontStyle.Bold, fontFamily: rules.HeadingFontFamily);
            DrawParagraphLines(graphics, right, ref rightY, scale, rules, 5);
        }

        private static void DrawText(Graphics graphics, string text, Rectangle bounds, ref int y,
            decimal size, float scale, FontStyle style, StringAlignment alignment = StringAlignment.Near,
            Color? color = null, string? fontFamily = null)
        {
            var fontPixels = Math.Max(6F, (float)size * 96F / 72F * scale);
            var height = Math.Max(Px(12, scale), (int)Math.Ceiling(fontPixels * 1.45F));
            using var font = new Font(string.IsNullOrWhiteSpace(fontFamily) ? "Microsoft YaHei UI" : fontFamily,
                fontPixels, style, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(color ?? TextPrimary);
            using var format = new StringFormat { Alignment = alignment, LineAlignment = StringAlignment.Center };
            graphics.DrawString(text, font, brush, new RectangleF(bounds.Left, y, bounds.Width, height), format);
            y += height;
        }

        private static void DrawParagraphLines(Graphics graphics, Rectangle bounds, ref int y,
            float scale, TypographyRules rules, int count, int? maxLineChars = null)
        {
            var coverage = Math.Clamp((maxLineChars ?? rules.MaxLineChars) / 68F, 0.54F, 0.97F);
            var fontPixels = Math.Max(5F, (float)rules.BodyFontSizePt * 96F / 72F * scale);
            using var font = new Font(rules.BodyFontFamily, fontPixels, FontStyle.Regular, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(Color.FromArgb(88, 83, 77));
            using var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            var lineAdvance = Math.Max(Px(7, scale), (int)Math.Ceiling(fontPixels * (float)rules.LineSpacing));
            const string sample = "这是正文示例，用于观察字体、字号、行距与每行长度。";
            for (var index = 0; index < count; index++)
            {
                var factor = index == count - 1 ? 0.62F : 1F - index % 2 * 0.08F;
                var width = Math.Max(Px(24, scale), bounds.Width * coverage * factor);
                graphics.DrawString(sample, font, brush,
                    new RectangleF(bounds.Left, y, width, lineAdvance), format);
                y += lineAdvance;
            }
            y += Px((float)rules.ParagraphSpacingPt, scale);
        }

        private static void DrawTocLines(Graphics graphics, Rectangle bounds, ref int y, float scale, int count)
        {
            using var pen = new Pen(Color.FromArgb(135, 129, 121), Math.Max(1, Px(1, scale)))
            { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
            for (var index = 0; index < count; index++)
            {
                var inset = index == 1 ? Px(12, scale) : 0;
                graphics.DrawLine(pen, bounds.Left + inset, y, bounds.Right - Px(18, scale), y);
                y += Px(11, scale);
            }
        }

        private static void DrawImage(Graphics graphics, Rectangle bounds, ref int y, float scale,
            int widthPercent, string caption)
        {
            var width = Math.Max(Px(54, scale), (int)(bounds.Width * widthPercent / 100F));
            var height = Px(64, scale);
            var x = bounds.Left + (bounds.Width - width) / 2;
            using var fill = new SolidBrush(Color.FromArgb(225, 235, 229));
            using var outline = new Pen(Color.FromArgb(115, 155, 132));
            graphics.FillRectangle(fill, x, y, width, height);
            graphics.DrawRectangle(outline, x, y, width, height);
            var captionBounds = new Rectangle(x, y + height / 2 - Px(8, scale), width, Px(16, scale));
            var captionY = captionBounds.Top;
            DrawText(graphics, caption, captionBounds, ref captionY, 8, scale, FontStyle.Regular,
                StringAlignment.Center, Accent);
            y += height + Px(6, scale);
        }

        private static void DrawTable(Graphics graphics, Rectangle bounds, ref int y, float scale, int widthPercent)
        {
            var width = Math.Max(Px(90, scale), (int)(bounds.Width * widthPercent / 100F));
            var height = Px(58, scale);
            var x = bounds.Left + (bounds.Width - width) / 2;
            using var pen = new Pen(Color.FromArgb(145, 139, 131));
            using var header = new SolidBrush(Color.FromArgb(235, 232, 226));
            graphics.FillRectangle(header, x, y, width, height / 3);
            graphics.DrawRectangle(pen, x, y, width, height);
            graphics.DrawLine(pen, x, y + height / 3, x + width, y + height / 3);
            graphics.DrawLine(pen, x, y + height * 2 / 3, x + width, y + height * 2 / 3);
            graphics.DrawLine(pen, x + width / 3, y, x + width / 3, y + height);
            graphics.DrawLine(pen, x + width * 2 / 3, y, x + width * 2 / 3, y + height);
            y += height;
        }

        private static void DrawSkillBars(Graphics graphics, Rectangle bounds, ref int y, float scale)
        {
            using var track = new Pen(Color.FromArgb(218, 213, 205), Math.Max(2, Px(3, scale)));
            using var value = new Pen(Accent, Math.Max(2, Px(3, scale)));
            for (var index = 0; index < 4; index++)
            {
                graphics.DrawLine(track, bounds.Left, y, bounds.Right, y);
                graphics.DrawLine(value, bounds.Left, y,
                    bounds.Left + bounds.Width * (0.58F + index * 0.09F), y);
                y += Px(12, scale);
            }
        }

        private static void DrawRule(Graphics graphics, int left, int right, int y, Color color)
        {
            using var pen = new Pen(color, 1F);
            graphics.DrawLine(pen, left, y, right, y);
        }

        private static int Px(float logical, float scale) => Math.Max(1, (int)Math.Round(logical * scale));
    }
}
