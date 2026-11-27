using System.Diagnostics;
using System.Text;

namespace WorkLog.Native;

/// <summary>前面ウィンドウの情報。</summary>
public sealed record WindowSnapshot(string ProcessName, string Title);

public static class ForegroundWindow
{
    // 毎回 Process を作るとゴミが出るので、直前の PID の結果を使い回す
    private static uint _lastPid;
    private static string _lastProcessName = "";

    /// <summary>前面ウィンドウを取得する。取れない場合（ロック画面など）は null。</summary>
    public static WindowSnapshot? Capture()
    {
        IntPtr hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return null;

        string processName = GetProcessName(pid);
        if (processName.Length == 0) return null;

        int len = NativeMethods.GetWindowTextLength(hwnd);
        string title = "";
        if (len > 0)
        {
            var sb = new StringBuilder(Math.Min(len, 1024) + 1);
            NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
            title = sb.ToString();
        }

        return new WindowSnapshot(processName, title);
    }

    private static string GetProcessName(uint pid)
    {
        if (pid == _lastPid && _lastProcessName.Length > 0) return _lastProcessName;
        try
        {
            using var p = Process.GetProcessById((int)pid);
            _lastPid = pid;
            _lastProcessName = p.ProcessName;
            return _lastProcessName;
        }
        catch
        {
            return "";
        }
    }
}
