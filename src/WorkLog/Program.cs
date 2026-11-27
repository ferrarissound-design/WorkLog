using WorkLog.Storage;
using WorkLog.UI;

namespace WorkLog;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // 二重起動防止（ユーザーごと）
        using var mutex = new Mutex(true, @"Local\WorkLog-SingleInstance-7f3c2a", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("WorkLog はすでに起動しています。タスクトレイを確認してください。", "WorkLog",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ErrorLog.Write("UI スレッドの例外", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ErrorLog.Write("未処理の例外", e.ExceptionObject as Exception);

        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetDefaultFont(new Font("Yu Gothic UI", 9f));

        using var app = new TrayApp();
        Application.Run(app);
    }
}
