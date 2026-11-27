namespace WorkLog.Storage;

/// <summary>%APPDATA%\WorkLog 以下のパス。</summary>
public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WorkLog");

    public static string LogsDir => Path.Combine(Root, "logs");
    public static string ProjectsFile => Path.Combine(Root, "projects.json");
    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string ErrorLog => Path.Combine(Root, "error.log");

    public static string DayFile(DateOnly date) => Path.Combine(LogsDir, date.ToString("yyyy-MM-dd") + ".json");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogsDir);
    }
}
