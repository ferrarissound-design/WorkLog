using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorkLog.Models;

/// <summary>1件の作業記録（同じウィンドウが続いた区間）。</summary>
public sealed class LogEntry
{
    [JsonPropertyName("start")] public DateTimeOffset Start { get; set; }
    [JsonPropertyName("end")] public DateTimeOffset End { get; set; }
    [JsonPropertyName("process")] public string Process { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    /// <summary>判定できたプロジェクト名。判定できなければ null（＝その他）。</summary>
    [JsonPropertyName("project")] public string? Project { get; set; }

    [JsonIgnore] public TimeSpan Duration => End > Start ? End - Start : TimeSpan.Zero;

    /// <summary>集計に使うキー。未判定は「その他（アプリ名）」。</summary>
    [JsonIgnore]
    public string ProjectKey => !string.IsNullOrEmpty(Project) ? Project! : $"その他（{Process}）";
}

/// <summary>1日分のログファイル。</summary>
public sealed class DayLog
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("date")] public string Date { get; set; } = "";
    [JsonPropertyName("entries")] public List<LogEntry> Entries { get; set; } = new();
}

/// <summary>projects.json の1プロジェクト。</summary>
public sealed class ProjectRule
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("keywords")] public List<string> Keywords { get; set; } = new();
    [JsonPropertyName("completed")] public bool Completed { get; set; }

    /// <summary>他のアプリ（将来のランチャーなど）が追加した項目を消さずに保持する。</summary>
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>projects.json 全体。</summary>
public sealed class ProjectsFile
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("projects")] public List<ProjectRule> Projects { get; set; } = new();
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>settings.json。</summary>
public sealed class AppSettings
{
    /// <summary>この分数以上操作がなければ離席扱い。</summary>
    [JsonPropertyName("idleMinutes")] public int IdleMinutes { get; set; } = 3;
    /// <summary>この日数以上作業していないプロジェクトを放置扱い。</summary>
    [JsonPropertyName("neglectDays")] public int NeglectDays { get; set; } = 7;
    /// <summary>前面ウィンドウを確認する間隔（秒）。</summary>
    [JsonPropertyName("pollSeconds")] public int PollSeconds { get; set; } = 5;
    /// <summary>除外するアプリのプロセス名（拡張子なし・大文字小文字は区別しない）。</summary>
    [JsonPropertyName("excludedApps")]
    public List<string> ExcludedApps { get; set; } = new()
    {
        "LINE", "KeePass", "KeePassXC", "1Password", "Bitwarden",
    };

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}
