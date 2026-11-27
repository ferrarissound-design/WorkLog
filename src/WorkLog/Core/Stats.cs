using WorkLog.Models;
using WorkLog.Storage;

namespace WorkLog.Core;

/// <summary>集計処理。</summary>
public static class Stats
{
    /// <summary>プロジェクト別合計（長い順）。</summary>
    public static List<(string Project, TimeSpan Total)> ByProject(IEnumerable<LogEntry> entries) =>
        entries.GroupBy(e => e.ProjectKey)
            .Select(g => (g.Key, TimeSpan.FromTicks(g.Sum(e => e.Duration.Ticks))))
            .OrderByDescending(x => x.Item2)
            .ToList();

    public static TimeSpan Total(IEnumerable<LogEntry> entries) =>
        TimeSpan.FromTicks(entries.Sum(e => e.Duration.Ticks));

    /// <summary>指定日の記録。今日なら記録中のデータ（メモリ上）を使う。</summary>
    public static List<LogEntry> EntriesFor(DateOnly date, Tracker tracker)
    {
        var today = tracker.SnapshotToday(out var trackerDay);
        if (date == trackerDay) return today;
        return LogStore.Load(date).Entries.Where(e => e.End > e.Start).ToList();
    }

    /// <summary>期間（両端含む）の記録をまとめて取得。</summary>
    public static List<(DateOnly Date, List<LogEntry> Entries)> EntriesForRange(DateOnly from, DateOnly to, Tracker tracker)
    {
        var list = new List<(DateOnly, List<LogEntry>)>();
        for (var d = from; d <= to; d = d.AddDays(1)) list.Add((d, EntriesFor(d, tracker)));
        return list;
    }

    /// <summary>
    /// 登録プロジェクトごとの最終作業日。ログを新しい日から順に見て、全プロジェクトが見つかった時点で止める。
    /// </summary>
    public static Dictionary<string, DateOnly> LastWorkedDates(IReadOnlyCollection<string> projectNames, Tracker tracker)
    {
        var result = new Dictionary<string, DateOnly>(StringComparer.OrdinalIgnoreCase);
        var remaining = new HashSet<string>(projectNames, StringComparer.OrdinalIgnoreCase);
        if (remaining.Count == 0) return result;

        var today = tracker.SnapshotToday(out var trackerDay);
        Mark(trackerDay, today);

        foreach (var date in LogStore.ExistingDatesDescending())
        {
            if (remaining.Count == 0) break;
            if (date >= trackerDay) continue;
            Mark(date, LogStore.Load(date).Entries);
        }
        return result;

        void Mark(DateOnly date, IEnumerable<LogEntry> entries)
        {
            foreach (var e in entries)
            {
                if (e.Project != null && e.Duration > TimeSpan.Zero && remaining.Remove(e.Project))
                    result[e.Project] = date;
            }
        }
    }

    public static string Format(TimeSpan t)
    {
        int totalMin = (int)Math.Round(t.TotalMinutes);
        if (totalMin < 60) return $"{totalMin}分";
        return $"{totalMin / 60}時間{totalMin % 60:00}分";
    }
}
