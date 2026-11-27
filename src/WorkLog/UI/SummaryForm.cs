using WorkLog.Core;
using WorkLog.Storage;

namespace WorkLog.UI;

/// <summary>今日のまとめ（日表示・週表示・放置プロジェクト）。</summary>
internal sealed class SummaryForm : Form
{
    private readonly Tracker _tracker;
    private readonly ProjectStore _projects;
    private readonly SettingsStore _settings;

    private readonly DateTimePicker _datePicker = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy/MM/dd (ddd)", Width = 150 };
    private readonly Label _totalLabel = new() { AutoSize = true, Padding = new Padding(12, 6, 0, 0) };
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly DayChart _dayChart = new() { Dock = DockStyle.Top };
    private readonly WeekChart _weekChart = new() { Dock = DockStyle.Top };
    private readonly ListView _neglectList = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false };
    private readonly Label _neglectInfo = new() { Dock = DockStyle.Top, AutoSize = false, Height = 28, Padding = new Padding(4, 6, 0, 0) };
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 30_000 };

    public event EventHandler? SettingsRequested;
    public event EventHandler? CsvRequested;

    public SummaryForm(Tracker tracker, ProjectStore projects, SettingsStore settings, Icon icon)
    {
        _tracker = tracker;
        _projects = projects;
        _settings = settings;

        Text = "WorkLog - まとめ";
        Icon = icon;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Font;
        Size = new Size(820, 620);
        MinimumSize = new Size(560, 400);

        // 上部：日付切り替え
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 8, 8, 4), WrapContents = false };
        var prev = new Button { Text = "◀", Width = 36, Height = 26 };
        var next = new Button { Text = "▶", Width = 36, Height = 26 };
        var today = new Button { Text = "今日", Width = 60, Height = 26 };
        prev.Click += (_, _) => _datePicker.Value = _datePicker.Value.AddDays(-1);
        next.Click += (_, _) => _datePicker.Value = _datePicker.Value.AddDays(1);
        today.Click += (_, _) => _datePicker.Value = DateTime.Today;
        _datePicker.MaxDate = DateTime.Today.AddYears(1);
        _datePicker.Value = DateTime.Today;
        _datePicker.ValueChanged += (_, _) => RefreshAll();
        top.Controls.AddRange(new Control[] { prev, _datePicker, next, today, _totalLabel });

        // 下部：CSV・設定
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8, 4, 8, 8) };
        var settingsBtn = new Button { Text = "設定…", AutoSize = true };
        var csvBtn = new Button { Text = "CSV出力…", AutoSize = true };
        settingsBtn.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        csvBtn.Click += (_, _) => CsvRequested?.Invoke(this, EventArgs.Empty);
        bottom.Controls.AddRange(new Control[] { settingsBtn, csvBtn });

        // タブ
        _tabs.TabPages.Add(MakeScrollTab("日", _dayChart));
        _tabs.TabPages.Add(MakeScrollTab("週（直近7日）", _weekChart));
        _tabs.TabPages.Add(MakeNeglectTab());
        _tabs.SelectedIndexChanged += (_, _) => RefreshAll();

        Controls.Add(_tabs);
        Controls.Add(bottom);
        Controls.Add(top);

        _refreshTimer.Tick += (_, _) => { if (_datePicker.Value.Date == DateTime.Today) RefreshAll(); };
        _refreshTimer.Start();
        _projects.Changed += OnProjectsChanged;
        Resize += (_, _) => { _dayChart.Invalidate(); _weekChart.Invalidate(); };

        RefreshAll();
    }

    private static TabPage MakeScrollTab(string title, Control chart)
    {
        var page = new TabPage(title) { BackColor = Color.White };
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.White };
        scroll.Controls.Add(chart);
        page.Controls.Add(scroll);
        return page;
    }

    private TabPage MakeNeglectTab()
    {
        var page = new TabPage("放置プロジェクト");
        _neglectList.Columns.Add("プロジェクト", 260);
        _neglectList.Columns.Add("最後に作業した日", 160);
        _neglectList.Columns.Add("放置日数", 120);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(4) };
        var completeBtn = new Button { Text = "選択したプロジェクトを完成済みにする", AutoSize = true };
        completeBtn.Click += (_, _) => MarkSelectedCompleted();
        buttons.Controls.Add(completeBtn);

        page.Controls.Add(_neglectList);
        page.Controls.Add(buttons);
        page.Controls.Add(_neglectInfo);
        return page;
    }

    private void OnProjectsChanged(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(RefreshAll); else RefreshAll();
    }

    public void RefreshAll()
    {
        try
        {
            var date = DateOnly.FromDateTime(_datePicker.Value);
            switch (_tabs.SelectedIndex)
            {
                case 0:
                {
                    var entries = Stats.EntriesFor(date, _tracker);
                    _dayChart.SetData(date, entries);
                    _totalLabel.Text = $"合計 {Stats.Format(Stats.Total(entries.Where(e => e.Project != ProjectDetector.ExcludedLabel)))}";
                    break;
                }
                case 1:
                {
                    var days = Stats.EntriesForRange(date.AddDays(-6), date, _tracker);
                    _weekChart.SetData(days);
                    _totalLabel.Text = "";
                    break;
                }
                case 2:
                    RefreshNeglect();
                    _totalLabel.Text = "";
                    break;
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write("まとめ画面の更新に失敗", ex);
        }
    }

    private void RefreshNeglect()
    {
        int threshold = _settings.Data.NeglectDays;
        var targets = _projects.Data.Projects.Where(p => !p.Completed && !string.IsNullOrWhiteSpace(p.Name)).ToList();
        var last = Stats.LastWorkedDates(targets.Select(p => p.Name).ToList(), _tracker);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var rows = targets.Select(p =>
            {
                bool has = last.TryGetValue(p.Name, out var d);
                int days = has ? today.DayNumber - d.DayNumber : int.MaxValue;
                return (p.Name, Has: has, Date: d, Days: days);
            })
            .Where(r => r.Days >= threshold)
            .OrderByDescending(r => r.Days)
            .ThenBy(r => r.Name, StringComparer.CurrentCulture)
            .ToList();

        _neglectList.BeginUpdate();
        _neglectList.Items.Clear();
        foreach (var r in rows)
        {
            var item = new ListViewItem(r.Name) { Tag = r.Name };
            item.SubItems.Add(r.Has ? r.Date.ToString("yyyy/MM/dd") : "記録なし");
            item.SubItems.Add(r.Has ? $"{r.Days}日放置" : "—");
            _neglectList.Items.Add(item);
        }
        _neglectList.EndUpdate();

        int completed = _projects.Data.Projects.Count(p => p.Completed);
        _neglectInfo.Text = targets.Count == 0 && completed == 0
            ? "プロジェクトが登録されていません。「設定…」→「プロジェクト」から登録してください。"
            : $"最後の作業から {threshold} 日以上たったプロジェクト：{rows.Count} 件（完成済み {completed} 件は除外。日数は設定で変更できます）";
    }

    private void MarkSelectedCompleted()
    {
        if (_neglectList.SelectedItems.Count == 0) return;
        var name = (string)_neglectList.SelectedItems[0].Tag!;
        var rule = _projects.Data.Projects.FirstOrDefault(p => p.Name == name);
        if (rule == null) return;
        if (MessageBox.Show(this, $"「{name}」を完成済みにしますか？\n（放置一覧に出なくなります。設定画面からいつでも戻せます）",
                "WorkLog", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        rule.Completed = true;
        try { _projects.Save(); }
        catch (Exception ex) { ErrorLog.Write("projects.json の保存に失敗", ex); MessageBox.Show(this, "保存に失敗しました：" + ex.Message, "WorkLog"); }
    }

    public void ShowNeglectTab() => _tabs.SelectedIndex = 2;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _projects.Changed -= OnProjectsChanged;
            _refreshTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
