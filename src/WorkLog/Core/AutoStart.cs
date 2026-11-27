using Microsoft.Win32;
using WorkLog.Storage;

namespace WorkLog.Core;

/// <summary>Windows 起動時の自動開始（HKCU\...\Run）。</summary>
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WorkLog";

    private static string ExePath => Environment.ProcessPath ?? Application.ExecutablePath;

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) is string s && s.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>登録されているパスが今の exe と違うか（exe を移動・更新したとき）。</summary>
    public static bool IsPathStale()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) is string s && s.Length > 0 &&
                   !string.Equals(s.Trim('"'), ExePath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (enabled) key.SetValue(ValueName, $"\"{ExePath}\"");
            else if (key.GetValue(ValueName) != null) key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            ErrorLog.Write("自動起動の設定に失敗", ex);
            throw;
        }
    }
}
