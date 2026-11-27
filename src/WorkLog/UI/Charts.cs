using System.Drawing.Drawing2D;
using WorkLog.Core;
using WorkLog.Models;

namespace WorkLog.UI;

/// <summary>グラフ描画の共通部品（外部ライブラリなしで自前描画）。</summary>
internal static class ChartStyle
{
    private static readonly Color[] Palette =
    {
        Color.FromArgb(52, 120, 200), Color.FromArgb(230, 120, 40), Color.FromArgb(60, 160, 90),
        Color.FromArgb(200, 70, 80), Color.FromArgb(140, 100, 190), Color.FromArgb(40, 160, 170),
        Color.FromArgb(200, 160, 40), Color.FromArgb(180, 90, 150), Color.FromArgb(110, 140, 60),
        Color.FromArgb(90, 110, 140),
    };

    public static readonly Color OtherColor = Color.FromArgb(175, 175, 175);
    public static readonly Color ExcludedColor = Color.FromArgb(120, 120, 120);
    public static readonly Color GridColor = Color.FromArgb(225, 225, 225);
    public static readonly Color TextColor = Color.FromArgb(40, 40, 40);
    public static readonly Color SubTextColor = Color.FromArgb(110, 110, 110);

    /// <summary>プロジェクト名から毎回同じ色を決める（string.GetHashCode は起動ごとに変わるので自前ハッシュ）。</summary>
    public static Color ColorFor(string project)
    {
        if (project == ProjectDetector.ExcludedLabel) return ExcludedColor;
        if (project.StartsWith("その他（", StringComparison.Ordinal)) return OtherColor;
        uint h = 2166136261;
        foreach (char c in project) h = (h ^ c) * 16777619;
        return Palette[h % (uint)Palette.Length];
    }

    public static float Scale(Control c) => c.DeviceDpi / 96f;
}

/// <summary>ちらつかない描画用コントロール。</summary>
internal class ChartControl : Control
{
    public ChartControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.White;
    }

    protected float S => ChartStyle.Scale(this);

    /// <summary>横棒グラフを描き、描いた高さを返す。</summary>
    protected float DrawBars(Graphics g, float top, string heading, IReadOnlyList<(string Label, TimeSpan Value, Color Color)> rows)
    {
        float s = S, pad = 12 * s, y = top;
        using var headFont = new Font(Font.FontFamily, Font.Size + 1.5f, FontStyle.Bold);
        using var textBrush = new SolidBrush(ChartStyle.TextColor);
        using var subBrush = new SolidBrush(ChartStyle.SubTextColor);
        g.DrawString(heading, headFont, textBrush, pad, y);
        y += headFont.GetHeight(g) + 6 * s;

        if (rows.Count == 0)
        {
            g.DrawString("記録がありません", Font, subBrush, pad, y);
            return y + Font.GetHeight(g) + 8 * s - top;
        }

        float labelW = Math.Min(200 * s, Width * 0.35f);
        float valueW = 90 * s;
        float barX = pad + labelW + 8 * s;
        float barMaxW = Math.Max(10, Width - barX - valueW - pad);
        float rowH = 24 * s;
        double max = Math.Max(1, rows.Max(r => r.Value.TotalSeconds));
        var fmt = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap, LineAlignment = StringAlignment.Center };

        foreach (var (label, value, color) in rows)
        {
            g.DrawString(label, Font, textBrush, new RectangleF(pad, y, labelW, rowH), fmt);
            float w = (float)(value.TotalSeconds / max * barMaxW);
            using (var b = new SolidBrush(color))
                g.FillRectangle(b, barX, y + rowH * 0.2f, Math.Max(2 * s, w), rowH * 0.6f);
            g.DrawString(Stats.Format(value), Font, subBrush, new RectangleF(barX + w + 6 * s, y, valueW, rowH), fmt);
            y += rowH;
        }
        return y + 8 * s - top;
    }
}

/// <summary>1日分：プロジェクト別の横棒グラフ ＋ 時間帯タイムライン。</summary>
internal sealed class DayChart : ChartControl
{
    private List<(string Project, TimeSpan Total)> _bars = new();
    private List<LogEntry> _entries = new();
    private DateOnly _date;
    private readonly List<(RectangleF Rect, LogEntry Entry)> _hitAreas = new();
    private readonly ToolTip _tip = new() { InitialDelay = 200, ReshowDelay = 100 };
    private LogEntry? _hovered;
    private const int MaxTimelineRows = 15;

    public void SetData(DateOnly date, List<LogEntry> entries)
    {
        _date = date;
        _entries = entries;
        _bars = Stats.ByProject(entries);
        Height = (int)Measure();
        Invalidate();
    }

    private float Measure()
    {
        float s = S;
        float barsH = 40 * s + Math.Max(1, _bars.Count) * 24 * s + 8 * s;
        int rows = Math.Min(MaxTimelineRows, _bars.Count);
        float timelineH = 40 * s + 20 * s + Math.Max(1, rows) * 18 * s + 30 * s;
        return barsH + timelineH + 20 * s;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.None;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        float s = S, pad = 12 * s;

        var rows = _bars.Select(b => (b.Project, b.Total, ChartStyle.ColorFor(b.Project))).ToList();
        float y = 8 * s;
        y += DrawBars(g, y, "プロジェクト別", rows);
        y += 8 * s;
        DrawTimeline(g, y, pad);
    }

