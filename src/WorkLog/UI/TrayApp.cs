using Microsoft.Win32;
using WorkLog.Core;
using WorkLog.Native;
using WorkLog.Storage;

namespace WorkLog.UI;

/// <summary>タスクトレイ常駐の本体。</summary>
public sealed class TrayApp : ApplicationContext
{
    private readonly SettingsStore _settings = new();
    private readonly ProjectStore _projects = new();
    private readonly Tracker _tracker;
    private readonly TrayIcons _icons = new();
    private readonly NotifyIcon _notify;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly HotkeyWindow _hotkey = new();
    private int _tickCount;
    private string _lastTooltip = "";

    public static string VersionText
    {
        get
        {
            var v = Application.ProductVersion;
            int plus = v.IndexOf('+'); // ビルドメタデータ（コミットハッシュ）は省く
            return plus >= 0 ? v[..plus] : v;
        }
    }

    public TrayApp()
    {
        AppPaths.EnsureDirectories();
        _settings.Load();
        _projects.Load();
        _tracker = new Tracker(_settings, _projects);
        _tracker.StateChanged += (_, _) => UpdateIcon();

        var menu = new ContextMenuStrip();
        var title = new ToolStripMenuItem($"WorkLog v{VersionText}") { Enabled = false };
        var summaryItem = new ToolStripMenuItem("今日のまとめ", null, (_, _) => ShowSummary());
        summaryItem.Font = new Font(summaryItem.Font, FontStyle.Bold);
        _pauseItem = new ToolStripMenuItem("一時停止 (Ctrl+Alt+P)", null, (_, _) => TogglePause());
        menu.Items.Add(title);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(summaryItem);
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new ToolStripMenuItem("設定", null, (_, _) => ShowSettings()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("終了", null, (_, _) => ExitApp()));

        _notify = new NotifyIcon
        {
            Icon = _icons[TrackerState.Recording],
            Text = "WorkLog",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _notify.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowSummary(); };

        _hotkey.Pressed += (_, _) => TogglePause();
        if (!_hotkey.Register(Keys.P))
        {
            ErrorLog.Write("ホットキー Ctrl+Alt+P の登録に失敗（他のアプリが使用中の可能性）");
            _notify.ShowBalloonTip(5000, "WorkLog", "Ctrl+Alt+P を登録できませんでした。他のアプリが使っている可能性があります。トレイメニューから一時停止できます。", ToolTipIcon.Warning);
        }

        // スリープ・シャットダウンで記録中のデータを失わないようにする
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionEnding += OnSessionEnding;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        Application.ApplicationExit += (_, _) => _tracker.Suspend();

        _timer = new System.Windows.Forms.Timer { Interval = _settings.Data.PollSeconds * 1000 };
        _timer.Tick += (_, _) => OnTick();
        _timer.Start();
        _settings.Changed += (_, _) => _timer.Interval = _settings.Data.PollSeconds * 1000;

        OnTick();
        TrimMemory();
    }

    private void OnTick()
    {
        try
        {
            _tracker.Tick();
            _tickCount++;
            // 外部アプリによる projects.json の更新を1分ごとに確認
            if (_tickCount % Math.Max(1, 60 / _settings.Data.PollSeconds) == 0) _projects.ReloadIfChanged();
            UpdateTooltip();
        }
        catch (Exception ex)
        {
            ErrorLog.Write("記録処理でエラー", ex);
        }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend) _tracker.Suspend();
    }

    private void OnSessionEnding(object? sender, SessionEndingEventArgs e) => _tracker.Suspend();

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        // 画面ロック時は区間を閉じる（ロック中は前面ウィンドウが取れないので記録されない）
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
            _tracker.Suspend();
    }

    private void TogglePause()
    {
        _tracker.TogglePause();
        UpdateTooltip();
    }

    private void UpdateIcon()
    {
        if (_notify == null) return;
        _notify.Icon = _icons[_tracker.State];
        _pauseItem.Text = _tracker.State == TrackerState.Paused ? "再開 (Ctrl+Alt+P)" : "一時停止 (Ctrl+Alt+P)";
    }

    private void UpdateTooltip()
    {
        var entries = _tracker.SnapshotToday(out _);
        var total = Stats.Total(entries.Where(e => e.Project != ProjectDetector.ExcludedLabel));
        var top = Stats.ByProject(entries).FirstOrDefault();
        string state = _tracker.State switch
        {
            TrackerState.Paused => "一時停止中",
            TrackerState.Away => "離席中",
            _ => "記録中",
        };
        string text = $"WorkLog（{state}）\n今日の合計：{Stats.Format(total)}";
        if (top.Project != null) text += $"\n最長：{top.Project}（{Stats.Format(top.Total)}）";
        if (text.Length > 127) text = text[..126] + "…"; // NotifyIcon.Text の上限
        if (text != _lastTooltip)
        {
            _notify.Text = text;
            _lastTooltip = text;
        }
    }

    private void ShowSummary()
    {
        MessageBox.Show("今日のまとめ画面は次の段階で実装します。", "WorkLog");
    }

    private void ShowSettings()
    {
        MessageBox.Show("設定画面は次の段階で実装します。", "WorkLog");
    }

    /// <summary>画面を閉じた後などに、使っていないメモリを OS に返す。</summary>
    private static void TrimMemory()
    {
        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            using var p = System.Diagnostics.Process.GetCurrentProcess();
            NativeMethods.EmptyWorkingSet(p.Handle);
        }
        catch
        {
            // 失敗しても動作には影響しない
        }
    }

    private void ExitApp()
    {
        _timer.Stop();
        _tracker.Suspend();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionEnding -= OnSessionEnding;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            _timer.Dispose();
            _hotkey.Dispose();
            _notify.Visible = false;
            _notify.Dispose();
            _icons.Dispose();
        }
        base.Dispose(disposing);
    }
}
