using WorkLog.Models;

namespace WorkLog.Core;

/// <summary>ウィンドウタイトルからプロジェクト名を推定する。</summary>
public static class ProjectDetector
{
    public const string ExcludedLabel = "除外";

    /// <summary>
    /// 判定順：
    ///  1. projects.json のキーワードがタイトルに含まれる → そのプロジェクト（全アプリ共通・ブラウザもここ）
    ///  2. VS Code → 「ファイル名 - フォルダ名 - Visual Studio Code」のフォルダ名（登録名と一致すればその名前）
    ///  3. Unity → タイトル中のプロジェクト名
    ///  4. それ以外 → null（集計時に「その他（アプリ名）」）
    /// </summary>
    public static string? Detect(string processName, string title, IReadOnlyList<ProjectRule> rules)
    {
        var byKeyword = MatchKeyword(title, rules);
        if (byKeyword != null) return byKeyword;

        string? candidate = null;
        if (IsVsCode(processName, title)) candidate = ParseVsCodeFolder(title);
        else if (IsUnity(processName)) candidate = ParseUnityProject(title);

        if (string.IsNullOrWhiteSpace(candidate)) return null;

        // 抽出した名前が登録プロジェクトの名前かキーワードと一致すれば、登録名に寄せる
        foreach (var r in rules)
        {
            if (string.Equals(r.Name, candidate, StringComparison.OrdinalIgnoreCase)) return r.Name;
            foreach (var k in r.Keywords)
                if (!string.IsNullOrWhiteSpace(k) && string.Equals(k.Trim(), candidate, StringComparison.OrdinalIgnoreCase))
                    return r.Name;
        }
        return candidate;
    }

    public static string? MatchKeyword(string title, IReadOnlyList<ProjectRule> rules)
    {
        if (string.IsNullOrEmpty(title)) return null;
        // 長いキーワードを優先（"jo-splash" と "jo" が両方あるとき誤判定しにくくする）
        string? best = null;
        int bestLen = 0;
        foreach (var r in rules)
        {
            if (string.IsNullOrWhiteSpace(r.Name)) continue;
            foreach (var raw in r.Keywords)
            {
                var k = raw?.Trim();
                if (string.IsNullOrEmpty(k)) continue;
                if (k.Length > bestLen && title.Contains(k, StringComparison.OrdinalIgnoreCase))
                {
                    best = r.Name;
                    bestLen = k.Length;
                }
            }
        }
        return best;
    }

    private static bool IsVsCode(string processName, string title) =>
        processName.Equals("Code", StringComparison.OrdinalIgnoreCase) ||
        processName.Equals("Code - Insiders", StringComparison.OrdinalIgnoreCase) ||
        title.EndsWith("Visual Studio Code", StringComparison.Ordinal);

    private static bool IsUnity(string processName) =>
        processName.Equals("Unity", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 例："● Program.cs - worklog - Visual Studio Code" → "worklog"
    ///     "worklog (ワークスペース) - Visual Studio Code" → "worklog"
    /// </summary>
    public static string? ParseVsCodeFolder(string title)
    {
        var parts = SplitDash(title);
        if (parts.Count < 2) return null;
        int last = parts.Count - 1;
        if (!parts[last].Contains("Visual Studio Code", StringComparison.Ordinal)) return null;
        var folder = parts[last - 1];
        if (parts.Count == 2 && LooksLikeFile(folder)) return null; // フォルダを開いていない
        folder = StripSuffix(folder, "(Workspace)");
        folder = StripSuffix(folder, "(ワークスペース)");
        folder = folder.TrimStart('●', ' ').Trim();
        if (folder.StartsWith("[") && folder.EndsWith("]")) return null; // [管理者] など
        return folder.Length == 0 ? null : folder;
    }

    /// <summary>
    /// Unity 2021 以降："MyGame - SampleScene - Windows, Mac, Linux - Unity 2022.3.10f1 &lt;DX11&gt;" → "MyGame"
    /// Unity 2020 以前："Unity 2019.4.1f1 Personal - SampleScene.unity - MyGame - PC, Mac &amp; Linux Standalone &lt;DX11&gt;" → "MyGame"
    /// </summary>
    public static string? ParseUnityProject(string title)
    {
        var parts = SplitDash(title);
        if (parts.Count < 2) return null;
        string? name;
        if (parts[0].StartsWith("Unity ", StringComparison.Ordinal))
            name = parts.Count >= 3 ? parts[2] : null;
        else
            name = parts[0];
        name = name?.Trim();
        return string.IsNullOrEmpty(name) ? null : name;
    }

    private static List<string> SplitDash(string title) =>
        title.Split(" - ", StringSplitOptions.TrimEntries).ToList();

    private static string StripSuffix(string s, string suffix) =>
        s.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? s[..^suffix.Length].Trim() : s;

    private static bool LooksLikeFile(string s)
    {
        var t = s.TrimStart('●', ' ');
        int dot = t.LastIndexOf('.');
        return dot > 0 && dot < t.Length - 1 && t.Length - dot <= 6;
    }
}
