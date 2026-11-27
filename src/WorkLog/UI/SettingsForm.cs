using WorkLog.Core;
using WorkLog.Models;
using WorkLog.Storage;

namespace WorkLog.UI;

/// <summary>設定（一般・プロジェクト・除外アプリ）。</summary>
internal sealed class SettingsForm : Form
{
    private readonly SettingsStore _settings;
    private readonly ProjectStore _projects;

    // 一般
    private readonly NumericUpDown _idle = new() { Minimum = 1, Maximum = 240, Width = 70 };
    private readonly NumericUpDown _neglect = new() { Minimum = 1, Maximum = 3650, Width = 70 };
    private readonly CheckBox _autoStart = new() { Text = "Windows 起動時に自動で開始する", AutoSize = true };

    // プロジェクト（編集中のコピー。OK で保存）
    private readonly List<ProjectRule> _editing;
    private readonly ListBox _projectList = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly TextBox _nameBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _keywordsBox = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true };
    private readonly CheckBox _completedBox = new() { Text = "完成済み（放置一覧に出さない）", AutoSize = true };
    private bool _loadingProject;

    // 除外アプリ
    private readonly TextBox _excludedBox = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true };

    public SettingsForm(SettingsStore settings, ProjectStore projects, Icon icon)
    {
        _settings = settings;
        _projects = projects;
        _editing = projects.Data.Projects.Select(Clone).ToList();

        Text = $"WorkLog - 設定（v{TrayApp.VersionText}）";
        Icon = icon;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Font;
        Size = new Size(640, 520);
        MinimumSize = new Size(520, 420);
        MaximizeBox = false;

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(MakeGeneralTab());
        tabs.TabPages.Add(MakeProjectsTab());
        tabs.TabPages.Add(MakeExcludedTab());

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var cancel = new Button { Text = "キャンセル", AutoSize = true, DialogResult = DialogResult.Cancel };
        var ok = new Button { Text = "保存", AutoSize = true };
        ok.Click += (_, _) => { if (SaveAll()) Close(); };
        buttons.Controls.AddRange(new Control[] { cancel, ok });
        AcceptButton = null; // キーワード欄で Enter を使うので既定ボタンにしない
        CancelButton = cancel;

        Controls.Add(tabs);
        Controls.Add(buttons);

        _idle.Value = settings.Data.IdleMinutes;
        _neglect.Value = settings.Data.NeglectDays;
        _autoStart.Checked = AutoStart.IsEnabled();
        _excludedBox.Text = string.Join(Environment.NewLine, settings.Data.ExcludedApps);
        RefreshProjectList();
    }

    private static ProjectRule Clone(ProjectRule p) => new()
    {
        Name = p.Name,
        Keywords = p.Keywords.ToList(),
        Completed = p.Completed,
        Extra = p.Extra == null ? null : new(p.Extra),
    };

    private TabPage MakeGeneralTab()
    {
        var page = new TabPage("一般") { Padding = new Padding(12) };
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        t.Controls.Add(Lbl("離席とみなす無操作時間"), 0, 0);
        t.Controls.Add(_idle, 1, 0);
        t.Controls.Add(Lbl("分"), 2, 0);
        t.Controls.Add(Lbl("放置とみなす日数"), 0, 1);
        t.Controls.Add(_neglect, 1, 1);
        t.Controls.Add(Lbl("日"), 2, 1);
        t.Controls.Add(_autoStart, 0, 2);
        t.SetColumnSpan(_autoStart, 3);
        var info = Lbl($"データの保存場所：{AppPaths.Root}\n記録はこのPCの中だけに保存され、外部には送信されません。\n記録の一時停止：トレイメニュー または Ctrl+Alt+P");
        info.ForeColor = ChartStyle.SubTextColor;
        info.Padding = new Padding(0, 16, 0, 0);
        t.Controls.Add(info, 0, 3);
        t.SetColumnSpan(info, 3);
        var openFolder = new LinkLabel { Text = "保存フォルダを開く", AutoSize = true };
        openFolder.LinkClicked += (_, _) =>
        {
            try { System.Diagnostics.Process.Start("explorer.exe", $"\"{AppPaths.Root}\""); } catch { }
        };
        t.Controls.Add(openFolder, 0, 4);
        page.Controls.Add(t);
        return page;
    }

    private TabPage MakeProjectsTab()
    {
        var page = new TabPage("プロジェクト") { Padding = new Padding(8) };
        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));

        var left = new Panel { Dock = DockStyle.Fill };
        var addBtn = new Button { Text = "＋ 新しいプロジェクト", Dock = DockStyle.Bottom, Height = 30 };
        addBtn.Click += (_, _) => AddProject();
        left.Controls.Add(_projectList);
        left.Controls.Add(addBtn);

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(8, 0, 0, 0) };
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.Controls.Add(Lbl("プロジェクト名"));
        right.Controls.Add(_nameBox);
        right.Controls.Add(Lbl("キーワード（1行に1つ。ウィンドウタイトルにこれが含まれたらこのプロジェクト）"));
        right.Controls.Add(_keywordsBox);
        right.Controls.Add(_completedBox);
        var hint = Lbl("例：joukura / ジョークラ / ferrarissound-design/jo-splash\n" +
                       "VS Code はフォルダ名、Unity はプロジェクト名が自動で使われます。\n" +
                       "それがプロジェクト名かキーワードと一致すれば、このプロジェクトとして集計されます。\n" +
                       "※ 名前を変えても、過去の記録は記録した時点の名前のままです。");
        hint.ForeColor = ChartStyle.SubTextColor;
        right.Controls.Add(hint);

        split.Controls.Add(left, 0, 0);
        split.Controls.Add(right, 1, 0);
        page.Controls.Add(split);

        _projectList.SelectedIndexChanged += (_, _) => LoadSelectedProject();
        _nameBox.TextChanged += (_, _) => StoreSelectedProject();
        _keywordsBox.TextChanged += (_, _) => StoreSelectedProject();
        _completedBox.CheckedChanged += (_, _) => StoreSelectedProject();
        return page;
    }

    private TabPage MakeExcludedTab()
    {
        var page = new TabPage("除外アプリ") { Padding = new Padding(8) };
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        t.Controls.Add(Lbl("除外するアプリのプロセス名（1行に1つ。例：LINE、KeePassXC、1Password）\n" +
                           "除外したアプリはタイトルもアプリ名も保存せず「除外」とだけ記録します。\n" +
                           "プロセス名はタスクマネージャーの「詳細」タブの名前から .exe を除いたものです。"));
        t.Controls.Add(_excludedBox);
        page.Controls.Add(t);
        return page;
    }

    private static Label Lbl(string text) => new() { Text = text, AutoSize = true, Padding = new Padding(0, 6, 4, 2) };

    private void RefreshProjectList(int select = -1)
    {
        _projectList.BeginUpdate();
        _projectList.Items.Clear();
        foreach (var p in _editing) _projectList.Items.Add(Display(p));
        _projectList.EndUpdate();
        if (_editing.Count > 0) _projectList.SelectedIndex = select >= 0 && select < _editing.Count ? select : 0;
        else LoadSelectedProject();
    }

    private static string Display(ProjectRule p) =>
        (string.IsNullOrWhiteSpace(p.Name) ? "（名前なし）" : p.Name) + (p.Completed ? "　✔完成" : "");

    private void AddProject()
    {
        _editing.Add(new ProjectRule { Name = "新しいプロジェクト" });
        RefreshProjectList(_editing.Count - 1);
        _nameBox.Focus();
        _nameBox.SelectAll();
    }

    private void LoadSelectedProject()
    {
        _loadingProject = true;
        int i = _projectList.SelectedIndex;
        bool has = i >= 0 && i < _editing.Count;
        _nameBox.Enabled = _keywordsBox.Enabled = _completedBox.Enabled = has;
        _nameBox.Text = has ? _editing[i].Name : "";
        _keywordsBox.Text = has ? string.Join(Environment.NewLine, _editing[i].Keywords) : "";
        _completedBox.Checked = has && _editing[i].Completed;
        _loadingProject = false;
    }

    private void StoreSelectedProject()
    {
        if (_loadingProject) return;
        int i = _projectList.SelectedIndex;
        if (i < 0 || i >= _editing.Count) return;
        var p = _editing[i];
        p.Name = _nameBox.Text.Trim();
        p.Keywords = SplitLines(_keywordsBox.Text);
        p.Completed = _completedBox.Checked;
        string disp = Display(p);
        if ((string)_projectList.Items[i] != disp)
        {
            _loadingProject = true;
            _projectList.Items[i] = disp;
            _loadingProject = false;
        }
    }

    private static List<string> SplitLines(string text) =>
        text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private bool SaveAll()
    {
        var unnamed = _editing.FindIndex(p => string.IsNullOrWhiteSpace(p.Name));
        if (unnamed >= 0)
        {
            MessageBox.Show(this, "名前が空のプロジェクトがあります。名前を入れてください。", "WorkLog");
            _projectList.SelectedIndex = unnamed;
            return false;
        }
        var dup = _editing.GroupBy(p => p.Name.Trim(), StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (dup != null)
        {
            MessageBox.Show(this, $"「{dup.Key}」という名前のプロジェクトが複数あります。名前を変えてください。", "WorkLog");
            return false;
        }

        try
        {
            _settings.Data.IdleMinutes = (int)_idle.Value;
            _settings.Data.NeglectDays = (int)_neglect.Value;
            _settings.Data.ExcludedApps = SplitLines(_excludedBox.Text);
            _settings.Save();

            _projects.Data.Projects = _editing.Select(Clone).ToList();
            _projects.Save();

            if (_autoStart.Checked != AutoStart.IsEnabled() || (_autoStart.Checked && AutoStart.IsPathStale()))
                AutoStart.Set(_autoStart.Checked);
            return true;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("設定の保存に失敗", ex);
            MessageBox.Show(this, "保存に失敗しました：" + ex.Message, "WorkLog", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }
}
