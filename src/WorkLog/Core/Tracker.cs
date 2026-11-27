using WorkLog.Models;
using WorkLog.Native;
using WorkLog.Storage;

namespace WorkLog.Core;

public enum TrackerState
{
    Recording,
    Away,
    Paused,
}

/// <summary>
/// 前面ウィンドウを定期的に確認して作業記録を作る。
/// Tick() は UI スレッドのタイマーから、Suspend()/Flush() は電源イベントなど別スレッドからも呼ばれるので lock で守る。
/// </summary>
public sealed class Tracker
{
    private readonly object _gate = new();
    private readonly SettingsStore _settings;
    private readonly ProjectStore _projects;

    private DateOnly _day;
    private DayLog _today;
    private LogEntry? _current;
    private DateTimeOffset? _lastTick;
    private DateTimeOffset _lastSaved = DateTimeOffset.MinValue;
    private bool _dirty;

    /// <summary>記録中の区間を書き出す間隔。クラッシュ・強制終了時に失うのは最大でこの時間分。</summary>
    private static readonly TimeSpan CheckpointInterval = TimeSpan.FromSeconds(30);

    public TrackerState State { get; private set; } = TrackerState.Recording;
    public event EventHandler? StateChanged;

    public Tracker(SettingsStore settings, ProjectStore projects)
    {
        _settings = settings;
        _projects = projects;
        _day = DateOnly.FromDateTime(DateTime.Now);
        _today = LogStore.Load(_day);
    }

    public void Tick()
    {
        TrackerState newState;
        lock (_gate)
        {
            var now = DateTimeOffset.Now;
            if (State == TrackerState.Paused) return;

            // 前回から大きく間が空いた＝スリープ復帰など。前回時点で区間を閉じる
            var maxGap = TimeSpan.FromSeconds(Math.Max(30, _settings.Data.PollSeconds * 6));
            if (_lastTick is { } prev && now - prev > maxGap) CloseCurrent(prev);
            _lastTick = now;

            RollDayIfNeeded(now);

            var idle = NativeMethods.GetIdleTime();
            if (idle >= TimeSpan.FromMinutes(_settings.Data.IdleMinutes))
            {
                // 最後に操作した時刻で閉じる（区間の開始より前にはしない）
                if (_current != null)
                {
                    var end = now - idle;
                    CloseCurrent(end < _current.Start ? _current.Start : end, exact: true);
                }
                newState = TrackerState.Away;
            }
            else
            {
                newState = TrackerState.Recording;
                var snap = ForegroundWindow.Capture();
                if (snap == null)
                {
                    CloseCurrent(now);
                }
                else
                {
                    var (process, title, project) = Classify(snap);
                    if (_current != null && _current.Process == process && _current.Title == title)
                    {
                        _current.End = now;
                        _dirty = true;
                    }
                    else
                    {
                        CloseCurrent(now);
                        _current = new LogEntry { Start = now, End = now, Process = process, Title = title, Project = project };
                        _today.Entries.Add(_current);
                        _dirty = true;
                    }
                }
            }

            if (_dirty && now - _lastSaved >= CheckpointInterval) SaveToday(now);
        }
        SetState(newState);
    }

    private (string process, string title, string? project) Classify(WindowSnapshot snap)
    {
        foreach (var ex in _settings.Data.ExcludedApps)
        {
            if (!string.IsNullOrWhiteSpace(ex) &&
                string.Equals(TrimExe(ex), snap.ProcessName, StringComparison.OrdinalIgnoreCase))
            {
                // 除外アプリはプロセス名もタイトルも残さず「除外」とだけ記録する
                return (ProjectDetector.ExcludedLabel, "", ProjectDetector.ExcludedLabel);
            }
        }
        var project = ProjectDetector.Detect(snap.ProcessName, snap.Title, _projects.Data.Projects);
        return (snap.ProcessName, snap.Title, project);
    }

    private static string TrimExe(string s)
    {
        s = s.Trim();
        return s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? s[..^4] : s;
    }

    /// <summary>日付をまたいだら、記録中の区間を0時で分割して前日分を保存する。</summary>
    private void RollDayIfNeeded(DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.LocalDateTime);
        if (today == _day) return;

        var midnight = new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), now.Offset);
        LogEntry? carry = null;
        if (_current != null)
        {
            carry = new LogEntry { Process = _current.Process, Title = _current.Title, Project = _current.Project, Start = midnight, End = midnight };
            _current.End = midnight;
            _current = null;
        }
        _today.Entries.RemoveAll(e => e.End <= e.Start);
        SafeSave(_day, _today);

        _day = today;
        _today = LogStore.Load(_day);
        if (carry != null)
        {
            _today.Entries.Add(carry);
            _current = carry;
        }
        _dirty = true;
    }

    /// <param name="exact">true なら end をそのまま終了時刻にする（離席判定で最後の操作時刻まで戻すとき）。</param>
    private void CloseCurrent(DateTimeOffset end, bool exact = false)
    {
        if (_current == null) return;
        if (exact || end > _current.End) _current.End = end;
        if (_current.End <= _current.Start) _today.Entries.Remove(_current);
        _current = null;
        _dirty = true;
        // 保存は次のチェックポイント（最大30秒後）か、Pause/Suspend でまとめて行う
    }

    private void SaveToday(DateTimeOffset now)
    {
        if (SafeSave(_day, _today))
        {
            _lastSaved = now;
            _dirty = false;
        }
    }

    private static bool SafeSave(DateOnly day, DayLog log)
    {
        try
        {
            LogStore.Save(day, log);
            return true;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("ログの保存に失敗", ex);
            return false;
        }
    }

    public void TogglePause()
    {
        if (State == TrackerState.Paused) Resume(); else Pause();
    }

    public void Pause()
    {
        lock (_gate)
        {
            CloseCurrent(DateTimeOffset.Now);
            _lastTick = null;
            if (_dirty) SaveToday(DateTimeOffset.Now);
        }
        SetState(TrackerState.Paused);
    }

    public void Resume()
    {
        lock (_gate) _lastTick = null;
        SetState(TrackerState.Recording);
        Tick();
    }

    /// <summary>スリープ・シャットダウン・終了時：記録中の区間を閉じて保存する。</summary>
    public void Suspend()
    {
        lock (_gate)
        {
            CloseCurrent(DateTimeOffset.Now);
            _lastTick = null;
            if (_dirty) SaveToday(DateTimeOffset.Now);
        }
    }

    /// <summary>今日の記録のコピー（記録中の区間を含む）。</summary>
    public List<LogEntry> SnapshotToday(out DateOnly day)
    {
        lock (_gate)
        {
            day = _day;
            return _today.Entries
                .Where(e => e.End > e.Start)
                .Select(e => new LogEntry { Start = e.Start, End = e.End, Process = e.Process, Title = e.Title, Project = e.Project })
                .ToList();
        }
    }

    private void SetState(TrackerState s)
    {
        if (State == s) return;
        State = s;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
