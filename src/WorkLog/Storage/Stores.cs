using WorkLog.Models;

namespace WorkLog.Storage;

/// <summary>日別ログ（logs\YYYY-MM-DD.json）。</summary>
public static class LogStore
{
    public static DayLog Load(DateOnly date)
    {
        var log = JsonFile.Read<DayLog>(AppPaths.DayFile(date)) ?? new DayLog();
        log.Date = date.ToString("yyyy-MM-dd");
        log.Entries ??= new List<LogEntry>();
        return log;
    }

    public static void Save(DateOnly date, DayLog log)
    {
        log.Date = date.ToString("yyyy-MM-dd");
        JsonFile.Write(AppPaths.DayFile(date), log);
    }

    /// <summary>ログファイルがある日付を新しい順に返す。</summary>
    public static IEnumerable<DateOnly> ExistingDatesDescending()
    {
        if (!Directory.Exists(AppPaths.LogsDir)) yield break;
        var dates = new List<DateOnly>();
        foreach (var f in Directory.EnumerateFiles(AppPaths.LogsDir, "????-??-??.json"))
        {
            if (DateOnly.TryParseExact(Path.GetFileNameWithoutExtension(f), "yyyy-MM-dd", out var d))
                dates.Add(d);
        }
        dates.Sort();
        for (int i = dates.Count - 1; i >= 0; i--) yield return dates[i];
    }
}

/// <summary>projects.json。外部アプリ（将来のランチャー）からの書き換えも検知して読み直す。</summary>
public sealed class ProjectStore
{
    private DateTime _loadedStamp;
    public ProjectsFile Data { get; private set; } = new();
    public event EventHandler? Changed;

    public void Load()
    {
        var path = AppPaths.ProjectsFile;
        var data = JsonFile.Read<ProjectsFile>(path);
        if (data == null)
        {
            data = new ProjectsFile();
            if (!File.Exists(path)) JsonFile.Write(path, data); // 初回だけ空のファイルを作る
        }
        data.Projects ??= new List<ProjectRule>();
        foreach (var p in data.Projects) p.Keywords ??= new List<string>();
        Data = data;
        _loadedStamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : default;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Save()
    {
        JsonFile.Write(AppPaths.ProjectsFile, Data);
        _loadedStamp = File.GetLastWriteTimeUtc(AppPaths.ProjectsFile);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>ファイルが外部で更新されていれば読み直す。</summary>
    public void ReloadIfChanged()
    {
        try
        {
            var path = AppPaths.ProjectsFile;
            if (!File.Exists(path)) return;
            if (File.GetLastWriteTimeUtc(path) != _loadedStamp) Load();
        }
        catch (Exception ex)
        {
            ErrorLog.Write("projects.json の再読み込みに失敗", ex);
        }
    }
}

public sealed class SettingsStore
{
    public AppSettings Data { get; private set; } = new();
    public event EventHandler? Changed;

    public void Load()
    {
        var data = JsonFile.Read<AppSettings>(AppPaths.SettingsFile);
        if (data == null)
        {
            data = new AppSettings();
            if (!File.Exists(AppPaths.SettingsFile)) JsonFile.Write(AppPaths.SettingsFile, data);
        }
        data.ExcludedApps ??= new List<string>();
        data.IdleMinutes = Math.Clamp(data.IdleMinutes, 1, 240);
        data.NeglectDays = Math.Clamp(data.NeglectDays, 1, 3650);
        data.PollSeconds = Math.Clamp(data.PollSeconds, 1, 60);
        Data = data;
    }

    public void Save()
    {
        JsonFile.Write(AppPaths.SettingsFile, Data);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
