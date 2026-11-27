using System.Drawing.Drawing2D;
using WorkLog.Core;
using WorkLog.Native;

namespace WorkLog.UI;

/// <summary>状態ごとのトレイアイコンをコードで描いて作る（画像ファイル不要）。</summary>
public sealed class TrayIcons : IDisposable
{
    private readonly Dictionary<TrackerState, Icon> _icons = new();
    private readonly List<IntPtr> _handles = new();

    public TrayIcons()
    {
        _icons[TrackerState.Recording] = Make(Color.FromArgb(34, 177, 76));   // 緑
        _icons[TrackerState.Away] = Make(Color.FromArgb(150, 150, 150));      // 灰
        _icons[TrackerState.Paused] = Make(Color.FromArgb(240, 190, 20));     // 黄
    }

    public Icon this[TrackerState s] => _icons[s];

    /// <summary>アプリ本体（ウィンドウ）用のアイコン。</summary>
    public Icon AppIcon => _icons[TrackerState.Recording];

    private Icon Make(Color color)
    {
        int size = SystemInformation.SmallIconSize.Width;
        if (size < 16) size = 16;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            float pad = size * 0.08f;
            var rect = new RectangleF(pad, pad, size - pad * 2 - 1, size - pad * 2 - 1);
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, rect);
            using var pen = new Pen(Color.FromArgb(160, 0, 0, 0), Math.Max(1f, size / 16f));
            g.DrawEllipse(pen, rect);
            // 中央に「時計の針」っぽい線を入れて、ただの丸と区別しやすくする
            using var white = new Pen(Color.White, Math.Max(1.5f, size / 10f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            float c = size / 2f;
            g.DrawLine(white, c, c, c, size * 0.25f);
            g.DrawLine(white, c, c, size * 0.70f, c);
        }
        IntPtr h = bmp.GetHicon();
        _handles.Add(h);
        return Icon.FromHandle(h);
    }

    public void Dispose()
    {
        foreach (var i in _icons.Values) i.Dispose();
        foreach (var h in _handles) NativeMethods.DestroyIcon(h);
        _icons.Clear();
        _handles.Clear();
    }
}
