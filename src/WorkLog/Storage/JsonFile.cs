using System.Text.Encodings.Web;
using System.Text.Json;

namespace WorkLog.Storage;

/// <summary>JSON の読み書き。書き込みは一時ファイル経由で行い、途中で落ちても元ファイルを壊さない。</summary>
public static class JsonFile
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // 日本語をそのまま読める形で保存する（ローカル保存専用なので問題なし）
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// 読み込む。ファイルがなければ null。
    /// 壊れていた場合は削除せず「*.broken-日時」に名前を変えて退避し、null を返す。
    /// </summary>
    public static T? Read<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<T>(fs, Options);
        }
        catch (JsonException ex)
        {
            ErrorLog.Write($"JSON 読み込み失敗: {path}", ex);
            try
            {
                File.Move(path, path + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            }
            catch (Exception moveEx)
            {
                ErrorLog.Write("壊れたファイルの退避に失敗", moveEx);
            }
            return null;
        }
    }

    public static void Write<T>(string path, T value)
    {
        string dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        string tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(fs, value, Options);
            fs.Flush(flushToDisk: true);
        }
        File.Move(tmp, path, overwrite: true);
    }
}

/// <summary>%APPDATA%\WorkLog\error.log への簡易エラーログ。</summary>
public static class ErrorLog
{
    private static readonly object Gate = new();

    public static void Write(string message, Exception? ex = null)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.Root);
                var fi = new FileInfo(AppPaths.ErrorLog);
                // 肥大化防止：1MB を超えたらそれ以上書かない（消す処理は入れない）
                if (fi.Exists && fi.Length > 1024 * 1024) return;
                File.AppendAllText(AppPaths.ErrorLog,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{(ex != null ? Environment.NewLine + ex : "")}{Environment.NewLine}");
            }
        }
        catch
        {
            // ログ書き込みの失敗は無視する
        }
    }
}