    private void DrawTimeline(Graphics g, float top, float pad)
    {
        float s = S, y = top;
        _hitAreas.Clear();
        using var headFont = new Font(Font.FontFamily, Font.Size + 1.5f, FontStyle.Bold);
        using var smallFont = new Font(Font.FontFamily, Font.Size - 1f);
        using var textBrush = new SolidBrush(ChartStyle.TextColor);
        using var subBrush = new SolidBrush(ChartStyle.SubTextColor);
        using var gridPen = new Pen(ChartStyle.GridColor);
        g.DrawString("タイムライン", headFont, textBrush, pad, y);
        y += headFont.GetHeight(g) + 6 * s;

        if (_entries.Count == 0)
        {
            g.DrawString("記録がありません", Font, subBrush, pad, y);
            return;
        }

        var dayStart = _date.ToDateTime(TimeOnly.MinValue);
        int firstHour = Math.Clamp(_entries.Min(e => e.Start.LocalDateTime.Hour), 0, 23);
        var lastEnd = _entries.Max(e => e.End.LocalDateTime);
        int lastHour = lastEnd.Date > dayStart.Date ? 24 : Math.Clamp(lastEnd.Hour + 1, firstHour + 1, 24);
        if (lastHour - firstHour < 3) lastHour = Math.Min(24, firstHour + 3);

        var projects = _bars.Take(MaxTimelineRows).Select(b => b.Project).ToList();
        bool hasMore = _bars.Count > MaxTimelineRows;
        float labelW = Math.Min(200 * s, Width * 0.35f);
        float x0 = pad + labelW + 8 * s;
        float x1 = Width - pad - 10 * s;
        float rowH = 18 * s;
        float axisY = y;
        y += smallFont.GetHeight(g) + 4 * s;
        float spanH = rowH * Math.Max(1, projects.Count);
        double totalHours = lastHour - firstHour;

        float XOf(DateTime t)
        {
            double h = (t - dayStart).TotalHours - firstHour;
            return x0 + (float)(Math.Clamp(h, 0, totalHours) / totalHours * (x1 - x0));
        }

        for (int h = firstHour; h <= lastHour; h++)
        {
            float x = XOf(dayStart.AddHours(h));
            g.DrawLine(gridPen, x, y, x, y + spanH);
            var label = h.ToString();
            var sz = g.MeasureString(label, smallFont);
            g.DrawString(label, smallFont, subBrush, x - sz.Width / 2, axisY);
        }

        var fmt = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap, LineAlignment = StringAlignment.Center };
        for (int i = 0; i < projects.Count; i++)
        {
            float ry = y + i * rowH;
            g.DrawString(projects[i], Font, textBrush, new RectangleF(pad, ry, labelW, rowH), fmt);
            using var brush = new SolidBrush(ChartStyle.ColorFor(projects[i]));
            foreach (var en in _entries.Where(en => en.ProjectKey == projects[i]))
            {
                float a = XOf(en.Start.LocalDateTime), b = XOf(en.End.LocalDateTime);
                var r = new RectangleF(a, ry + 2 * s, Math.Max(1.5f * s, b - a), rowH - 4 * s);
                g.FillRectangle(brush, r);
                _hitAreas.Add((r, en));
            }
        }
        if (hasMore)
            g.DrawString($"ほか {_bars.Count - MaxTimelineRows} 件は省略", smallFont, subBrush, pad, y + spanH + 4 * s);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        LogEntry? hit = null;
        foreach (var (rect, entry) in _hitAreas)
        {
            var r = rect;
            r.Inflate(2, 0);
            if (r.Contains(e.Location)) { hit = entry; break; }
        }
        if (hit == _hovered) return;
        _hovered = hit;
        if (hit == null) { _tip.Hide(this); return; }
        string text = $"{hit.ProjectKey}\n{hit.Start.LocalDateTime:HH:mm} 〜 {hit.End.LocalDateTime:HH:mm}（{Stats.Format(hit.Duration)}）";
        if (!string.IsNullOrEmpty(hit.Title)) text += "\n" + (hit.Title.Length > 80 ? hit.Title[..80] + "…" : hit.Title);
        _tip.Show(text, this, e.X + 12, e.Y + 16);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = null;
        _tip.Hide(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tip.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>週表示：直近7日間のプロジェクト別合計 ＋ 日別合計。</summary>
internal sealed class WeekChart : ChartControl
{
    private List<(string, TimeSpan, Color)> _projects = new();
    private List<(string, TimeSpan, Color)> _days = new();
    private string _heading = "";

    public void SetData(List<(DateOnly Date, List<LogEntry> Entries)> days)
    {
        var all = days.SelectMany(d => d.Entries).ToList();
        _projects = Stats.ByProject(all).Select(p => (p.Project, p.Total, ChartStyle.ColorFor(p.Project))).ToList();
        string[] dow = { "日", "月", "火", "水", "木", "金", "土" };
        _days = days.Select(d => ($"{d.Date:M/d}（{dow[(int)d.Date.DayOfWeek]}）", Stats.Total(d.Entries), Color.FromArgb(52, 120, 200))).ToList();
        _heading = days.Count > 0 ? $"{days[0].Date:M/d} 〜 {days[^1].Date:M/d} のプロジェクト別合計（計 {Stats.Format(Stats.Total(all))}）" : "";
        float s = S;
        Height = (int)(40 * s + Math.Max(1, _projects.Count) * 24 * s + 40 * s + _days.Count * 24 * s + 40 * s);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        float y = 8 * S;
        y += DrawBars(g, y, _heading, _projects);
        y += 8 * S;
        DrawBars(g, y, "日別合計", _days);
    }
}
