using WorkLog.Native;

namespace WorkLog.UI;

/// <summary>グローバルホットキーを受け取るための見えないウィンドウ。</summary>
public sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const int HotkeyId = 0x5701;
    private bool _registered;
    public event EventHandler? Pressed;

    public HotkeyWindow()
    {
        CreateHandle(new CreateParams());
    }

    /// <summary>Ctrl+Alt+&lt;key&gt; を登録する。他のアプリが使っていると false。</summary>
    public bool Register(Keys key)
    {
        Unregister();
        _registered = NativeMethods.RegisterHotKey(Handle, HotkeyId,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT, (uint)key);
        return _registered;
    }

    public void Unregister()
    {
        if (_registered) NativeMethods.UnregisterHotKey(Handle, HotkeyId);
        _registered = false;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
            Pressed?.Invoke(this, EventArgs.Empty);
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        Unregister();
        DestroyHandle();
    }
}
